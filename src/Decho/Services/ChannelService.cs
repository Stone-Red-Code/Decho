using Decho.Models;

using EchoHub.Client.Services;

using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Security;
using EchoHub.Core.Services;

using System.Diagnostics;

namespace Decho.Services;

internal sealed class ChannelService : IChannelService
{
    private readonly IConnectionStore _store;
    private readonly ICryptoService _crypto;

    public ChannelService(IConnectionStore store, ICryptoService crypto)
    {
        _store = store;
        _crypto = crypto;
    }

    public async Task<ChannelJoinResult> JoinWithCryptoAsync(string serverUrl, string channelName, string? password)
    {
        ChannelCryptoDto? crypto = await GetChannelCryptoAsync(serverUrl, channelName);
        bool isEncrypted = crypto is not null && crypto.IsEncrypted;

        if (isEncrypted)
        {
            MarkChannelEncrypted(serverUrl, channelName, true);
        }

        string? wirePassword = DeriveWirePassword(password, crypto);
        ChannelJoinResult result = await JoinChannelAsync(serverUrl, channelName, wirePassword);

        if (isEncrypted && !HasChannelKey(serverUrl, channelName) && password is not null)
        {
            ChannelJoinResult unlockResult = await UnlockRoomKeyAsync(
                serverUrl, channelName, password, crypto!.EncryptionSalt!, result.WrappedRoomKey ?? "");
            if (unlockResult.History.Count > 0)
            {
                result = unlockResult;
            }
        }

        return result;
    }

    public async Task<ChannelDto?> CreateChannelAsync(string serverUrl, string name, string? topic, bool isPublic, string? password = null)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected to server");

        string? wirePassword = null, saltB64 = null, wrappedKey = null;

        if (password is not null)
        {
            byte[] salt = RoomCrypto.GenerateSalt();
            RoomCrypto.DerivedKeys derived = RoomCrypto.DeriveKeys(password, salt);
            byte[] roomKey = RoomCrypto.GenerateRoomKey();
            wirePassword = derived.AuthKeyHex;
            saltB64 = Convert.ToBase64String(salt);
            wrappedKey = RoomCrypto.WrapRoomKey(roomKey, derived.KeyEncryptionKey);
            entry.Manager.RoomKeys.StoreKey(name, roomKey);
        }

        return await entry.ApiClient.CreateChannelAsync(name, topic, isPublic, wirePassword, saltB64, wrappedKey);
    }

    public async Task<ChannelJoinResult> JoinChannelAsync(string serverUrl, string channelName, string? password = null)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected to server");

        if (entry.Manager.TrackChannel(channelName))
        {
            JoinOutcome outcome = await entry.Manager.JoinChannelAsync(channelName, password);

            List<MessageModel> history = outcome.History.Select(m => MessageModelFromDto(m, entry)).ToList();

            bool isEncrypted = !string.IsNullOrEmpty(outcome.EncryptionSalt) && !string.IsNullOrEmpty(outcome.WrappedRoomKey);
            bool hasKey = entry.Manager.RoomKeys.HasKey(channelName);

            return new ChannelJoinResult(history, isEncrypted && !hasKey, outcome.EncryptionSalt, outcome.WrappedRoomKey);
        }

        bool encFlag = entry.Manager.RoomKeys.IsChannelEncrypted(channelName);
        bool hasKeyFlag = entry.Manager.RoomKeys.HasKey(channelName);

        if (encFlag && !hasKeyFlag)
        {
            JoinOutcome outcome = await entry.Manager.JoinChannelAsync(channelName, password);
            List<MessageModel> history = outcome.History.Select(m => MessageModelFromDto(m, entry)).ToList();
            return new ChannelJoinResult(history, true, outcome.EncryptionSalt, outcome.WrappedRoomKey);
        }

        List<MessageDto> existing = await entry.Manager.GetHistoryAsync(channelName);
        List<MessageModel> hist = existing.Select(m => MessageModelFromDto(m, entry)).ToList();

        return new ChannelJoinResult(hist, encFlag && !hasKeyFlag, null, null);
    }

    public async Task LeaveChannelAsync(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.Manager.LeaveChannelAsync(channelName);
        entry.Manager.UntrackChannel(channelName);
    }

    public async Task DeleteChannelAsync(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected to server");

        await entry.ApiClient.DeleteChannelAsync(channelName);
        entry.Manager.UntrackChannel(channelName);
    }

    public async Task NukeChannelAsync(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.NukeChannelAsync(channelName);
    }

    public void UpdateChannelTopic(string serverUrl, string channelName, string? topic)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        ChannelModel? channel = entry.Server.Channels.FirstOrDefault(c =>
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        if (channel is not null)
        {
            channel.Topic = topic;
        }
    }

    public async Task<ChannelCryptoDto?> GetChannelCryptoAsync(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return null;

        return await entry.ApiClient.GetChannelCryptoAsync(channelName);
    }

    public async Task<ChannelJoinResult> UnlockRoomKeyAsync(string serverUrl, string channelName, string passphrase, string encryptionSalt, string wrappedRoomKey)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected to server");

        if (!entry.Manager.RoomKeys.IsChannelEncrypted(channelName))
        {
            return new ChannelJoinResult([], false, null, null);
        }

        if (string.IsNullOrEmpty(encryptionSalt))
        {
            throw new InvalidOperationException("Encryption salt not available for this channel");
        }

        if (string.IsNullOrEmpty(wrappedRoomKey))
        {
            throw new InvalidOperationException("Wrapped room key not available. Re-join the channel to obtain it.");
        }

        byte[] salt = Convert.FromBase64String(encryptionSalt);
        RoomCrypto.DerivedKeys derived = RoomCrypto.DeriveKeys(passphrase, salt);

        if (!entry.Manager.RoomKeys.TryStoreFromEnvelope(channelName, wrappedRoomKey, derived.KeyEncryptionKey))
        {
            throw new InvalidOperationException("Wrong passphrase");
        }

        List<MessageDto> history = await entry.Manager.GetHistoryAsync(channelName);
        return new ChannelJoinResult(history.Select(m => MessageModelFromDto(m, entry)).ToList(), false, null, null);
    }

    public void MarkChannelEncrypted(string serverUrl, string channelName, bool isEncrypted)
        => _crypto.MarkChannelEncrypted(serverUrl, channelName, isEncrypted);

    public bool HasChannelKey(string serverUrl, string channelName)
        => _crypto.HasChannelKey(serverUrl, channelName);

    public void AddChannelToList(string serverUrl, ChannelDto channelDto)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        ChannelModel channel = new ChannelModel(
            channelDto.Id.ToString(),
            channelDto.Name,
            [],
            channelDto.Topic,
            channelDto.IsPublic,
            channelDto.IsProtected,
            channelDto.IsEncrypted,
            channelDto.IsSystem);
        entry.Server.Channels.Add(channel);
    }

    public void RemoveChannelFromList(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        ChannelModel? channel = entry.Server.Channels.FirstOrDefault(c =>
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        if (channel is not null)
        {
            _ = entry.Server.Channels.Remove(channel);
        }
    }

    public async Task<List<ChannelDto>> GetChannelsAsync(string serverUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return [];

        return await entry.ApiClient.GetChannelsAsync();
    }

    public async Task SendMessageAsync(string serverUrl, string channelName, string content, Guid? replyToMessageId = null)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected to server");

        await entry.Manager.SendMessageAsync(channelName, content, replyToMessageId);
    }

    public async Task SendMessageWithAttachmentsAsync(string serverUrl, string channelName, string content, IReadOnlyList<string> filePaths, string? size = null)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected to server");

        entry.Manager.RoomKeys.TryGetKey(channelName, out byte[]? roomKey);
        List<OutgoingAttachment> attachments = new List<OutgoingAttachment>(filePaths.Count);

        foreach (string filePath in filePaths)
        {
            OutgoingAttachment attachment = await BuildAttachmentAsync(filePath, roomKey, size);
            attachments.Add(attachment);
        }

        _ = await entry.ApiClient.SendMessageWithAttachmentsAsync(channelName, content, attachments, size);
    }

    public async Task UploadFileAsync(string serverUrl, string channelName, string filePath, string? size)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        entry.Manager.RoomKeys.TryGetKey(channelName, out byte[]? roomKey);
        OutgoingAttachment attachment = await BuildAttachmentAsync(filePath, roomKey, size);

        _ = await entry.ApiClient.SendMessageWithAttachmentsAsync(channelName, "", [attachment], size);
    }

    public async Task SendUrlAsync(string serverUrl, string channelName, string url, string? size)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        _ = await entry.ApiClient.SendUrlAsync(channelName, url, size);
    }

    public async Task<List<MessageModel>> GetHistoryAsync(string serverUrl, string channelName, int count = HubConstants.DefaultHistoryCount, int offset = 0)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return [];

        List<MessageDto> messages = await entry.Manager.GetHistoryAsync(channelName, count, offset);
        return messages.Select(m => MessageModelFromDto(m, entry)).ToList();
    }

    private static async Task<OutgoingAttachment> BuildAttachmentAsync(string filePath, byte[]? roomKey, string? size)
    {
        string fileName = Path.GetFileName(filePath);
        byte[] bytes = await File.ReadAllBytesAsync(filePath);

        if (roomKey is null || roomKey.Length == 0)
        {
            return new OutgoingAttachment(new MemoryStream(bytes), fileName);
        }

        string declaredKind;
        string? preview = null;

        await using (MemoryStream ms = new MemoryStream(bytes))
        {
            if (FileValidationHelper.IsValidImage(ms))
            {
                declaredKind = "image";
                (int w, int h) = ImageToAsciiService.GetDimensions(size);
                ms.Position = 0;
                preview = RoomCrypto.EncryptText(new ImageToAsciiService().ConvertToAscii(ms, w, h), roomKey);
            }
            else
            {
                declaredKind = FileValidationHelper.IsAudioFile(fileName) ? "audio" : "file";
            }
        }

        byte[] encryptedBlob = RoomCrypto.EncryptBytes(bytes, roomKey);
        return new OutgoingAttachment(new MemoryStream(encryptedBlob), fileName, declaredKind, preview);
    }

    private static string? DeriveWirePassword(string? password, ChannelCryptoDto? crypto)
    {
        if (password is null || crypto?.EncryptionSalt is null) return password;
        byte[] salt = Convert.FromBase64String(crypto.EncryptionSalt);
        return RoomCrypto.DeriveKeys(password, salt).AuthKeyHex;
    }

    private static MessageModel MessageModelFromDto(MessageDto dto, ServerConnection entry)
    {
        UserModel author = new UserModel(
            dto.SenderUsername,
            dto.SenderDisplayName ?? dto.SenderUsername,
            dto.SenderNicknameColor);

        List<AttachmentDto> attachments = dto.Attachments ?? [];

        return new MessageModel(
            dto.Id.ToString("N"),
            author,
            dto.SentAt,
            dto.Content,
            dto.ChannelName,
            entry.Server.ServerUrl,
            attachments,
            dto.ReplyTo);
    }
}
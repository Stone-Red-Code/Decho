using Decho.Models;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;
using EchoHub.Core.Security;

namespace Decho.Services;

public interface IChannelService
{
    Task<ChannelJoinResult> JoinWithCryptoAsync(string serverUrl, string channelName, string? password);

    Task<ChannelDto?> CreateChannelAsync(string serverUrl, string name, string? topic, bool isPublic, string? password = null);

    Task<ChannelJoinResult> JoinChannelAsync(string serverUrl, string channelName, string? password = null);

    Task LeaveChannelAsync(string serverUrl, string channelName);

    Task DeleteChannelAsync(string serverUrl, string channelName);

    Task NukeChannelAsync(string serverUrl, string channelName);

    void UpdateChannelTopic(string serverUrl, string channelName, string? topic);

    Task<ChannelCryptoDto?> GetChannelCryptoAsync(string serverUrl, string channelName);

    Task<ChannelJoinResult> UnlockRoomKeyAsync(string serverUrl, string channelName, string passphrase, string encryptionSalt, string wrappedRoomKey);

    void MarkChannelEncrypted(string serverUrl, string channelName, bool isEncrypted);

    bool HasChannelKey(string serverUrl, string channelName);

    void AddChannelToList(string serverUrl, ChannelDto channelDto);

    void RemoveChannelFromList(string serverUrl, string channelName);

    Task<List<ChannelDto>> GetChannelsAsync(string serverUrl);

    Task SendMessageAsync(string serverUrl, string channelName, string content, Guid? replyToMessageId = null);

    Task SendMessageWithAttachmentsAsync(string serverUrl, string channelName, string content, IReadOnlyList<string> filePaths, string? size = null);

    Task UploadFileAsync(string serverUrl, string channelName, string filePath, string? size);

    Task SendUrlAsync(string serverUrl, string channelName, string url, string? size);

    Task<List<MessageModel>> GetHistoryAsync(string serverUrl, string channelName, int count = 50, int offset = 0);
}

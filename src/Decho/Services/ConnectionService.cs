using Decho.Models;

using EchoHub.Client.Config;
using EchoHub.Client.Services;
using EchoHub.Client.UI.Dialogs;
using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;
using EchoHub.Core.Services;

using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Decho.Services;

public sealed class ChannelJoinResult
{
    public List<MessageModel> History { get; }
    public bool IsEncrypted { get; }
    public string? EncryptionSalt { get; }
    public string? WrappedRoomKey { get; }

    public ChannelJoinResult(List<MessageModel> history, bool isEncrypted = false, string? encryptionSalt = null, string? wrappedRoomKey = null)
    {
        History = history;
        IsEncrypted = isEncrypted;
        EncryptionSalt = encryptionSalt;
        WrappedRoomKey = wrappedRoomKey;
    }
}

public sealed class ConnectionService : IConnectionService
{
    public event Action<ServerModel>? ServerAdded;

    public event Action<string>? ServerRemoved;

    public event Action<ServerModel>? ServerStateChanged;

    public event Action<string, ChannelModel>? ChannelAdded;

    public event Action<string, string>? ChannelRemoved;

    public event Action<string, MessageModel>? MessageReceived;

    public event Action<string, string, string?>? UserJoined;

    public event Action<string, string>? UserLeft;

    public event Action<string, string>? ErrorOccurred;

    public event Action<string, string>? ChannelDeleted;

    private readonly Dictionary<string, ServerConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly IConfigPersistenceService _config;
    private readonly IAttachmentService _attachment;
    private readonly ConnectionStore _store;
    private readonly IChannelService _channelService;

    internal IReadOnlyDictionary<string, ServerConnection> Connections => _connections;
    internal IConnectionStore Store => _store;

    public ConnectionService()
        : this(new ConfigPersistenceService())
    {
    }

    internal ConnectionService(IConfigPersistenceService config)
    {
        _config = config;
        _store = new ConnectionStore(_connections);
        ICryptoService crypto = new CryptoService(_store);
        _attachment = new AttachmentService(_store);
        _channelService = new ChannelService(_store, crypto);
    }

    // ── Connection lifecycle ──────────────────────────────────────────────

    public async Task<ServerModel> ConnectAsync(string serverUrl, string username, string password, bool isRegister, bool rememberMe)
    {
        ConnectDialogResult dialogResult = new ConnectDialogResult(
            serverUrl, username, password, isRegister, rememberMe, null);
        return await ConnectCoreAsync(dialogResult);
    }

    public async Task ConnectWithSavedTokenAsync(string serverUrl, string username, string refreshToken, bool rememberMe)
    {
        ConnectDialogResult dialogResult = new ConnectDialogResult(
            serverUrl, username, "", false, rememberMe, refreshToken);
        _ = await ConnectCoreAsync(dialogResult);
    }

    public async Task DisconnectAsync(string serverUrl)
    {
        ServerConnection? entry = await CleanupConnectionAsync(serverUrl);
        if (entry is not null)
        {
            ServerStateChanged?.Invoke(entry.Server);
        }
    }

    public async Task RemoveServerAsync(string serverUrl)
    {
        _ = await CleanupConnectionAsync(serverUrl);
        RemoveServerFromConfig(serverUrl);
        ServerRemoved?.Invoke(serverUrl);
    }

    // ── Non-delegated helpers ─────────────────────────────────────────────

    public string? GetRefreshToken(string serverUrl)
    {
        return _connections.TryGetValue(serverUrl, out ServerConnection? entry)
            ? entry.ApiClient.RefreshToken
            : null;
    }

    public async Task<string?> DownloadAttachmentAsync(string serverUrl, string channelName, string relativeUrl, string fileName)
        => await _attachment.DownloadAttachmentAsync(serverUrl, channelName, relativeUrl, fileName);

    public async Task<byte[]?> DownloadImageBytesAsync(string serverUrl, string channelName, string relativeUrl)
        => await _attachment.DownloadImageBytesAsync(serverUrl, channelName, relativeUrl);

    public void Dispose()
    {
        foreach ((string _, ServerConnection? entry) in _connections)
        {
            entry.ApiClient.Dispose();
        }
        _connections.Clear();
    }

    // ── Internal helpers ──────────────────────────────────────────────────

    internal static ChannelModel ChannelModelFromDto(ChannelDto dto)
    {
        return new ChannelModel(
            dto.Id.ToString(),
            dto.Name,
            [],
            dto.Topic,
            dto.IsPublic,
            dto.IsProtected);
    }

    internal static MessageModel MessageModelFromDto(MessageDto dto, ServerConnection entry)
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

    internal ServerConnection? GetConnection(string serverUrl)
    {
        return _connections.TryGetValue(serverUrl, out ServerConnection? conn) ? conn : null;
    }

    private void RemoveServerFromConfig(string serverUrl)
    {
        _config.RemoveServerFromConfig(serverUrl);
    }

    private void RemoveFromLeftChannels(string serverUrl, string channelName)
    {
        _config.RemoveFromLeftChannels(serverUrl, channelName);
    }

    private async Task<ServerModel> ConnectCoreAsync(ConnectDialogResult dialogResult)
    {
        ConnectionManager conn = new ConnectionManager();

        ConnectResult result;
        try
        {
            result = await conn.ConnectAsync(dialogResult, _ => { });
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }

        LoginResponse login = result.Login;
        UserModel userModel = new UserModel(login.Username, login.DisplayName ?? login.Username, login.NicknameColor);

        ObservableCollection<ChannelModel> channels = [];
        ServerModel serverModel = new ServerModel(
            Guid.NewGuid().ToString("N"),
            new Uri(dialogResult.ServerUrl).Host,
            channels,
            dialogResult.ServerUrl,
            isConnected: true,
            connectedUser: login.Username);

        ServerConnection serverEntry = new ServerConnection(conn, conn.Api!, serverModel, userModel);

        foreach (ChannelDto ch in result.Channels)
        {
            ChannelModel channelModel = ChannelModelFromDto(ch);
            channels.Add(channelModel);
        }

        WireConnectionEvents(serverEntry, conn);

        _connections[dialogResult.ServerUrl] = serverEntry;
        SaveRefreshToken(dialogResult.ServerUrl, dialogResult.RememberMe);
        ServerAdded?.Invoke(serverModel);

        AutoJoinRemainingChannels(dialogResult.ServerUrl, result.Channels);

        return serverModel;
    }

    private async void AutoJoinRemainingChannels(string serverUrl, List<ChannelDto> channels)
    {
        ClientConfig config = ConfigManager.Load();
        List<string> leftChannels = config.SavedServers
            .FirstOrDefault(s => string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase))
            ?.LeftChannels ?? [];

        foreach (ChannelDto ch in channels)
        {
            if (string.Equals(ch.Name, HubConstants.DefaultChannel, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (leftChannels.Contains(ch.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                _ = await _channelService.JoinChannelAsync(serverUrl, ch.Name);
                RemoveFromLeftChannels(serverUrl, ch.Name);
            }
            catch (EchoHub.Client.Services.ChannelPasswordRequiredException)
            {
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Auto-join failed for {ch.Name}: {ex.Message}");
            }
        }
    }

    private async Task<ServerConnection?> CleanupConnectionAsync(string serverUrl)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return null;
        }

        entry.Server.IsConnected = false;
        entry.Server.IsConnecting = false;

        await entry.Manager.CleanupAsync();
        entry.ApiClient.Dispose();
        await entry.Manager.DisposeAsync();

        _ = _connections.Remove(serverUrl);
        return entry;
    }

    private void SaveRefreshToken(string serverUrl, bool rememberMe)
    {
        if (!rememberMe)
        {
            return;
        }

        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        string? token = entry.ApiClient.RefreshToken;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        _config.SaveRefreshToken(serverUrl, token, entry.User.Id);
    }

    private void WireConnectionEvents(ServerConnection entry, ConnectionManager conn)
    {
        conn.MessageReceived += message =>
        {
            MessageModel msg = MessageModelFromDto(message, entry);
            MessageReceived?.Invoke(entry.Server.ServerUrl, msg);
        };

        conn.UserJoined += (channelName, username, presence) =>
        {
            UserJoined?.Invoke(entry.Server.ServerUrl, channelName, username);
        };

        conn.UserLeft += (channelName, username) =>
        {
            UserLeft?.Invoke(entry.Server.ServerUrl, channelName);
        };

        conn.UserStatusChanged += presence =>
        {
            entry.User.Status = presence.Status;
            entry.User.StatusMessage = presence.StatusMessage;
        };

        conn.ChannelDeleted += channelName =>
        {
            ChannelDeleted?.Invoke(entry.Server.ServerUrl, channelName);
        };

        conn.ChannelUpdated += channel =>
        {
            ChannelModel? existing = entry.Server.Channels.FirstOrDefault(c =>
                string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                _ = existing.Topic = channel.Topic;
            }
            else
            {
                ChannelModel model = new ChannelModel(
                    channel.Id.ToString(),
                    channel.Name,
                    [],
                    channel.Topic,
                    channel.IsPublic,
                    channel.IsProtected);
                entry.Server.Channels.Add(model);
                ChannelAdded?.Invoke(entry.Server.ServerUrl, model);
            }
        };

        conn.ForceDisconnected += reason =>
        {
            entry.Server.IsConnected = false;
            ServerStateChanged?.Invoke(entry.Server);
            ErrorOccurred?.Invoke(entry.Server.ServerUrl, reason);
        };

        conn.Error += error =>
        {
            ErrorOccurred?.Invoke(entry.Server.ServerUrl, error);
        };

        conn.ConnectionStatusChanged += status =>
        {
            entry.Server.IsConnected = status == "Connected";
            entry.Server.IsConnecting = status is "Connecting..." or "Authenticating..." or "Reconnecting...";
            ServerStateChanged?.Invoke(entry.Server);
        };
    }
}
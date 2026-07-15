using Decho.Models;

using EchoHub.Client.Commands;
using EchoHub.Client.Config;
using EchoHub.Client.Services;
using EchoHub.Client.UI.Dialogs;
using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using System.Collections.ObjectModel;

namespace Decho.Services;

public sealed class ConnectionService : IDisposable
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

    private readonly Dictionary<string, ServerConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyDictionary<string, ServerConnection> Connections => _connections;

    public async Task<ServerModel> ConnectAsync(string serverUrl, string username, string password, bool isRegister, bool rememberMe)
    {
        ConnectionManager conn = new ConnectionManager();

        ConnectDialogResult dialogResult = new EchoHub.Client.UI.Dialogs.ConnectDialogResult(
            serverUrl, username, password, isRegister, rememberMe, null);

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
        UserModel userModel = new UserModel(login.Username, login.DisplayName ?? login.Username,
            login.NicknameColor);

        ObservableCollection<ChannelModel> channels = [];
        ServerModel serverModel = new ServerModel(
            Guid.NewGuid().ToString("N"),
            new Uri(serverUrl).Host,
            channels,
            serverUrl,
            isConnected: true,
            connectedUser: login.Username);

        ServerConnection serverEntry = new ServerConnection(conn, conn.Api!, serverModel, userModel);

        foreach (ChannelDto ch in result.Channels)
        {
            ChannelModel channelModel = ChannelModelFromDto(ch);
            channels.Add(channelModel);
        }

        WireConnectionEvents(serverEntry, conn);

        _connections[serverUrl] = serverEntry;
        SaveRefreshToken(serverUrl, rememberMe);
        ServerAdded?.Invoke(serverModel);

        return serverModel;
    }

    public async Task ConnectWithSavedTokenAsync(string serverUrl, string username, string refreshToken, bool rememberMe)
    {
        ConnectionManager conn = new ConnectionManager();

        ConnectDialogResult dialogResult = new EchoHub.Client.UI.Dialogs.ConnectDialogResult(
            serverUrl, username, "", false, rememberMe, refreshToken);

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
            new Uri(serverUrl).Host,
            channels,
            serverUrl,
            isConnected: true,
            connectedUser: login.Username);

        ServerConnection serverEntry = new ServerConnection(conn, conn.Api!, serverModel, userModel);

        foreach (ChannelDto ch in result.Channels)
        {
            ChannelModel channelModel = ChannelModelFromDto(ch);
            channels.Add(channelModel);
        }

        WireConnectionEvents(serverEntry, conn);

        _connections[serverUrl] = serverEntry;
        SaveRefreshToken(serverUrl, rememberMe);
        ServerAdded?.Invoke(serverModel);
    }

    public async Task DisconnectAsync(string serverUrl)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        entry.Server.IsConnected = false;
        entry.Server.IsConnecting = false;

        await entry.Manager.CleanupAsync();
        entry.ApiClient.Dispose();
        await entry.Manager.DisposeAsync();

        _ = _connections.Remove(serverUrl);
        ServerStateChanged?.Invoke(entry.Server);
    }

    public async Task RemoveServerAsync(string serverUrl)
    {
        if (_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            entry.Server.IsConnected = false;
            entry.Server.IsConnecting = false;

            await entry.Manager.CleanupAsync();
            entry.ApiClient.Dispose();
            await entry.Manager.DisposeAsync();

            _ = _connections.Remove(serverUrl);
        }

        RemoveServerFromConfig(serverUrl);
        ServerRemoved?.Invoke(serverUrl);
    }

    private static void RemoveServerFromConfig(string serverUrl)
    {
        ClientConfig config = ConfigManager.Load();
        SavedServer? saved = config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase));
        if (saved is not null)
        {
            _ = config.SavedServers.Remove(saved);
            ConfigManager.Save(config);
        }
    }

    public async Task SendMessageAsync(string serverUrl, string channelName, string content)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            throw new InvalidOperationException("Not connected to server");
        }

        await entry.Manager.SendMessageAsync(channelName, content);
    }

    public async Task<List<MessageModel>> JoinChannelAsync(string serverUrl, string channelName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            throw new InvalidOperationException("Not connected to server");
        }

        if (entry.Manager.TrackChannel(channelName))
        {
            List<MessageDto> history = await entry.Manager.JoinChannelAsync(channelName);
            return history.Select(m => MessageModelFromDto(m, entry)).ToList();
        }

        List<MessageDto> existing = await entry.Manager.GetHistoryAsync(channelName);
        return existing.Select(m => MessageModelFromDto(m, entry)).ToList();
    }

    public async Task LeaveChannelAsync(string serverUrl, string channelName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.Manager.LeaveChannelAsync(channelName);
    }

    public async Task<List<MessageModel>> GetHistoryAsync(string serverUrl, string channelName, int count = HubConstants.DefaultHistoryCount, int offset = 0)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return [];
        }

        List<MessageDto> history = await entry.Manager.GetHistoryAsync(channelName, count, offset);
        return history.Select(m => MessageModelFromDto(m, entry)).ToList();
    }

    public async Task<List<UserPresenceDto>> GetOnlineUsersAsync(string serverUrl, string channelName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return [];
        }

        return await entry.Manager.GetOnlineUsersAsync(channelName);
    }

    public async Task UpdateStatusAsync(string serverUrl, UserStatus status, string? statusMessage)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.Manager.UpdateStatusAsync(status, statusMessage);
        entry.User.Status = status;
        entry.User.StatusMessage = statusMessage;
    }

    public async Task<ChannelDto?> CreateChannelAsync(string serverUrl, string name, string? topic, bool isPublic)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return null;
        }

        return await entry.ApiClient.CreateChannelAsync(name, topic, isPublic);
    }

    public async Task DeleteChannelAsync(string serverUrl, string channelName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.DeleteChannelAsync(channelName);
        entry.Manager.UntrackChannel(channelName);
    }

    public async Task KickUserAsync(string serverUrl, string username, string? reason)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.KickUserAsync(username, reason);
    }

    public async Task BanUserAsync(string serverUrl, string username, string? reason)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.BanUserAsync(username, reason);
    }

    public async Task UnbanUserAsync(string serverUrl, string username)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.UnbanUserAsync(username);
    }

    public async Task MuteUserAsync(string serverUrl, string username, int? durationMinutes)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.MuteUserAsync(username, durationMinutes);
    }

    public async Task UnmuteUserAsync(string serverUrl, string username)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.UnmuteUserAsync(username);
    }

    public async Task AssignRoleAsync(string serverUrl, string username, ServerRole role)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.AssignRoleAsync(username, role);
    }

    public async Task NukeChannelAsync(string serverUrl, string channelName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await entry.ApiClient.NukeChannelAsync(channelName);
    }

    public async Task UpdateProfileAsync(string serverUrl, string? displayName, string? bio, string? nickColor)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        _ = await entry.ApiClient.UpdateProfileAsync(new UpdateProfileRequest(displayName, bio, nickColor));
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(string serverUrl, string username)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return null;
        }

        return await entry.ApiClient.GetUserProfileAsync(username);
    }

    public async Task SetAvatarAsync(string serverUrl, string target)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        _ = await AvatarHelper.UploadAsync(entry.ApiClient, target);
    }

    public async Task SendUrlAsync(string serverUrl, string channelName, string url, string? size)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        _ = await entry.ApiClient.SendUrlAsync(channelName, url, size);
    }

    public async Task UploadFileAsync(string serverUrl, string channelName, string filePath, string? size)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        await using FileStream stream = File.OpenRead(filePath);
        string fileName = Path.GetFileName(filePath);
        _ = await entry.ApiClient.UploadFileAsync(channelName, stream, fileName, size);
    }

    public void UpdateChannelTopic(string serverUrl, string channelName, string? topic)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        ChannelModel? channel = entry.Server.Channels.FirstOrDefault(c =>
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        _ = channel?.Topic = topic;
    }

    public void AddChannelToList(string serverUrl, ChannelDto channelDto)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        ChannelModel model = ChannelModelFromDto(channelDto);
        entry.Server.Channels.Add(model);
        ChannelAdded?.Invoke(serverUrl, model);
    }

    public void RemoveChannelFromList(string serverUrl, string channelName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return;
        }

        ChannelModel? channel = entry.Server.Channels.FirstOrDefault(c =>
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        if (channel is not null)
        {
            _ = entry.Server.Channels.Remove(channel);
            ChannelRemoved?.Invoke(serverUrl, channelName);
        }
    }

    public string? GetRefreshToken(string serverUrl)
    {
        return _connections.TryGetValue(serverUrl, out ServerConnection? entry)
            ? entry.ApiClient.RefreshToken
            : null;
    }

    public async Task<string?> DownloadAttachmentAsync(string serverUrl, string relativeUrl, string fileName)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return null;
        }

        try
        {
            return await entry.ApiClient.DownloadFileToTempAsync(relativeUrl, fileName);
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> DownloadImageBytesAsync(string serverUrl, string relativeUrl)
    {
        if (!_connections.TryGetValue(serverUrl, out ServerConnection? entry))
        {
            return null;
        }

        try
        {
            string? tempPath = await entry.ApiClient.DownloadFileToTempAsync(relativeUrl, "image");
            if (tempPath is null)
            {
                return null;
            }

            byte[] bytes = await File.ReadAllBytesAsync(tempPath);
            try 
            { 
                File.Delete(tempPath);
            }
            catch 
            {
                // Ignore if deletion fails
            }
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public string? GetCurrentUsername(string serverUrl)
    {
        return _connections.TryGetValue(serverUrl, out ServerConnection? entry)
            ? entry.User.DisplayName
            : null;
    }

    public void Dispose()
    {
        foreach ((string _, ServerConnection? entry) in _connections)
        {
            entry.ApiClient.Dispose();
        }
        _connections.Clear();
    }

    internal static ChannelModel ChannelModelFromDto(ChannelDto dto)
    {
        return new ChannelModel(
            dto.Id.ToString(),
            dto.Name,
            [],
            dto.Topic,
            dto.IsPublic);
    }

    internal static MessageModel MessageModelFromDto(MessageDto dto, ServerConnection entry)
    {
        UserModel author = new UserModel(
            dto.SenderUsername,
            dto.SenderUsername,
            dto.SenderNicknameColor);

        return new MessageModel(
            dto.Id.ToString("N"),
            author,
            dto.SentAt,
            dto.Content,
            dto.ChannelName,
            entry.Server.ServerUrl,
            dto.Type,
            dto.AttachmentUrl,
            dto.AttachmentFileName,
            dto.AttachmentFileSize);
    }

    internal ServerConnection? GetConnection(string serverUrl)
    {
        return _connections.TryGetValue(serverUrl, out ServerConnection? conn) ? conn : null;
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

        ClientConfig config = ConfigManager.Load();
        SavedServer? saved = config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase));
        if (saved is null)
        {
            saved = new SavedServer
            {
                Name = new Uri(serverUrl).Host,
                Url = serverUrl,
                Username = entry.User.Id,
                RememberMe = true,
                LastConnected = DateTimeOffset.Now,
            };
            config.SavedServers.Add(saved);
        }

        saved.RefreshToken = token;
        saved.LastConnected = DateTimeOffset.Now;
        ConfigManager.Save(config);
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

        conn.ChannelUpdated += channel =>
        {
            ChannelModel? existing = entry.Server.Channels.FirstOrDefault(c =>
                string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase));
            _ = existing?.Topic = channel.Topic;
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
            entry.Server.IsConnecting = status is "Connecting..." or "Authenticating...";
            ServerStateChanged?.Invoke(entry.Server);
        };
    }
}

internal sealed class ServerConnection
{
    public ApiClient ApiClient { get; }
    public ServerModel Server { get; }
    public UserModel User { get; }
    internal ConnectionManager Manager { get; }

    internal ServerConnection(ConnectionManager manager, ApiClient apiClient, ServerModel server, UserModel user)
    {
        Manager = manager;
        ApiClient = apiClient;
        Server = server;
        User = user;
    }
}
using Decho.Models;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

namespace Decho.Services;

public interface IConnectionService : IDisposable
{
    event Action<ServerModel>? ServerAdded;
    event Action<string>? ServerRemoved;
    event Action<ServerModel>? ServerStateChanged;
    event Action<string, ChannelModel>? ChannelAdded;
    event Action<string, string>? ChannelRemoved;
    event Action<string, MessageModel>? MessageReceived;
    event Action<string, string, string?>? UserJoined;
    event Action<string, string>? UserLeft;
    event Action<string, string>? ErrorOccurred;
    event Action<string, string>? ChannelDeleted;
    event Action<string>? Reconnected;
    event Action<string, string, string>? MessageDeleted;
    event Action<string, string>? ChannelNuked;
    event Action<string, string, string, string?>? UserKicked;
    event Action<string, string, string?>? UserBanned;
    event Action<string, string>? ForceDisconnected;
    event Action<string, UserPresenceDto>? UserStatusChanged;
    event Action<string, string, string?>? ChannelTopicChanged;

    Task<ServerModel> ConnectAsync(string serverUrl, string username, string password, bool isRegister, bool rememberMe, string? displayName = null, string? inviteCode = null);
    Task ConnectWithSavedTokenAsync(string serverUrl, string username, string refreshToken, bool rememberMe);
    Task DisconnectAsync(string serverUrl);

    /// <summary>Ends the session on the server and forgets the saved login, keeping the server in the list.</summary>
    Task LogoutAsync(string serverUrl);
    Task RemoveServerAsync(string serverUrl);

    string? GetRefreshToken(string serverUrl);

    Task<string?> DownloadAttachmentAsync(string serverUrl, string channelName, string relativeUrl, string fileName);
    Task<byte[]?> DownloadImageBytesAsync(string serverUrl, string channelName, string relativeUrl);
}

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

    Task<ServerModel> ConnectAsync(string serverUrl, string username, string password, bool isRegister, bool rememberMe);
    Task ConnectWithSavedTokenAsync(string serverUrl, string username, string refreshToken, bool rememberMe);
    Task DisconnectAsync(string serverUrl);
    Task RemoveServerAsync(string serverUrl);

    string? GetRefreshToken(string serverUrl);

    Task<string?> DownloadAttachmentAsync(string serverUrl, string channelName, string relativeUrl, string fileName);
    Task<byte[]?> DownloadImageBytesAsync(string serverUrl, string channelName, string relativeUrl);
}

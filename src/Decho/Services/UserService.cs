using EchoHub.Client.Services;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;
using EchoHub.Core.Services;

using System.Diagnostics;

namespace Decho.Services;

internal sealed class UserService : IUserService
{
    private static readonly HttpClient AvatarDownloadClient = new HttpClient();

    private readonly IConnectionStore _store;
    private readonly IAvatarService _avatarService;

    public UserService(IConnectionStore store, IAvatarService avatarService)
    {
        _store = store;
        _avatarService = avatarService;
    }

    public async Task UpdateStatusAsync(string serverUrl, UserStatus status, string? statusMessage)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.Manager.UpdateStatusAsync(status, statusMessage);
        entry.User.Status = status;
        entry.User.StatusMessage = statusMessage;
    }

    public async Task UpdateProfileAsync(string serverUrl, string? displayName, string? bio, string? nickColor)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        _ = await entry.ApiClient.UpdateProfileAsync(new UpdateProfileRequest(displayName, bio, nickColor));
    }

    public async Task SetAvatarAsync(string serverUrl, string target)
    {
        byte[] source = Uri.TryCreate(target, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https"
            ? await AvatarDownloadClient.GetByteArrayAsync(uri)
            : await File.ReadAllBytesAsync(target);

        using MemoryStream stream = new MemoryStream(source);
        byte[] prepared = await Task.Run(() => AvatarImageProcessor.PrepareForUpload(stream));
        await SetAvatarAsync(serverUrl, prepared);
    }

    public async Task SetAvatarAsync(string serverUrl, byte[] preparedImage)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        using MemoryStream stream = new MemoryStream(preparedImage);
        string? avatarAscii = await entry.ApiClient.UploadAvatarAsync(stream, "avatar.png");
        _avatarService.Publish(serverUrl, entry.User.Id, avatarAscii);
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(string serverUrl, string username)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return null;

        UserProfileDto? profile = await entry.ApiClient.GetUserProfileAsync(username);
        if (profile is not null)
        {
            _avatarService.Publish(serverUrl, profile.Username, profile.AvatarAscii);
        }

        return profile;
    }

    public async Task<List<UserPresenceDto>> GetOnlineUsersAsync(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return [];

        return await entry.Manager.GetOnlineUsersAsync(channelName);
    }

    public async Task KickUserAsync(string serverUrl, string username, string? reason)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.KickUserAsync(username, reason);
    }

    public async Task BanUserAsync(string serverUrl, string username, string? reason)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.BanUserAsync(username, reason);
    }

    public async Task UnbanUserAsync(string serverUrl, string username)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.UnbanUserAsync(username);
    }

    public async Task MuteUserAsync(string serverUrl, string username, int? durationMinutes)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.MuteUserAsync(username, durationMinutes);
    }

    public async Task UnmuteUserAsync(string serverUrl, string username)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.UnmuteUserAsync(username);
    }

    public async Task AssignRoleAsync(string serverUrl, string username, ServerRole role)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.AssignRoleAsync(username, role);
    }

    public string? GetCurrentUsername(string serverUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        return entry?.User.DisplayName;
    }

    public string? GetAccountUsername(string serverUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        return entry?.User.Id;
    }

    public async Task DeleteMyAccountAsync(string serverUrl, string password)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected");

        await entry.ApiClient.DeleteMyAccountAsync(password);
    }

    public async Task<string> ExportMyDataAsync(string serverUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) throw new InvalidOperationException("Not connected");

        return await entry.ApiClient.ExportMyDataAsync();
    }

    public string? GetRefreshToken(string serverUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        return entry?.ApiClient.RefreshToken;
    }
}

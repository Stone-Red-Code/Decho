using EchoHub.Client.Services;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;
using EchoHub.Core.Services;

using System.Diagnostics;

namespace Decho.Services;

internal sealed class UserService : IUserService
{
    private readonly IConnectionStore _store;

    public UserService(IConnectionStore store)
    {
        _store = store;
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
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        _ = await AvatarHelper.UploadAsync(entry.ApiClient, target);
    }

    public async Task<UserProfileDto?> GetUserProfileAsync(string serverUrl, string username)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return null;

        return await entry.ApiClient.GetUserProfileAsync(username);
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

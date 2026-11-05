using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

namespace Decho.Services;

public interface IUserService
{
    Task UpdateStatusAsync(string serverUrl, UserStatus status, string? statusMessage);

    Task UpdateProfileAsync(string serverUrl, string? displayName, string? bio, string? nickColor);

    /// <summary>Uploads an image already prepared by <see cref="AvatarImageProcessor"/>.</summary>
    Task SetAvatarAsync(string serverUrl, byte[] preparedImage);

    Task<UserProfileDto?> GetUserProfileAsync(string serverUrl, string username);

    Task<List<UserPresenceDto>> GetOnlineUsersAsync(string serverUrl, string channelName);

    Task KickUserAsync(string serverUrl, string username, string? reason);

    Task BanUserAsync(string serverUrl, string username, string? reason);

    Task UnbanUserAsync(string serverUrl, string username);

    Task MuteUserAsync(string serverUrl, string username, int? durationMinutes);

    Task UnmuteUserAsync(string serverUrl, string username);

    Task AssignRoleAsync(string serverUrl, string username, ServerRole role);

    /// <summary>The account name used for lookups and mentions. Unlike the display name, it is unique.</summary>
    string? GetCurrentUsername(string serverUrl);

    Task DeleteMyAccountAsync(string serverUrl, string password);

    Task<string> ExportMyDataAsync(string serverUrl);

    string? GetRefreshToken(string serverUrl);
}
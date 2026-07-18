using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

namespace Decho.Services;

public interface IUserService
{
    Task UpdateStatusAsync(string serverUrl, UserStatus status, string? statusMessage);

    Task UpdateProfileAsync(string serverUrl, string? displayName, string? bio, string? nickColor);

    Task SetAvatarAsync(string serverUrl, string target);

    Task<UserProfileDto?> GetUserProfileAsync(string serverUrl, string username);

    Task<List<UserPresenceDto>> GetOnlineUsersAsync(string serverUrl, string channelName);

    Task KickUserAsync(string serverUrl, string username, string? reason);

    Task BanUserAsync(string serverUrl, string username, string? reason);

    Task UnbanUserAsync(string serverUrl, string username);

    Task MuteUserAsync(string serverUrl, string username, int? durationMinutes);

    Task UnmuteUserAsync(string serverUrl, string username);

    Task AssignRoleAsync(string serverUrl, string username, ServerRole role);

    string? GetCurrentUsername(string serverUrl);

    Task DeleteMyAccountAsync(string serverUrl, string password);

    Task<string> ExportMyDataAsync(string serverUrl);

    string? GetRefreshToken(string serverUrl);
}
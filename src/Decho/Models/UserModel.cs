using EchoHub.Core.Models;

namespace Decho.Models;

public sealed class UserModel(string id, string displayName, string? nicknameColor = "#ffffff", UserStatus status = UserStatus.Online, string? statusMessage = null)
{
    public string Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public string? NicknameColor { get; set; } = nicknameColor;

    public UserStatus Status { get; set; } = status;

    public string? StatusMessage { get; set; } = statusMessage;
}
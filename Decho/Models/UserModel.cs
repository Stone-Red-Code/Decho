using EchoHub.Core.Models;

namespace Decho.Models;

public sealed class UserModel
{
    public UserModel(
        string id,
        string displayName,
        string? nicknameColor = null,
        UserStatus status = UserStatus.Online,
        string? statusMessage = null)
    {
        Id = id;
        DisplayName = displayName;
        NicknameColor = nicknameColor;
        Status = status;
        StatusMessage = statusMessage;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string? NicknameColor { get; set; }

    public UserStatus Status { get; set; }

    public string? StatusMessage { get; set; }
}

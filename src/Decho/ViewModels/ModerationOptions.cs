using EchoHub.Core.Models;

namespace Decho.ViewModels;

public sealed record ModerationOptions(
    bool CanKick,
    bool CanMute,
    bool CanUnmute,
    bool CanBan,
    bool CanUnban,
    IReadOnlyList<ServerRole> AssignableRoles,
    ServerRole? CurrentRole)
{
    public static ModerationOptions None { get; } = new(false, false, false, false, false, [], null);
}

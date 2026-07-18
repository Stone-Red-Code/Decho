using EchoHub.Core.DTOs;

namespace Decho.Services;

public interface IInviteService
{
    Task<InviteDto?> CreateInviteAsync(string serverUrl, int? maxUses, int? expiresInHours);

    Task<List<InviteDto>> GetInvitesAsync(string serverUrl);

    Task RevokeInviteAsync(string serverUrl, string code);
}

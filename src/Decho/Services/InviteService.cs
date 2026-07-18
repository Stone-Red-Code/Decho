using EchoHub.Core.DTOs;

namespace Decho.Services;

internal sealed class InviteService : IInviteService
{
    private readonly IConnectionStore _store;

    public InviteService(IConnectionStore store)
    {
        _store = store;
    }

    public async Task<InviteDto?> CreateInviteAsync(string serverUrl, int? maxUses, int? expiresInHours)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return null;

        return await entry.ApiClient.CreateInviteAsync(maxUses, expiresInHours);
    }

    public async Task<List<InviteDto>> GetInvitesAsync(string serverUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return [];

        return await entry.ApiClient.GetInvitesAsync();
    }

    public async Task RevokeInviteAsync(string serverUrl, string code)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null) return;

        await entry.ApiClient.RevokeInviteAsync(code);
    }
}

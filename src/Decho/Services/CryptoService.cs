namespace Decho.Services;

internal sealed class CryptoService : ICryptoService
{
    private readonly IConnectionStore _store;

    public CryptoService(IConnectionStore store)
    {
        _store = store;
    }

    public void MarkChannelEncrypted(string serverUrl, string channelName, bool isEncrypted)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null)
        {
            return;
        }

        entry.Manager.RoomKeys.MarkChannelEncrypted(channelName, isEncrypted);
    }

    public bool HasChannelKey(string serverUrl, string channelName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        return entry is not null && entry.Manager.RoomKeys.HasKey(channelName);
    }

    public bool TryGetRoomKey(string serverUrl, string channelName, out byte[]? key)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null)
        {
            key = null;
            return false;
        }

        return entry.Manager.RoomKeys.TryGetKey(channelName, out key);
    }
}
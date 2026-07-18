namespace Decho.Services;

internal sealed class ConnectionStore : IConnectionStore
{
    private readonly Dictionary<string, ServerConnection> _connections;

    public ConnectionStore(Dictionary<string, ServerConnection> connections)
    {
        _connections = connections;
    }

    public ServerConnection? Get(string serverUrl)
        => _connections.TryGetValue(serverUrl, out ServerConnection? entry) ? entry : null;
}
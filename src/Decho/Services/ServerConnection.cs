using Decho.Models;

using EchoHub.Client.Services;

namespace Decho.Services;

internal sealed class ServerConnection
{
    public ApiClient ApiClient { get; }
    public ServerModel Server { get; }
    public UserModel User { get; }
    internal ConnectionManager Manager { get; }

    internal ServerConnection(ConnectionManager manager, ApiClient apiClient, ServerModel server, UserModel user)
    {
        Manager = manager;
        ApiClient = apiClient;
        Server = server;
        User = user;
    }
}

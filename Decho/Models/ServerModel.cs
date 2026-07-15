using System.Collections.ObjectModel;

namespace Decho.Models;

public sealed class ServerModel
{
    public ServerModel(
        string id,
        string name,
        ObservableCollection<ChannelModel> channels,
        string serverUrl = "",
        bool isConnected = false,
        bool isConnecting = false,
        string? connectedUser = null)
    {
        Id = id;
        Name = name;
        Channels = channels;
        ServerUrl = serverUrl;
        IsConnected = isConnected;
        IsConnecting = isConnecting;
        ConnectedUser = connectedUser;
    }

    public string Id { get; }

    public string Name { get; }

    public string ServerUrl { get; set; }

    public bool IsConnected { get; set; }

    public bool IsConnecting { get; set; }

    public string? ConnectedUser { get; set; }

    public ObservableCollection<ChannelModel> Channels { get; }
}

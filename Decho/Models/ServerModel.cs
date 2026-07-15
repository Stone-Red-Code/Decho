using System.Collections.ObjectModel;

namespace Decho.Models;

public sealed class ServerModel(string id, string name, ObservableCollection<ChannelModel> channels, string serverUrl = "", bool isConnected = false, bool isConnecting = false, string? connectedUser = null)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string ServerUrl { get; set; } = serverUrl;

    public bool IsConnected { get; set; } = isConnected;

    public bool IsConnecting { get; set; } = isConnecting;

    public string? ConnectedUser { get; set; } = connectedUser;

    public ObservableCollection<ChannelModel> Channels { get; } = channels;
}
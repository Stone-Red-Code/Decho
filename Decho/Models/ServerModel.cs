using System.Collections.ObjectModel;

namespace Decho.Models;

public sealed class ServerModel
{
    public ServerModel(string id, string name, ObservableCollection<ChannelModel> channels)
    {
        Id = id;
        Name = name;
        Channels = channels;
    }

    public string Id { get; }

    public string Name { get; }

    public ObservableCollection<ChannelModel> Channels { get; }
}

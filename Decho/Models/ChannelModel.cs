using System.Collections.ObjectModel;

namespace Decho.Models;

public sealed class ChannelModel
{
    public ChannelModel(string id, string name, ObservableCollection<MessageModel> messages)
    {
        Id = id;
        Name = name;
        Messages = messages;
    }

    public string Id { get; }

    public string Name { get; }

    public ObservableCollection<MessageModel> Messages { get; }
}

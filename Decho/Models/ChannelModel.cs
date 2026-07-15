using System.Collections.ObjectModel;

namespace Decho.Models;

public sealed class ChannelModel
{
    public ChannelModel(
        string id,
        string name,
        ObservableCollection<MessageModel> messages,
        string? topic = null,
        bool isPublic = true)
    {
        Id = id;
        Name = name;
        Messages = messages;
        Topic = topic;
        IsPublic = isPublic;
    }

    public string Id { get; }

    public string Name { get; }

    public string? Topic { get; set; }

    public bool IsPublic { get; }

    public ObservableCollection<MessageModel> Messages { get; }
}

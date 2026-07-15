using System.Collections.ObjectModel;

namespace Decho.Models;

public sealed class ChannelModel(string id, string name, ObservableCollection<MessageModel> messages, string? topic = null, bool isPublic = true)
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public string? Topic { get; set; } = topic;

    public bool IsPublic { get; } = isPublic;

    public ObservableCollection<MessageModel> Messages { get; } = messages;
}
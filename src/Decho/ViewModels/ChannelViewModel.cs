using Decho.Models;

using System.Collections.ObjectModel;

namespace Decho.ViewModels;

public sealed class ChannelViewModel(ChannelModel model) : ViewModelBase
{
    public ChannelModel Model { get; } = model;

    public string Name => Model.Name;

    public string? Topic
    {
        get => Model.Topic;
        set
        {
            if (Model.Topic != value)
            {
                Model.Topic = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasTopic));
            }
        }
    }

    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);

    public bool IsPublic => Model.IsPublic;

    public ObservableCollection<MessageViewModel> Messages { get; } = new ObservableCollection<MessageViewModel>(
            model.Messages.Select(message => new MessageViewModel(message)));

    public void ClearMessages()
    {
        Model.Messages.Clear();
        Messages.Clear();
    }

    public void AddMessage(MessageModel message)
    {
        Model.Messages.Add(message);
        Messages.Add(new MessageViewModel(message));
    }
}
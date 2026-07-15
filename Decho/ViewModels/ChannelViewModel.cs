using System.Collections.ObjectModel;
using System.Linq;

using Decho.Models;

namespace Decho.ViewModels;

public sealed class ChannelViewModel : ViewModelBase
{
    public ChannelViewModel(ChannelModel model)
    {
        Model = model;
        Messages = new ObservableCollection<MessageViewModel>(
            model.Messages.Select(message => new MessageViewModel(message)));
    }

    public ChannelModel Model { get; }

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

    public ObservableCollection<MessageViewModel> Messages { get; }

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

    public void AddMessageViewModel(MessageViewModel messageViewModel)
    {
        Messages.Add(messageViewModel);
    }
}
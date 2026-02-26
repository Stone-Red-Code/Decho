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

    public ObservableCollection<MessageViewModel> Messages { get; }

    public void AddMessage(MessageModel message)
    {
        Model.Messages.Add(message);
        Messages.Add(new MessageViewModel(message));
    }
}

using System.Collections.ObjectModel;

namespace Decho.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    private ObservableCollection<MessageViewModel> _messages = new();
    private string _channelTitle = "Select a channel";

    public ChatViewModel()
    {
        Composer = new MessageComposerViewModel();
    }

    public ObservableCollection<MessageViewModel> Messages
    {
        get => _messages;
        private set => this.RaiseAndSetIfChanged(ref _messages, value);
    }

    public string ChannelTitle
    {
        get => _channelTitle;
        private set => this.RaiseAndSetIfChanged(ref _channelTitle, value);
    }

    public MessageComposerViewModel Composer { get; }

    public void SetChannel(ChannelViewModel? channel)
    {
        if (channel is null)
        {
            Messages = new ObservableCollection<MessageViewModel>();
            ChannelTitle = "Select a channel";
            return;
        }

        Messages = channel.Messages;
        ChannelTitle = "#" + channel.Name;
    }
}

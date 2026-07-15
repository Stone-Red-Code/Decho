using EchoHub.Client.Commands;

using System.Collections.ObjectModel;

namespace Decho.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    public ObservableCollection<MessageViewModel> Messages
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public string ChannelTitle
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Select a channel";

    public string? ChannelTopic
    {
        get;
        set
        {
            _ = this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(HasTopic));
        }
    }

    public bool HasTopic
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public MessageComposerViewModel Composer { get; }

    public string CurrentServerUrl { get; private set; } = string.Empty;

    public string CurrentChannelName { get; private set; } = string.Empty;

    public ChatViewModel()
    {
        Composer = new MessageComposerViewModel();
    }

    public void SetChannel(ChannelViewModel? channel, string serverUrl = "")
    {
        if (channel is null)
        {
            Messages = [];
            ChannelTitle = "Select a channel";
            ChannelTopic = null;
            HasTopic = false;
            CurrentChannelName = string.Empty;
            CurrentServerUrl = string.Empty;
            Composer.SetServer(string.Empty);
            return;
        }

        Messages = channel.Messages;
        ChannelTitle = "#" + channel.Name;
        ChannelTopic = channel.Topic;
        HasTopic = channel.HasTopic;
        CurrentChannelName = channel.Name;
        CurrentServerUrl = serverUrl;
        Composer.SetServer(serverUrl);
    }

    public void SetComposerCommandHandler(CommandHandler handler)
    {
        Composer.SetCommandHandler(handler);
    }

    public void ClearMessages()
    {
        Messages = [];
        ChannelTitle = "Select a channel";
        ChannelTopic = null;
        HasTopic = false;
    }
}
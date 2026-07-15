using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

using EchoHub.Client.Commands;
using ReactiveUI;

namespace Decho.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    private ObservableCollection<MessageViewModel> _messages = new();
    private string _channelTitle = "Select a channel";
    private string? _channelTopic;
    private bool _hasTopic;
    private string _currentServerUrl = string.Empty;
    private string _currentChannelName = string.Empty;

    public ChatViewModel()
    {
        Composer = new MessageComposerViewModel();
        Composer.CommandRequested += HandleCommandAsync;
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

    public string? ChannelTopic
    {
        get => _channelTopic;
        set
        {
            this.RaiseAndSetIfChanged(ref _channelTopic, value);
            this.RaisePropertyChanged(nameof(HasTopic));
        }
    }

    public bool HasTopic
    {
        get => _hasTopic;
        private set => this.RaiseAndSetIfChanged(ref _hasTopic, value);
    }

    public MessageComposerViewModel Composer { get; }

    public string CurrentServerUrl => _currentServerUrl;
    public string CurrentChannelName => _currentChannelName;

    public event Func<string, Task<string?>>? CommandRequested;

    public void SetChannel(ChannelViewModel? channel, string serverUrl = "")
    {
        if (channel is null)
        {
            Messages = new ObservableCollection<MessageViewModel>();
            ChannelTitle = "Select a channel";
            ChannelTopic = null;
            HasTopic = false;
            _currentChannelName = string.Empty;
            _currentServerUrl = string.Empty;
            Composer.SetServer(string.Empty);
            return;
        }

        Messages = channel.Messages;
        ChannelTitle = "#" + channel.Name;
        ChannelTopic = channel.Topic;
        HasTopic = channel.HasTopic;
        _currentChannelName = channel.Name;
        _currentServerUrl = serverUrl;
        Composer.SetServer(serverUrl);
    }

    public void SetComposerCommandHandler(CommandHandler handler)
    {
        Composer.SetCommandHandler(handler);
    }

    public void AddMessage(MessageViewModel message)
    {
        Messages.Add(message);
    }

    public void ClearMessages()
    {
        Messages = new ObservableCollection<MessageViewModel>();
        ChannelTitle = "Select a channel";
        ChannelTopic = null;
        HasTopic = false;
    }

    private async Task<string?> HandleCommandAsync(string commandText)
    {
        if (CommandRequested is not null)
        {
            return await CommandRequested(commandText);
        }
        return null;
    }
}
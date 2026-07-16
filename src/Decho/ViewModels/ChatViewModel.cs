using EchoHub.Core.DTOs;

using System.Collections.ObjectModel;
using System.Reactive;

namespace Decho.ViewModels;

public sealed class ChatViewModel : ViewModelBase
{
    public ObservableCollection<MessageViewModel> Messages
    {
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    } = [];

    public ObservableCollection<UserViewModel> OnlineUsers { get; } = [];

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

    public bool ShowOnlineUsers
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleUsersPanelCommand { get; }

    public string OnlineUserCount
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = string.Empty;

    public MessageComposerViewModel Composer { get; }

    public string CurrentServerUrl { get; private set; } = string.Empty;

    public string CurrentChannelName { get; private set; } = string.Empty;

    public ChatViewModel()
    {
        Composer = new MessageComposerViewModel();
        ToggleUsersPanelCommand = ReactiveCommand.Create(ToggleUsersPanel);
    }

    public void SetChannel(ChannelViewModel? channel, string serverUrl = "", bool isServerConnected = true)
    {
        if (channel is null)
        {
            Messages = [];
            ChannelTitle = "Select a channel";
            ChannelTopic = null;
            HasTopic = false;
            CurrentChannelName = string.Empty;
            CurrentServerUrl = string.Empty;
            ShowOnlineUsers = false;
            OnlineUsers.Clear();
            OnlineUserCount = string.Empty;
            Composer.SetServer(string.Empty, isServerConnected);
            return;
        }

        Messages = channel.Messages;
        ChannelTitle = "#" + channel.Name;
        ChannelTopic = channel.Topic;
        HasTopic = channel.HasTopic;
        CurrentChannelName = channel.Name;
        CurrentServerUrl = serverUrl;
        Composer.SetServer(serverUrl, isServerConnected);

        if (!isServerConnected)
        {
            ShowOnlineUsers = false;
            OnlineUsers.Clear();
            OnlineUserCount = string.Empty;
        }
    }

    public void SetOnlineUsers(List<UserPresenceDto> users)
    {
        OnlineUsers.Clear();
        foreach (UserPresenceDto user in users)
        {
            OnlineUsers.Add(new UserViewModel(user));
        }
        OnlineUserCount = $"{users.Count}";
        ShowOnlineUsers = true;
        Composer.UpdateAvailableUsers(OnlineUsers);
    }

    public void AddOnlineUser(UserPresenceDto user)
    {
        if (OnlineUsers.All(u => u.Username != user.Username))
        {
            OnlineUsers.Add(new UserViewModel(user));
            OnlineUserCount = $"{OnlineUsers.Count}";
        }
        ShowOnlineUsers = OnlineUsers.Count > 0;
        Composer.UpdateAvailableUsers(OnlineUsers);
    }

    public void RemoveOnlineUser(string username)
    {
        UserViewModel? user = OnlineUsers.FirstOrDefault(u => u.Username == username);
        if (user is not null)
        {
            _ = OnlineUsers.Remove(user);
            OnlineUserCount = $"{OnlineUsers.Count}";
        }
        ShowOnlineUsers = OnlineUsers.Count > 0;
        Composer.UpdateAvailableUsers(OnlineUsers);
    }

    public void ClearMessages()
    {
        Messages = [];
        ChannelTitle = "Select a channel";
        ChannelTopic = null;
        HasTopic = false;
        OnlineUsers.Clear();
        OnlineUserCount = string.Empty;
    }

    private void ToggleUsersPanel()
    {
        ShowOnlineUsers = !ShowOnlineUsers;
    }
}
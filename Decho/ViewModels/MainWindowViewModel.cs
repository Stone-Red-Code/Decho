using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;

using Decho.Models;
using ReactiveUI;

namespace Decho.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly UserModel _currentUser;

    public string Title { get; } = "Decho";

    public SidebarViewModel Sidebar { get; }

    public ChatViewModel Chat { get; }

    public MainWindowViewModel()
    {
        _currentUser = new UserModel("user-1", "You");

        var servers = SeedServers();
        Sidebar = new SidebarViewModel(servers);
        Chat = new ChatViewModel();

        Sidebar.WhenAnyValue(x => x.SelectedChannel)
            .Subscribe(channel => Chat.SetChannel(channel));

        Chat.Composer.SendRequested += HandleSendRequested;

        if (Sidebar.SelectedChannel is not null)
            Chat.SetChannel(Sidebar.SelectedChannel);
    }

    private void HandleSendRequested(string text)
    {
        if (Sidebar.SelectedChannel is null)
            return;

        var message = new MessageModel(
            Guid.NewGuid().ToString("N"),
            _currentUser,
            DateTimeOffset.Now,
            text);

        Sidebar.SelectedChannel.AddMessage(message);
    }

    private static IReadOnlyList<ServerModel> SeedServers()
    {
        var alex = new UserModel("user-2", "Alex");
        var sam = new UserModel("user-3", "Sam");

        var general = new ChannelModel(
            "channel-1",
            "general",
            new ObservableCollection<MessageModel>
            {
                new("message-1", alex, DateTimeOffset.Now.AddMinutes(-30), "This is a message."),
                new("message-2", sam, DateTimeOffset.Now.AddMinutes(-25), "Another message for the channel."),
                new("message-3", alex, DateTimeOffset.Now.AddMinutes(-10), "We should test the \nnew layout."),
            });

        var random = new ChannelModel(
            "channel-2",
            "random",
            new ObservableCollection<MessageModel>
            {
                new("message-4", sam, DateTimeOffset.Now.AddMinutes(-5), @"Random chat keeps the vibe light.
rwerwerw
rw
er
wer
w
r
ztr5z56j7u5"),
            });

        var music = new ChannelModel(
            "channel-3",
            "music",
            new ObservableCollection<MessageModel>());

        var server = new ServerModel(
            "echo.voidcube.cloud",
            "echo.voidcube.cloud",
            new ObservableCollection<ChannelModel> { general, random });

        var anotherServer = new ServerModel(
            "echo.stone-red.net",
            "echo.stone-red.net",
            new ObservableCollection<ChannelModel>() { general, music});

        return new[] { server, anotherServer };
    }
}

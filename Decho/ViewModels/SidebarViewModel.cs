using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;

using Decho.Models;
using ReactiveUI;

namespace Decho.ViewModels;

public sealed class SidebarViewModel : ViewModelBase
{
    private ChannelViewModel? _selectedChannel;

    public SidebarViewModel()
    {
        Servers = new ObservableCollection<ServerViewModel>();
    }

    public ObservableCollection<ServerViewModel> Servers { get; }

    public ChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set
        {
            if (ReferenceEquals(_selectedChannel, value))
                return;

            this.RaiseAndSetIfChanged(ref _selectedChannel, value);

            if (value is null)
                return;

            foreach (var server in Servers)
            {
                if (server.Channels.Contains(value))
                {
                    if (!ReferenceEquals(server.SelectedChannel, value))
                        server.SelectedChannel = value;
                }
                else if (server.SelectedChannel is not null)
                {
                    server.SelectedChannel = null;
                }
            }
        }
    }

    public void AddServer(ServerModel model)
    {
        var vm = new ServerViewModel(model);
        vm.WhenAnyValue(s => s.SelectedChannel)
            .Where(channel => channel is not null)
            .Subscribe(channel => SelectedChannel = channel!);

        Servers.Add(vm);
    }

    public void RemoveServer(string serverUrl)
    {
        var server = Servers.FirstOrDefault(s =>
            string.Equals(s.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase));
        if (server is not null)
        {
            if (SelectedChannel is not null && server.Channels.Contains(SelectedChannel))
                SelectedChannel = null;

            Servers.Remove(server);
        }
    }

    public ServerViewModel? GetServer(string serverUrl)
    {
        return Servers.FirstOrDefault(s =>
            string.Equals(s.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase));
    }
}
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

    public SidebarViewModel(IEnumerable<ServerModel> servers)
    {
        Servers = new ObservableCollection<ServerViewModel>(
            servers.Select(server => new ServerViewModel(server)));
        SelectedChannel = Servers.FirstOrDefault()?.Channels.FirstOrDefault();

        foreach (var server in Servers)
        {
            server.WhenAnyValue(s => s.SelectedChannel)
                .Where(channel => channel is not null)
                .Subscribe(new Action<ChannelViewModel?>(channel => SelectedChannel = channel!));
        }
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
}

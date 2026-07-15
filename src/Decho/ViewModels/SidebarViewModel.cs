using System.Collections.ObjectModel;
using System.Reactive.Linq;

namespace Decho.ViewModels;

public sealed class SidebarViewModel : ViewModelBase
{
    public ObservableCollection<ServerViewModel> Servers { get; }

    public ChannelViewModel? SelectedChannel
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
            {
                return;
            }

            _ = this.RaiseAndSetIfChanged(ref field, value);

            if (value is null)
            {
                return;
            }

            foreach (ServerViewModel server in Servers)
            {
                if (server.Channels.Contains(value))
                {
                    if (!ReferenceEquals(server.SelectedChannel, value))
                    {
                        server.SelectedChannel = value;
                    }
                }
                else if (server.SelectedChannel is not null)
                {
                    server.SelectedChannel = null;
                }
            }
        }
    }

    public SidebarViewModel()
    {
        Servers = [];
    }

    public void RemoveServer(string serverUrl)
    {
        ServerViewModel? server = Servers.FirstOrDefault(s =>
            string.Equals(s.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase));
        if (server is not null)
        {
            if (SelectedChannel is not null && server.Channels.Contains(SelectedChannel))
            {
                SelectedChannel = null;
            }

            _ = Servers.Remove(server);
        }
    }

    public ServerViewModel? GetServer(string serverUrl)
    {
        return Servers.FirstOrDefault(s =>
            string.Equals(s.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase));
    }
}
using System.Collections.ObjectModel;
using System.Linq;

using Decho.Models;

namespace Decho.ViewModels;

public sealed class ServerViewModel : ViewModelBase
{
    private bool _isExpanded = true;
    private ChannelViewModel? _selectedChannel;

    public ServerViewModel(ServerModel model)
    {
        Model = model;
        Channels = new ObservableCollection<ChannelViewModel>(
            model.Channels.Select(channel => new ChannelViewModel(channel)));
    }

    public ServerModel Model { get; }

    public string Name => Model.Name;

    public ObservableCollection<ChannelViewModel> Channels { get; }

    public ChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set => this.RaiseAndSetIfChanged(ref _selectedChannel, value);
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
    }
}

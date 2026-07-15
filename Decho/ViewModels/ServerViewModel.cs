using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;

using Decho.Models;
using ReactiveUI;

namespace Decho.ViewModels;

public sealed class ServerViewModel : ViewModelBase
{
    private bool _isExpanded = true;
    private ChannelViewModel? _selectedChannel;
    private bool _isConnected;
    private bool _isConnecting;
    private string? _connectedUser;

    public ServerViewModel(ServerModel model)
    {
        Model = model;
        Channels = new ObservableCollection<ChannelViewModel>(
            model.Channels.Select(channel => new ChannelViewModel(channel)));

        _isConnected = model.IsConnected;
        _isConnecting = model.IsConnecting;
        _connectedUser = model.ConnectedUser;

        ConnectCommand = ReactiveCommand.CreateFromTask(ConnectAsync);
        DisconnectCommand = ReactiveCommand.CreateFromTask(DisconnectAsync);
    }

    public event Func<Task>? ConnectRequested
    {
        add => _connectRequested = (Func<Task>?)Delegate.Combine(_connectRequested, value);
        remove => _connectRequested = (Func<Task>?)Delegate.Remove(_connectRequested, value);
    }
    private Func<Task>? _connectRequested;

    public event Func<Task>? DisconnectRequested
    {
        add => _disconnectRequested = (Func<Task>?)Delegate.Combine(_disconnectRequested, value);
        remove => _disconnectRequested = (Func<Task>?)Delegate.Remove(_disconnectRequested, value);
    }
    private Func<Task>? _disconnectRequested;

    public ServerModel Model { get; }

    public string Name => Model.Name;

    public string ServerUrl => Model.ServerUrl;

    public ObservableCollection<ChannelViewModel> Channels { get; }

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }
    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

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

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            this.RaiseAndSetIfChanged(ref _isConnected, value);
            this.RaisePropertyChanged(nameof(ConnectionStatusText));
            this.RaisePropertyChanged(nameof(ConnectionStatusColor));
            this.RaisePropertyChanged(nameof(ShowConnectionControls));
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            this.RaiseAndSetIfChanged(ref _isConnecting, value);
            this.RaisePropertyChanged(nameof(ConnectionStatusText));
            this.RaisePropertyChanged(nameof(ShowConnectionControls));
        }
    }

    public string? ConnectedUser
    {
        get => _connectedUser;
        set => this.RaiseAndSetIfChanged(ref _connectedUser, value);
    }

    public string ConnectionStatusText => IsConnecting ? "Connecting..." :
        IsConnected ? $"Connected as {ConnectedUser}" : "Disconnected";

    public string ConnectionStatusColor => IsConnected ? "Green" : IsConnecting ? "Orange" : "Gray";

    public bool ShowConnectionControls => !IsConnected && !IsConnecting;

    private async Task ConnectAsync()
    {
        if (_connectRequested is not null)
            await _connectRequested();
    }

    private async Task DisconnectAsync()
    {
        if (_disconnectRequested is not null)
            await _disconnectRequested();
    }

    public void SyncFromModel()
    {
        IsConnected = Model.IsConnected;
        IsConnecting = Model.IsConnecting;
        ConnectedUser = Model.ConnectedUser;
        IsExpanded = true;
    }
}
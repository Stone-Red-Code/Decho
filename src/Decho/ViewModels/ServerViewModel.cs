using Decho.Models;

using EchoHub.Core.Models;

using System.Collections.ObjectModel;
using System.Reactive;

namespace Decho.ViewModels;

public sealed class ServerViewModel : ViewModelBase
{
    public event Func<Task>? ConnectRequested
    {
        add => _connectRequested = (Func<Task>?)Delegate.Combine(_connectRequested, value);
        remove => _connectRequested = (Func<Task>?)Delegate.Remove(_connectRequested, value);
    }

    public event Func<Task>? DisconnectRequested
    {
        add => _disconnectRequested = (Func<Task>?)Delegate.Combine(_disconnectRequested, value);
        remove => _disconnectRequested = (Func<Task>?)Delegate.Remove(_disconnectRequested, value);
    }

    public event Func<Task>? RemoveRequested
    {
        add => _removeRequested = (Func<Task>?)Delegate.Combine(_removeRequested, value);
        remove => _removeRequested = (Func<Task>?)Delegate.Remove(_removeRequested, value);
    }

    public event Func<Task>? CreateChannelRequested
    {
        add => _createChannelRequested = (Func<Task>?)Delegate.Combine(_createChannelRequested, value);
        remove => _createChannelRequested = (Func<Task>?)Delegate.Remove(_createChannelRequested, value);
    }

    public event Func<Task>? ProfileRequested
    {
        add => _profileRequested = (Func<Task>?)Delegate.Combine(_profileRequested, value);
        remove => _profileRequested = (Func<Task>?)Delegate.Remove(_profileRequested, value);
    }

    public event Func<Task>? JoinChannelRequested;

    public event Func<Task>? InvitesRequested;

    public event Func<Task>? ExportDataRequested;

    public event Func<Task>? DeleteAccountRequested;

    public event Func<Task>? UnmuteUserRequested;

    public event Func<Task>? UnbanUserRequested;

    private bool _isConnected;
    private bool _isConnecting;
    private string? _connectedUser;

    private Func<Task>? _connectRequested;

    private Func<Task>? _disconnectRequested;

    private Func<Task>? _removeRequested;

    private Func<Task>? _createChannelRequested;

    private Func<Task>? _profileRequested;

    public ServerModel Model { get; }

    public string Name => Model.Name;

    public string ServerUrl => Model.ServerUrl;

    public ObservableCollection<ChannelViewModel> Channels { get; }

    public ReactiveCommand<Unit, Unit> ConnectCommand { get; }

    public ReactiveCommand<Unit, Unit> DisconnectCommand { get; }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

    public ReactiveCommand<Unit, Unit> CreateChannelCommand { get; }

    public ReactiveCommand<Unit, Unit> ProfileCommand { get; }

    public ReactiveCommand<Unit, Unit> JoinChannelCommand { get; }

    public ReactiveCommand<Unit, Unit> InvitesCommand { get; }

    public ReactiveCommand<Unit, Unit> ExportDataCommand { get; }

    public ReactiveCommand<Unit, Unit> DeleteAccountCommand { get; }

    public ReactiveCommand<Unit, Unit> UnmuteUserCommand { get; }

    public ReactiveCommand<Unit, Unit> UnbanUserCommand { get; }

    /// <summary>The user's role on this server, or null until it has been looked up.</summary>
    public ServerRole? OwnRole
    {
        get;
        set
        {
            _ = this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(CanModerate));
            this.RaisePropertyChanged(nameof(CanAdminister));
        }
    }

    public bool CanModerate => IsConnected && OwnRole >= ServerRole.Mod;

    public bool CanAdminister => IsConnected && OwnRole >= ServerRole.Admin;

    public ChannelViewModel? SelectedChannel
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsExpanded
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = true;

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _ = this.RaiseAndSetIfChanged(ref _isConnected, value);
            this.RaisePropertyChanged(nameof(ConnectionStatusText));
            this.RaisePropertyChanged(nameof(ConnectionStatusColor));
            this.RaisePropertyChanged(nameof(ShowConnectionControls));
            this.RaisePropertyChanged(nameof(CanModerate));
            this.RaisePropertyChanged(nameof(CanAdminister));
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            _ = this.RaiseAndSetIfChanged(ref _isConnecting, value);
            this.RaisePropertyChanged(nameof(ConnectionStatusText));
            this.RaisePropertyChanged(nameof(ConnectionStatusColor));
            this.RaisePropertyChanged(nameof(ShowConnectionControls));
        }
    }

    public string? ConnectedUser
    {
        get => _connectedUser;
        set => this.RaiseAndSetIfChanged(ref _connectedUser, value);
    }

    public string ConnectionStatusText
    {
        get
        {
            if (IsConnected)
            {
                return IsConnecting ? "Connecting..." : $"Connected as {ConnectedUser}";
            }
            else
            {
                return IsConnecting ? "Connecting..." : "Disconnected";
            }
        }
    }

    public string ConnectionStatusColor
    {
        get
        {
            if (IsConnecting)
            {
                return IsConnected ? "Green" : "Orange";
            }
            else
            {
                return IsConnected ? "Green" : "Gray";
            }
        }
    }

    public bool ShowConnectionControls => !IsConnected && !IsConnecting;

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
        RemoveCommand = ReactiveCommand.CreateFromTask(RemoveAsync);
        CreateChannelCommand = ReactiveCommand.CreateFromTask(CreateChannelAsync);
        ProfileCommand = ReactiveCommand.CreateFromTask(OpenProfileAsync);
        JoinChannelCommand = ReactiveCommand.CreateFromTask(() => JoinChannelRequested?.Invoke() ?? Task.CompletedTask);
        InvitesCommand = ReactiveCommand.CreateFromTask(() => InvitesRequested?.Invoke() ?? Task.CompletedTask);
        ExportDataCommand = ReactiveCommand.CreateFromTask(() => ExportDataRequested?.Invoke() ?? Task.CompletedTask);
        DeleteAccountCommand = ReactiveCommand.CreateFromTask(() => DeleteAccountRequested?.Invoke() ?? Task.CompletedTask);
        UnmuteUserCommand = ReactiveCommand.CreateFromTask(() => UnmuteUserRequested?.Invoke() ?? Task.CompletedTask);
        UnbanUserCommand = ReactiveCommand.CreateFromTask(() => UnbanUserRequested?.Invoke() ?? Task.CompletedTask);
    }

    public void SyncFromModel()
    {
        IsConnected = Model.IsConnected;
        IsConnecting = Model.IsConnecting;
        ConnectedUser = Model.ConnectedUser;
        IsExpanded = true;
    }

    private async Task ConnectAsync()
    {
        if (_connectRequested is not null)
        {
            await _connectRequested();
        }
    }

    private async Task DisconnectAsync()
    {
        if (_disconnectRequested is not null)
        {
            await _disconnectRequested();
        }
    }

    private async Task RemoveAsync()
    {
        if (_removeRequested is not null)
        {
            await _removeRequested();
        }
    }

    private async Task CreateChannelAsync()
    {
        if (_createChannelRequested is not null)
        {
            await _createChannelRequested();
        }
    }

    private async Task OpenProfileAsync()
    {
        if (_profileRequested is not null)
        {
            await _profileRequested();
        }
    }
}
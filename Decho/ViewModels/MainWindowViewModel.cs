using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

using Decho.Models;
using Decho.Services;

using EchoHub.Client.Commands;
using EchoHub.Client.Config;
using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using ReactiveUI;

namespace Decho.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ConnectionService _connectionService;
    private readonly CommandHandler _commandHandler;
    private string _statusText = "Ready";
    private bool _isConnected;
    private Window? _mainWindow;

    public string Title { get; } = "Decho";

    public SidebarViewModel Sidebar { get; }

    public ChatViewModel Chat { get; }

    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    public ConnectionService ConnectionService => _connectionService;

    public ReactiveCommand<Unit, Unit> AddServerCommand { get; }

    public MainWindowViewModel()
    {
        _connectionService = new ConnectionService();
        _commandHandler = _connectionService.CreateCommandHandler();

        Sidebar = new SidebarViewModel();
        Chat = new ChatViewModel();

        AddServerCommand = ReactiveCommand.Create(AddServer);

        Chat.Composer.SendRequested += HandleSendRequested;
        Chat.Composer.CommandRequested += HandleCommandAsync;
        Chat.Composer.FileUploadRequested += HandleFileUploadRequested;

        WireCommandHandlerEvents();
        WireConnectionServiceEvents();

        Sidebar.WhenAnyValue(x => x.SelectedChannel)
            .Subscribe(channel => HandleChannelSelected(channel));

        _ = InitializeSavedServersAsync();
    }

    public void SetMainWindow(Window window)
    {
        _mainWindow = window;
    }

    private void AddServer()
    {
        var placeholderServer = new ServerModel(
            Guid.NewGuid().ToString("N"),
            "New Server",
            new ObservableCollection<ChannelModel>(),
            isConnected: false);

        var serverVm = new ServerViewModel(placeholderServer);
        serverVm.ConnectRequested += async () => await HandleServerConnectRequested(serverVm);
        serverVm.DisconnectRequested += async () => await HandleServerDisconnectRequested(serverVm);

        Sidebar.Servers.Add(serverVm);
        StatusText = "Click Connect on the server to get started";
    }

    private void WireCommandHandlerEvents()
    {
        _commandHandler.OnSetStatus += async (status, message) =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.UpdateStatusAsync(serverUrl, status, message);
        };

        _commandHandler.OnSetTheme += themeName =>
        {
            return Task.CompletedTask;
        };

        _commandHandler.OnJoinChannel += async channelName =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            var channel = await _connectionService.JoinChannelAsync(serverUrl, channelName);
            EnsureChannelInList(serverUrl, channelName);
            var channelModel = FindChannel(serverUrl, channelName);
            if (channelModel is not null)
            {
                var channelVm = Sidebar.GetServer(serverUrl)?.Channels
                    .FirstOrDefault(c => c.Name == channelName);
                if (channelVm is not null)
                {
                    foreach (var msg in channel)
                        channelVm.AddMessage(msg);
                }
            }
        };

        _commandHandler.OnLeaveChannel += async () =>
        {
            var serverUrl = GetCurrentServerUrl();
            var channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel)) return;

            if (channel == HubConstants.DefaultChannel) return;
            await _connectionService.LeaveChannelAsync(serverUrl, channel);
        };

        _commandHandler.OnListUsers += async () =>
        {
            var serverUrl = GetCurrentServerUrl();
            var channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel)) return;

            var users = await _connectionService.GetOnlineUsersAsync(serverUrl, channel);
            var userList = string.Join(", ", users.Select(u => u.DisplayName ?? u.Username));
            StatusText = $"Online in #{channel}: {userList}";
        };

        _commandHandler.OnSetTopic += async topic =>
        {
            var serverUrl = GetCurrentServerUrl();
            var channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel)) return;

            await _connectionService.UpdateProfileAsync(serverUrl, null, null, null);
            _connectionService.UpdateChannelTopic(serverUrl, channel, topic);
            Chat.ChannelTopic = topic;
        };

        _commandHandler.OnKickUser += async (username, reason) =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.KickUserAsync(serverUrl, username, reason);
        };

        _commandHandler.OnBanUser += async (username, reason) =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.BanUserAsync(serverUrl, username, reason);
        };

        _commandHandler.OnUnbanUser += async username =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.UnbanUserAsync(serverUrl, username);
        };

        _commandHandler.OnMuteUser += async (username, duration) =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.MuteUserAsync(serverUrl, username, duration);
        };

        _commandHandler.OnUnmuteUser += async username =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.UnmuteUserAsync(serverUrl, username);
        };

        _commandHandler.OnAssignRole += async (username, roleStr) =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            var role = roleStr.ToLowerInvariant() switch
            {
                "admin" => ServerRole.Admin,
                "mod" => ServerRole.Mod,
                _ => ServerRole.Member,
            };
            await _connectionService.AssignRoleAsync(serverUrl, username, role);
        };

        _commandHandler.OnNukeChannel += async () =>
        {
            var serverUrl = GetCurrentServerUrl();
            var channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel)) return;
            await _connectionService.NukeChannelAsync(serverUrl, channel);
        };

        _commandHandler.OnTestSound += () => Task.CompletedTask;

        _commandHandler.OnQuit += () =>
        {
            if (_mainWindow is not null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _mainWindow.Close());
            return Task.CompletedTask;
        };

        _commandHandler.OnHelp += () => Task.CompletedTask;

        _commandHandler.OnSendFile += async (target, size) =>
        {
            var serverUrl = GetCurrentServerUrl();
            var channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel)) return;

            try
            {
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri)
                    && (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    await _connectionService.SendUrlAsync(serverUrl, channel, target, size);
                }
                else
                {
                    await _connectionService.UploadFileAsync(serverUrl, channel, target, size);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Send failed: {ex.Message}";
            }
        };

        _commandHandler.OnSetNick += async displayName =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.UpdateProfileAsync(serverUrl, displayName, null, null);
        };

        _commandHandler.OnSetColor += async color =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.UpdateProfileAsync(serverUrl, null, null, color);
        };

        _commandHandler.OnSetAvatar += async target =>
        {
            var serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;
            await _connectionService.SetAvatarAsync(serverUrl, target);
        };

        _commandHandler.OnOpenProfile += async username =>
        {
            StatusText = $"Profile: {username ?? "self"}";
        };

        _commandHandler.OnOpenServers += () =>
        {
            var config = _connectionService.LoadConfig();
            var servers = string.Join("\n", config.SavedServers.Select(s =>
                $"{s.Name} ({s.Url}) - {s.Username ?? "?"}"));
            StatusText = servers;
            return Task.CompletedTask;
        };
    }

    private void WireConnectionServiceEvents()
    {
        _connectionService.ServerAdded += server =>
        {
            var serverVm = new ServerViewModel(server);
            serverVm.ConnectRequested += () => HandleServerConnectRequested(serverVm);
            serverVm.DisconnectRequested += () => HandleServerDisconnectRequested(serverVm);
            serverVm.WhenAnyValue(s => s.SelectedChannel)
                .Where(channel => channel is not null)
                .Subscribe(channel => Sidebar.SelectedChannel = channel!);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var existing = Sidebar.GetServer(server.ServerUrl);
                if (existing is not null)
                    Sidebar.Servers.Remove(existing);
                Sidebar.Servers.Add(serverVm);
                StatusText = $"Connected to {server.Name}";
                IsConnected = true;
            });
        };

        _connectionService.ServerRemoved += serverUrl =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Sidebar.RemoveServer(serverUrl);
                if (Sidebar.Servers.Count == 0)
                {
                    IsConnected = false;
                    Chat.ClearMessages();
                    StatusText = "Ready";
                }
            });
        };

        _connectionService.ServerStateChanged += server =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var serverVm = Sidebar.GetServer(server.ServerUrl);
                if (serverVm is not null)
                {
                    serverVm.SyncFromModel();
                    if (!server.IsConnected)
                        StatusText = $"Disconnected from {server.Name}";
                }
            });
        };

        _connectionService.MessageReceived += (serverUrl, message) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var channelVm = FindChannelViewModel(serverUrl, message.ChannelName);
                if (channelVm is not null)
                {
                    channelVm.AddMessage(message);
                }
            });
        };

        _connectionService.ChannelAdded += (serverUrl, channel) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                EnsureChannelInList(serverUrl, channel.Name);
            });
        };

        _connectionService.ErrorOccurred += (serverUrl, error) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Error: {error}";
            });
        };
    }

    private async Task<string?> HandleCommandAsync(string commandText)
    {
        var result = await _commandHandler.HandleAsync(commandText);
        if (result.Message is not null && !result.IsError)
        {
            Chat.AddMessage(new MessageViewModel(new MessageModel(
                Guid.NewGuid().ToString("N"),
                new UserModel("system", "System"),
                DateTimeOffset.Now,
                result.Message,
                Chat.CurrentChannelName,
                Chat.CurrentServerUrl)));
        }
        return result.Message;
    }

    private void HandleSendRequested(string serverUrl, string text)
    {
        if (string.IsNullOrEmpty(Chat.CurrentChannelName))
            return;

            if (_commandHandler.IsCommand(text))
            {
                _ = HandleCommandAsync(text);
                return;
            }

        Task.Run(async () =>
        {
            try
            {
                await _connectionService.SendMessageAsync(serverUrl, Chat.CurrentChannelName, text);
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = $"Send failed: {ex.Message}";
                });
            }
        });
    }

    private void HandleFileUploadRequested(string serverUrl, string filePath)
    {
        if (string.IsNullOrEmpty(Chat.CurrentChannelName)) return;

        Task.Run(async () =>
        {
            try
            {
                await _connectionService.UploadFileAsync(serverUrl, Chat.CurrentChannelName, filePath, null);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    StatusText = "File uploaded");
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    StatusText = $"Upload failed: {ex.Message}");
            }
        });
    }

    private void HandleChannelSelected(ChannelViewModel? channel)
    {
        if (channel is null)
        {
            Chat.SetChannel(null);
            return;
        }

        var serverUrl = FindServerUrlForChannel(channel);
        Chat.SetChannel(channel, serverUrl);

        if (!string.IsNullOrEmpty(serverUrl))
        {
            var commandHandler = _connectionService.CreateCommandHandler();
            Chat.SetComposerCommandHandler(commandHandler);

            Task.Run(async () =>
            {
                try
                {
                    var history = await _connectionService.JoinChannelAsync(serverUrl, channel.Name);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        channel.ClearMessages();
                        foreach (var msg in history)
                            channel.AddMessage(msg);
                    });
                }
                catch
                {
                    // Channel might not be available
                }
            });
        }
    }

    private async Task InitializeSavedServersAsync()
    {
        var config = _connectionService.LoadConfig();
        var connectTasks = new List<Task>();

        foreach (var saved in config.SavedServers)
        {
            var placeholderServer = new ServerModel(
                Guid.NewGuid().ToString("N"),
                saved.Name,
                new ObservableCollection<ChannelModel>(),
                saved.Url,
                isConnected: false);

            var serverVm = new ServerViewModel(placeholderServer);
            serverVm.ConnectRequested += async () => await HandleServerConnectRequested(serverVm);
            serverVm.DisconnectRequested += async () => await HandleServerDisconnectRequested(serverVm);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Sidebar.Servers.Add(serverVm);
            });

            if (!string.IsNullOrEmpty(saved.RefreshToken) && saved.RememberMe)
            {
                connectTasks.Add(AutoConnectSavedServer(serverVm, saved));
            }
        }

        await Task.WhenAll(connectTasks);
    }

    private async Task AutoConnectSavedServer(ServerViewModel serverVm, SavedServer saved)
    {
        if (string.IsNullOrEmpty(saved.Username) || string.IsNullOrEmpty(saved.RefreshToken))
            return;

        try
        {
            serverVm.IsConnecting = true;
            await _connectionService.ConnectWithSavedTokenAsync(
                saved.Url, saved.Username, saved.RefreshToken, saved.RememberMe);
        }
        catch
        {
            serverVm.IsConnecting = false;
        }
    }

    private static async Task ShowMessageBox(Window owner, string title, string message)
    {
        var msgBox = new Window
        {
            Title = title,
            Width = 400,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var stack = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(15) };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var okBtn = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        stack.Children.Add(okBtn);
        msgBox.Content = stack;

        var tcs = new TaskCompletionSource();
        okBtn.Click += (_, _) => { msgBox.Close(); tcs.TrySetResult(); };
        msgBox.Closed += (_, _) => tcs.TrySetResult();

        await msgBox.ShowDialog(owner);
        await tcs.Task;
    }

    private async Task HandleServerConnectRequested(ServerViewModel serverVm)
    {
        // Show connect dialog
        if (_mainWindow is null) return;

        var config = _connectionService.LoadConfig();
        var prefill = config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverVm.ServerUrl, StringComparison.OrdinalIgnoreCase));
        var dialog = new ConnectDialog(config.SavedServers, prefill);

        var result = await dialog.ShowAsync(_mainWindow);
        if (result is null) return;

        try
        {
            serverVm.IsConnecting = true;
            StatusText = "Connecting...";

            if (result.IsSavedSession && result.SavedRefreshToken is not null)
            {
                await _connectionService.ConnectWithSavedTokenAsync(
                    result.ServerUrl, result.Username, result.SavedRefreshToken, result.RememberMe);
            }
            else
            {
                await _connectionService.ConnectAsync(
                    result.ServerUrl, result.Username, result.Password, result.IsRegister, result.RememberMe);
            }

            // Remove the placeholder and let the real server added event handle it
            Sidebar.RemoveServer(serverVm.ServerUrl);

            // Save to config with refresh token
            var refreshToken = _connectionService.GetRefreshToken(result.ServerUrl);
            var savedServer = new SavedServer
            {
                Name = new Uri(result.ServerUrl).Host,
                Url = result.ServerUrl,
                Username = result.Username,
                RefreshToken = result.RememberMe ? refreshToken : null,
                RememberMe = result.RememberMe,
                LastConnected = DateTimeOffset.Now,
            };
            _connectionService.SaveServerToConfig(savedServer);
        }
        catch (Exception ex)
        {
            StatusText = $"Connection failed: {ex.Message}";
            serverVm.IsConnecting = false;
        }
    }

    private async Task HandleServerDisconnectRequested(ServerViewModel serverVm)
    {
        try
        {
            await _connectionService.DisconnectAsync(serverVm.ServerUrl);
        }
        catch (Exception ex)
        {
            StatusText = $"Disconnect error: {ex.Message}";
        }
    }

    private void EnsureChannelInList(string serverUrl, string channelName)
    {
        var serverVm = Sidebar.GetServer(serverUrl);
        if (serverVm is null) return;

        if (!serverVm.Channels.Any(c => c.Name == channelName))
        {
            var channelModel = new ChannelModel(
                Guid.NewGuid().ToString("N"),
                channelName,
                new System.Collections.ObjectModel.ObservableCollection<MessageModel>());
            var channelVm = new ChannelViewModel(channelModel);
            serverVm.Channels.Add(channelVm);
        }
    }

    private ChannelModel? FindChannel(string serverUrl, string channelName)
    {
        var serverVm = Sidebar.GetServer(serverUrl);
        return serverVm?.Channels.FirstOrDefault(c => c.Name == channelName)?.Model;
    }

    private ChannelViewModel? FindChannelViewModel(string serverUrl, string channelName)
    {
        var serverVm = Sidebar.GetServer(serverUrl);
        return serverVm?.Channels.FirstOrDefault(c => c.Name == channelName);
    }

    private string FindServerUrlForChannel(ChannelViewModel channel)
    {
        foreach (var server in Sidebar.Servers)
        {
            if (server.Channels.Contains(channel))
                return server.ServerUrl;
        }
        return string.Empty;
    }

    private string GetCurrentServerUrl()
    {
        return Chat.CurrentServerUrl;
    }

    public void Dispose()
    {
        _connectionService.Dispose();
    }
}

public sealed class ConnectDialogResult
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IsRegister { get; set; }
    public bool RememberMe { get; set; }
    public bool IsSavedSession { get; set; }
    public string? SavedRefreshToken { get; set; }
}

public sealed class ConnectDialog
{
    private readonly List<SavedServer> _savedServers;
    private readonly SavedServer? _prefill;

    public ConnectDialog(List<SavedServer> savedServers, SavedServer? prefill = null)
    {
        _savedServers = savedServers;
        _prefill = prefill;
    }

    public async Task<ConnectDialogResult?> ShowAsync(Window owner)
    {
        var dialog = new Window
        {
            Title = "Connect to Server",
            Width = 450,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var stack = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(15) };

        var urlLabel = new TextBlock { Text = "Server URL:" };
        var urlBox = new TextBox { Watermark = "http://localhost:5000", Text = "http://localhost:5000" };
        stack.Children.Add(urlLabel);
        stack.Children.Add(urlBox);

        var userLabel = new TextBlock { Text = "Username:" };
        var userBox = new TextBox { Watermark = "username" };
        stack.Children.Add(userLabel);
        stack.Children.Add(userBox);

        var passLabel = new TextBlock { Text = "Password:" };
        var passBox = new TextBox { Watermark = "password", PasswordChar = '*' };
        stack.Children.Add(passLabel);
        stack.Children.Add(passBox);

        var rememberMe = new CheckBox { Content = "Remember me", IsChecked = true };
        stack.Children.Add(rememberMe);

        var buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 10 };

        var loginBtn = new Button { Content = "Login", Width = 100 };
        var registerBtn = new Button { Content = "Register", Width = 100 };
        var cancelBtn = new Button { Content = "Cancel", Width = 100 };
        buttonPanel.Children.Add(loginBtn);
        buttonPanel.Children.Add(registerBtn);
        buttonPanel.Children.Add(cancelBtn);
        stack.Children.Add(buttonPanel);

        ListBox? serversList = null;
        if (_savedServers.Count > 0)
        {
            var sep = new TextBlock { Text = "--- Saved Servers ---", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            stack.Children.Add(sep);

            serversList = new ListBox { Height = 100 };
            serversList.ItemsSource = _savedServers.Select(s => $"{s.Name} ({s.Url}) - {s.Username ?? "?"}").ToList();
            stack.Children.Add(serversList);

            serversList.SelectionChanged += (_, _) =>
            {
                var idx = serversList.SelectedIndex;
                if (idx >= 0 && idx < _savedServers.Count)
                {
                    var s = _savedServers[idx];
                    urlBox.Text = s.Url;
                    userBox.Text = s.Username ?? "";
                    rememberMe.IsChecked = s.RememberMe;
                    if (!string.IsNullOrEmpty(s.RefreshToken))
                        passBox.Text = "";
                }
            };
        }

        if (_prefill is not null)
        {
            urlBox.Text = _prefill.Url;
            userBox.Text = _prefill.Username ?? "";
            var idx = _savedServers.FindIndex(s =>
                string.Equals(s.Url, _prefill.Url, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx < _savedServers.Count && serversList is not null)
                serversList.SelectedIndex = idx;
        }

        dialog.Content = new ScrollViewer { Content = stack };

        var tcs = new TaskCompletionSource<ConnectDialogResult?>();
        ConnectDialogResult? result = null;

        loginBtn.Click += async (_, _) =>
        {
            var url = urlBox.Text?.Trim() ?? "";
            var user = userBox.Text?.Trim() ?? "";
            var pass = passBox.Text ?? "";

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user))
            {
                await ShowMessageBox(owner, "Validation", "Server URL and username are required.");
                return;
            }

            // Check for saved session
            var saved = _savedServers.FirstOrDefault(s =>
                string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.Username, user, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(s.RefreshToken));

            if (string.IsNullOrEmpty(pass) && saved is not null)
            {
                result = new ConnectDialogResult
                {
                    ServerUrl = url,
                    Username = user,
                    IsSavedSession = true,
                    SavedRefreshToken = saved.RefreshToken,
                    RememberMe = saved.RememberMe,
                };
            }
            else
            {
                if (string.IsNullOrEmpty(pass))
                {
                    await ShowMessageBox(owner, "Validation", "Password is required.");
                    return;
                }

                result = new ConnectDialogResult
                {
                    ServerUrl = url,
                    Username = user,
                    Password = pass,
                    IsRegister = false,
                    RememberMe = rememberMe.IsChecked ?? false,
                };
            }

            dialog.Close();
            tcs.TrySetResult(result);
        };

        registerBtn.Click += (_, _) =>
        {
            var url = urlBox.Text?.Trim() ?? "";
            var user = userBox.Text?.Trim() ?? "";
            var pass = passBox.Text ?? "";

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            {
                _ = ShowMessageBox(owner, "Validation", "Server URL, username, and password are required.");
                return;
            }

            result = new ConnectDialogResult
            {
                ServerUrl = url,
                Username = user,
                Password = pass,
                IsRegister = true,
                RememberMe = rememberMe.IsChecked ?? false,
            };

            dialog.Close();
            tcs.TrySetResult(result);
        };

        cancelBtn.Click += (_, _) =>
        {
            dialog.Close();
            tcs.TrySetResult(null);
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(result);

        if (owner is not null)
            await dialog.ShowDialog(owner);
        else
            dialog.Show();

        return await tcs.Task;
    }

    private static async Task ShowMessageBox(Window owner, string title, string message)
    {
        var msgBox = new Window
        {
            Title = title,
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        var stack = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(15) };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var okBtn = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        stack.Children.Add(okBtn);
        msgBox.Content = stack;

        var tcs = new TaskCompletionSource();
        okBtn.Click += (_, _) => { msgBox.Close(); tcs.TrySetResult(); };
        msgBox.Closed += (_, _) => tcs.TrySetResult();

        await msgBox.ShowDialog(owner);
        await tcs.Task;
    }
}
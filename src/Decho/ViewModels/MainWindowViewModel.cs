using Avalonia.Controls;

using Decho.Models;
using Decho.Services;
using Decho.Views;

using EchoHub.Client.Commands;
using EchoHub.Client.Config;
using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using System.Reactive;
using System.Reactive.Linq;

namespace Decho.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly CommandHandler _commandHandler;
    private Window? _mainWindow;

    public string Title { get; } = "Decho";

    public SidebarViewModel Sidebar { get; }

    public ChatViewModel Chat { get; }

    public string StatusText
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    } = "Ready";

    public ConnectionService ConnectionService { get; }

    public ReactiveCommand<Unit, Unit> AddServerCommand { get; }

    public MainWindowViewModel()
    {
        ConnectionService = new ConnectionService();
        _commandHandler = new CommandHandler();

        Sidebar = new SidebarViewModel();
        Chat = new ChatViewModel();

        AddServerCommand = ReactiveCommand.Create(AddServer);

        Chat.Composer.SendRequested += HandleSendRequested;
        Chat.Composer.CommandRequested += HandleCommandAsync;
        Chat.Composer.FileUploadRequested += HandleFileUploadRequested;

        WireCommandHandlerEvents();
        WireConnectionServiceEvents();

        _ = Sidebar.WhenAnyValue(x => x.SelectedChannel)
            .Subscribe(HandleChannelSelected);

        _ = InitializeSavedServersAsync();
    }

    public void SetMainWindow(Window window)
    {
        _mainWindow = window;
    }

    public void Dispose()
    {
        ConnectionService.Dispose();
    }

    private void AddServer()
    {
        ServerModel placeholderServer = new ServerModel(
            Guid.NewGuid().ToString("N"),
            "New Server",
            [],
            isConnected: false);

        ServerViewModel serverVm = new ServerViewModel(placeholderServer);
        serverVm.ConnectRequested += async () => await HandleServerConnectRequested(serverVm);
        serverVm.DisconnectRequested += async () => await HandleServerDisconnectRequested(serverVm);
        serverVm.RemoveRequested += async () => await HandleServerRemoveRequested(serverVm);

        Sidebar.Servers.Add(serverVm);
        StatusText = "Click Connect on the server to get started";
    }

    private void WireCommandHandlerEvents()
    {
        _commandHandler.OnSetStatus += async (status, message) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.UpdateStatusAsync(serverUrl, status, message);
        };

        _commandHandler.OnSetTheme += themeName =>
        {
            return Task.CompletedTask;
        };

        _commandHandler.OnJoinChannel += async channelName =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            List<MessageModel> channel = await ConnectionService.JoinChannelAsync(serverUrl, channelName);
            EnsureChannelInList(serverUrl, channelName);
            ChannelModel? channelModel = FindChannel(serverUrl, channelName);
            if (channelModel is not null)
            {
                ChannelViewModel? channelVm = Sidebar.GetServer(serverUrl)?.Channels
                    .FirstOrDefault(c => c.Name == channelName);
                if (channelVm is not null)
                {
                    foreach (MessageModel msg in channel)
                    {
                        channelVm.AddMessage(msg);
                    }
                }
            }
        };

        _commandHandler.OnLeaveChannel += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            if (channel == HubConstants.DefaultChannel)
            {
                return;
            }

            await ConnectionService.LeaveChannelAsync(serverUrl, channel);
        };

        _commandHandler.OnListUsers += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            List<UserPresenceDto> users = await ConnectionService.GetOnlineUsersAsync(serverUrl, channel);
            string userList = string.Join(", ", users.Select(u => u.DisplayName ?? u.Username));
            StatusText = $"Online in #{channel}: {userList}";
        };

        _commandHandler.OnSetTopic += async topic =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            await ConnectionService.UpdateProfileAsync(serverUrl, null, null, null);
            ConnectionService.UpdateChannelTopic(serverUrl, channel, topic);
            Chat.ChannelTopic = topic;
        };

        _commandHandler.OnKickUser += async (username, reason) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.KickUserAsync(serverUrl, username, reason);
        };

        _commandHandler.OnBanUser += async (username, reason) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.BanUserAsync(serverUrl, username, reason);
        };

        _commandHandler.OnUnbanUser += async username =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.UnbanUserAsync(serverUrl, username);
        };

        _commandHandler.OnMuteUser += async (username, duration) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.MuteUserAsync(serverUrl, username, duration);
        };

        _commandHandler.OnUnmuteUser += async username =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.UnmuteUserAsync(serverUrl, username);
        };

        _commandHandler.OnAssignRole += async (username, roleStr) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            ServerRole role = roleStr.ToLowerInvariant() switch
            {
                "admin" => ServerRole.Admin,
                "mod" => ServerRole.Mod,
                _ => ServerRole.Member,
            };
            await ConnectionService.AssignRoleAsync(serverUrl, username, role);
        };

        _commandHandler.OnNukeChannel += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            await ConnectionService.NukeChannelAsync(serverUrl, channel);
        };

        _commandHandler.OnTestSound += () => Task.CompletedTask;

        _commandHandler.OnQuit += () =>
        {
            if (_mainWindow is not null)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _mainWindow.Close());
            }

            return Task.CompletedTask;
        };

        _commandHandler.OnHelp += () => Task.CompletedTask;

        _commandHandler.OnSendFile += async (target, size) =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            try
            {
                if (Uri.TryCreate(target, UriKind.Absolute, out Uri? uri)
                    && (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    await ConnectionService.SendUrlAsync(serverUrl, channel, target, size);
                }
                else
                {
                    await ConnectionService.UploadFileAsync(serverUrl, channel, target, size);
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Send failed: {ex.Message}";
            }
        };

        _commandHandler.OnSetNick += async displayName =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.UpdateProfileAsync(serverUrl, displayName, null, null);
        };

        _commandHandler.OnSetColor += async color =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.UpdateProfileAsync(serverUrl, null, null, color);
        };

        _commandHandler.OnSetAvatar += async target =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await ConnectionService.SetAvatarAsync(serverUrl, target);
        };

        _commandHandler.OnOpenProfile += async username =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            string target = username ?? ConnectionService.GetCurrentUsername(serverUrl) ?? string.Empty;
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            try
            {
                UserProfileDto? profile = await ConnectionService.GetUserProfileAsync(serverUrl, target);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (profile is null)
                    {
                        StatusText = "User not found";
                        return;
                    }

                    ProfileWindow dialog = new ProfileWindow(profile);
                    if (_mainWindow is not null)
                    {
                        _ = dialog.ShowDialog(_mainWindow);
                    }
                });
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    StatusText = $"Failed to load profile: {ex.Message}";
                });
            }
        };

        _commandHandler.OnOpenServers += () =>
        {
            ClientConfig config = ConfigManager.Load();
            string servers = string.Join("\n", config.SavedServers.Select(s =>
                $"{s.Name} ({s.Url}) - {s.Username ?? "?"}"));
            StatusText = servers;
            return Task.CompletedTask;
        };
    }

    private void WireConnectionServiceEvents()
    {
        ConnectionService.ServerAdded += server =>
        {
            ServerViewModel serverVm = new ServerViewModel(server);
            serverVm.ConnectRequested += () => HandleServerConnectRequested(serverVm);
            serverVm.DisconnectRequested += () => HandleServerDisconnectRequested(serverVm);
            serverVm.RemoveRequested += () => HandleServerRemoveRequested(serverVm);
            _ = serverVm.WhenAnyValue(s => s.SelectedChannel)
                .Where(channel => channel is not null)
                .Subscribe(channel => Sidebar.SelectedChannel = channel!);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ServerViewModel? existing = Sidebar.GetServer(server.ServerUrl);
                if (existing is not null)
                {
                    _ = Sidebar.Servers.Remove(existing);
                }

                Sidebar.Servers.Add(serverVm);
                if (serverVm.Channels.Count > 0)
                {
                    serverVm.SelectedChannel = serverVm.Channels[0];
                }

                StatusText = $"Connected to {server.Name}";
            });
        };

        ConnectionService.ServerRemoved += serverUrl =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Sidebar.RemoveServer(serverUrl);
                if (Sidebar.Servers.Count == 0)
                {
                    Chat.ClearMessages();
                    StatusText = "Ready";
                }
            });
        };

        ConnectionService.ServerStateChanged += server =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ServerViewModel? serverVm = Sidebar.GetServer(server.ServerUrl);
                if (serverVm is not null)
                {
                    serverVm.SyncFromModel();
                    if (string.Equals(server.ServerUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        Chat.Composer.IsConnected = server.IsConnected;
                    }

                    if (!server.IsConnected)
                    {
                        StatusText = $"Disconnected from {server.Name}";
                    }
                }
            });
        };

        ConnectionService.MessageReceived += (serverUrl, message) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ChannelViewModel? channelVm = FindChannelViewModel(serverUrl, message.ChannelName);
                channelVm?.AddMessage(message);
            });
        };

        ConnectionService.ErrorOccurred += (serverUrl, error) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Error: {error}";
            });
        };
    }

    private async Task<string?> HandleCommandAsync(string commandText)
    {
        CommandResult result = await _commandHandler.HandleAsync(commandText);
        if (result.Message is not null && !result.IsError)
        {
            Chat.Messages.Add(new MessageViewModel(new MessageModel(
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
        {
            return;
        }

        if (_commandHandler.IsCommand(text))
        {
            _ = HandleCommandAsync(text);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectionService.SendMessageAsync(serverUrl, Chat.CurrentChannelName, text);
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
        if (string.IsNullOrEmpty(Chat.CurrentChannelName))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectionService.UploadFileAsync(serverUrl, Chat.CurrentChannelName, filePath, null);
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

        string serverUrl = FindServerUrlForChannel(channel);
        bool isServerConnected = Sidebar.GetServer(serverUrl)?.IsConnected ?? false;
        Chat.SetChannel(channel, serverUrl, isServerConnected);

        if (!string.IsNullOrEmpty(serverUrl))
        {
            Chat.Composer.SetCommandHandler(new CommandHandler());

            _ = Task.Run(async () =>
            {
                try
                {
                    List<MessageModel> history = await ConnectionService.JoinChannelAsync(serverUrl, channel.Name);
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        channel.ClearMessages();
                        foreach (MessageModel msg in history)
                        {
                            channel.AddMessage(msg);
                        }
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
        ClientConfig config = ConfigManager.Load();
        List<Task> connectTasks = [];

        foreach (SavedServer saved in config.SavedServers)
        {
            ServerModel placeholderServer = new ServerModel(
                Guid.NewGuid().ToString("N"),
                saved.Name,
                [],
                saved.Url,
                isConnected: false);

            ServerViewModel serverVm = new ServerViewModel(placeholderServer);
            serverVm.ConnectRequested += async () => await HandleServerConnectRequested(serverVm);
            serverVm.DisconnectRequested += async () => await HandleServerDisconnectRequested(serverVm);
            serverVm.RemoveRequested += async () => await HandleServerRemoveRequested(serverVm);

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
        {
            return;
        }

        try
        {
            serverVm.IsConnecting = true;
            await ConnectionService.ConnectWithSavedTokenAsync(
                saved.Url, saved.Username, saved.RefreshToken, saved.RememberMe);
        }
        catch
        {
            serverVm.IsConnecting = false;
        }
    }

    private async Task HandleServerConnectRequested(ServerViewModel serverVm)
    {
        // Show connect dialog
        if (_mainWindow is null)
        {
            return;
        }

        ClientConfig config = ConfigManager.Load();
        SavedServer? prefill = config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverVm.ServerUrl, StringComparison.OrdinalIgnoreCase));
        ConnectDialog dialog = new ConnectDialog(config.SavedServers, prefill);

        ConnectDialogResult? result = await dialog.ShowAsync(_mainWindow);
        if (result is null)
        {
            return;
        }

        try
        {
            serverVm.IsConnecting = true;
            StatusText = "Connecting...";

            if (result.IsSavedSession && result.SavedRefreshToken is not null)
            {
                await ConnectionService.ConnectWithSavedTokenAsync(
                    result.ServerUrl, result.Username, result.SavedRefreshToken, result.RememberMe);
            }
            else
            {
                _ = await ConnectionService.ConnectAsync(
                    result.ServerUrl, result.Username, result.Password, result.IsRegister, result.RememberMe);
            }

            // Remove the placeholder and let the real server added event handle it
            Sidebar.RemoveServer(serverVm.ServerUrl);

            // Save to config with refresh token
            string? refreshToken = ConnectionService.GetRefreshToken(result.ServerUrl);
            SavedServer savedServer = new SavedServer
            {
                Name = new Uri(result.ServerUrl).Host,
                Url = result.ServerUrl,
                Username = result.Username,
                RefreshToken = result.RememberMe ? refreshToken : null,
                RememberMe = result.RememberMe,
                LastConnected = DateTimeOffset.Now,
            };
            ConfigManager.SaveServer(savedServer);
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
            await ConnectionService.DisconnectAsync(serverVm.ServerUrl);
        }
        catch (Exception ex)
        {
            StatusText = $"Disconnect error: {ex.Message}";
        }
    }

    private async Task HandleServerRemoveRequested(ServerViewModel serverVm)
    {
        try
        {
            await ConnectionService.RemoveServerAsync(serverVm.ServerUrl);
        }
        catch (Exception ex)
        {
            StatusText = $"Remove error: {ex.Message}";
        }
    }

    private void EnsureChannelInList(string serverUrl, string channelName)
    {
        ServerViewModel? serverVm = Sidebar.GetServer(serverUrl);
        if (serverVm is null)
        {
            return;
        }

        if (!serverVm.Channels.Any(c => c.Name == channelName))
        {
            ChannelModel channelModel = new ChannelModel(
                Guid.NewGuid().ToString("N"),
                channelName,
                []);
            ChannelViewModel channelVm = new ChannelViewModel(channelModel);
            serverVm.Channels.Add(channelVm);
        }
    }

    private ChannelModel? FindChannel(string serverUrl, string channelName)
    {
        ServerViewModel? serverVm = Sidebar.GetServer(serverUrl);
        return serverVm?.Channels.FirstOrDefault(c => c.Name == channelName)?.Model;
    }

    private ChannelViewModel? FindChannelViewModel(string serverUrl, string channelName)
    {
        ServerViewModel? serverVm = Sidebar.GetServer(serverUrl);
        return serverVm?.Channels.FirstOrDefault(c => c.Name == channelName);
    }

    private string FindServerUrlForChannel(ChannelViewModel channel)
    {
        foreach (ServerViewModel server in Sidebar.Servers)
        {
            if (server.Channels.Contains(channel))
            {
                return server.ServerUrl;
            }
        }
        return string.Empty;
    }

    private string GetCurrentServerUrl()
    {
        return Chat.CurrentServerUrl;
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

public sealed class ConnectDialog(List<SavedServer> savedServers, SavedServer? prefill = null)
{
    private readonly List<SavedServer> _savedServers = savedServers;
    private readonly SavedServer? _prefill = prefill;

    public async Task<ConnectDialogResult?> ShowAsync(Window owner)
    {
        Window dialog = new Window
        {
            Title = "Connect to Server",
            Width = 450,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        StackPanel stack = new StackPanel { Spacing = 8, Margin = new Avalonia.Thickness(15) };

        TextBlock urlLabel = new TextBlock { Text = "Server URL:" };
        TextBox urlBox = new TextBox { Watermark = "http://localhost:5000", Text = "http://localhost:5000" };
        stack.Children.Add(urlLabel);
        stack.Children.Add(urlBox);

        TextBlock userLabel = new TextBlock { Text = "Username:" };
        TextBox userBox = new TextBox { Watermark = "username" };
        stack.Children.Add(userLabel);
        stack.Children.Add(userBox);

        TextBlock passLabel = new TextBlock { Text = "Password:" };
        TextBox passBox = new TextBox { Watermark = "password", PasswordChar = '*' };
        stack.Children.Add(passLabel);
        stack.Children.Add(passBox);

        CheckBox rememberMe = new CheckBox { Content = "Remember me", IsChecked = true };
        stack.Children.Add(rememberMe);

        StackPanel buttonPanel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Spacing = 10 };

        Button loginBtn = new Button { Content = "Login", Width = 100 };
        Button registerBtn = new Button { Content = "Register", Width = 100 };
        Button cancelBtn = new Button { Content = "Cancel", Width = 100 };
        buttonPanel.Children.Add(loginBtn);
        buttonPanel.Children.Add(registerBtn);
        buttonPanel.Children.Add(cancelBtn);
        stack.Children.Add(buttonPanel);

        ListBox? serversList = null;
        if (_savedServers.Count > 0)
        {
            TextBlock sep = new TextBlock { Text = "--- Saved Servers ---", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            stack.Children.Add(sep);

            serversList = new ListBox
            {
                Height = 100,
                ItemsSource = _savedServers.Select(s => $"{s.Name} ({s.Url}) - {s.Username ?? "?"}").ToList()
            };
            stack.Children.Add(serversList);

            serversList.SelectionChanged += (_, _) =>
            {
                int idx = serversList.SelectedIndex;
                if (idx >= 0 && idx < _savedServers.Count)
                {
                    SavedServer s = _savedServers[idx];
                    urlBox.Text = s.Url;
                    userBox.Text = s.Username ?? "";
                    rememberMe.IsChecked = s.RememberMe;
                    if (!string.IsNullOrEmpty(s.RefreshToken))
                    {
                        passBox.Text = "";
                    }
                }
            };
        }

        if (_prefill is not null)
        {
            urlBox.Text = _prefill.Url;
            userBox.Text = _prefill.Username ?? "";
            int idx = _savedServers.FindIndex(s =>
                string.Equals(s.Url, _prefill.Url, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0 && idx < _savedServers.Count && serversList is not null)
            {
                serversList.SelectedIndex = idx;
            }
        }

        dialog.Content = new ScrollViewer { Content = stack };

        TaskCompletionSource<ConnectDialogResult?> tcs = new TaskCompletionSource<ConnectDialogResult?>();
        ConnectDialogResult? result = null;

        loginBtn.Click += async (_, _) =>
        {
            string url = urlBox.Text?.Trim() ?? "";
            string user = userBox.Text?.Trim() ?? "";
            string pass = passBox.Text ?? "";

            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user))
            {
                await ShowMessageBox(owner, "Validation", "Server URL and username are required.");
                return;
            }

            // Check for saved session
            SavedServer? saved = _savedServers.FirstOrDefault(s =>
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
            _ = tcs.TrySetResult(result);
        };

        registerBtn.Click += (_, _) =>
        {
            string url = urlBox.Text?.Trim() ?? "";
            string user = userBox.Text?.Trim() ?? "";
            string pass = passBox.Text ?? "";

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
            _ = tcs.TrySetResult(result);
        };

        cancelBtn.Click += (_, _) =>
        {
            dialog.Close();
            _ = tcs.TrySetResult(null);
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(result);

        if (owner is not null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        return await tcs.Task;
    }

    private static async Task ShowMessageBox(Window owner, string title, string message)
    {
        Window msgBox = new Window
        {
            Title = title,
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        StackPanel stack = new StackPanel { Spacing = 10, Margin = new Avalonia.Thickness(15) };
        stack.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        Button okBtn = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        stack.Children.Add(okBtn);
        msgBox.Content = stack;

        TaskCompletionSource tcs = new TaskCompletionSource();
        okBtn.Click += (_, _) => { msgBox.Close(); _ = tcs.TrySetResult(); };
        msgBox.Closed += (_, _) => tcs.TrySetResult();

        await msgBox.ShowDialog(owner);
        await tcs.Task;
    }
}
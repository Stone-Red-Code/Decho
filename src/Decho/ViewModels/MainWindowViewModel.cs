using Avalonia.Controls;

using Decho.Models;
using Decho.Services;
using Decho.Views;

using EchoHub.Client.Commands;
using EchoHub.Client.Config;
using EchoHub.Client.Services;
using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;
using EchoHub.Core.Security;

using MsBox.Avalonia;
using MsBox.Avalonia.Base;
using MsBox.Avalonia.Enums;

using System.Reactive;
using System.Reactive.Linq;

namespace Decho.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly CommandHandler _commandHandler;
    private readonly NotificationSoundService _notificationService;
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
        _notificationService = new NotificationSoundService(ConfigManager.Load().Notifications);

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

    private async void AddServer()
    {
        ConnectDialogResult? result = await ShowConnectDialogAsync(null);
        if (result is null)
        {
            return;
        }

        try
        {
            await ConnectAndSaveAsync(result);
        }
        catch (Exception ex)
        {
            IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(
                "Connection Failed",
                $"Could not connect to server:\n{ex.Message}",
                ButtonEnum.Ok);
            _ = await box.ShowWindowDialogAsync(_mainWindow);
        }
    }

    private async Task ConnectAndSaveAsync(ConnectDialogResult result)
    {
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

    private async Task<ConnectDialogResult?> ShowConnectDialogAsync(SavedServer? prefill)
    {
        if (_mainWindow is null)
        {
            return null;
        }

        ClientConfig config = ConfigManager.Load();
        ConnectDialogWindow dialog = prefill is null
            ? new ConnectDialogWindow(config.SavedServers)
            : new ConnectDialogWindow(config.SavedServers, prefill);
        return await dialog.ShowDialog<ConnectDialogResult?>(_mainWindow);
    }

    private async Task<string?> ShowPromptWindowAsync(string title, string message, string buttonText = "OK", bool isPassword = true)
    {
        string? result = null;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            Window window = new Avalonia.Controls.Window
            {
                Title = title,
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
            };

            TextBox inputBox = new Avalonia.Controls.TextBox { Watermark = isPassword ? "Password" : "Passphrase", PasswordChar = '*' };

            Button okBtn = new Avalonia.Controls.Button { Content = buttonText, IsDefault = true };
            Button cancelBtn = new Avalonia.Controls.Button { Content = "Cancel", IsCancel = true };

            okBtn.Click += (_, _) =>
            {
                result = inputBox.Text;
                window.Close();
            };
            cancelBtn.Click += (_, _) => window.Close();

            StackPanel buttons = new Avalonia.Controls.StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Children = { cancelBtn, okBtn },
            };

            StackPanel panel = new Avalonia.Controls.StackPanel
            {
                Margin = new Avalonia.Thickness(12),
                Spacing = 8,
                Children =
                {
                    new Avalonia.Controls.TextBlock { Text = message },
                    inputBox,
                    buttons,
                },
            };

            window.Content = panel;
            await window.ShowDialog(_mainWindow!);
        });

        return result;
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

        _commandHandler.OnJoinChannel += async (channelName, password) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            ChannelCryptoDto? crypto = await ConnectionService.GetChannelCryptoAsync(serverUrl, channelName);
            bool isEncrypted = crypto is not null && crypto.IsEncrypted;

            string? wirePassword = password;

            if (isEncrypted)
            {
                ServerConnection entry = ConnectionService.Connections[serverUrl];
                entry.Manager.RoomKeys.MarkChannelEncrypted(channelName, true);

                if (!entry.Manager.RoomKeys.HasKey(channelName) && password is null)
                {
                    password = await ShowPromptWindowAsync("Unlock Channel", "Enter the passphrase to unlock messages:", "Unlock");
                    if (string.IsNullOrEmpty(password))
                    {
                        return;
                    }
                }

                if (password is not null)
                {
                    byte[] salt = Convert.FromBase64String(crypto!.EncryptionSalt!);
                    wirePassword = RoomCrypto.DeriveKeys(password, salt).AuthKeyHex;
                }
            }

            ChannelJoinResult result = await ConnectionService.JoinChannelAsync(serverUrl, channelName, wirePassword);
            EnsureChannelInList(serverUrl, channelName);

            if (isEncrypted && !ConnectionService.Connections[serverUrl].Manager.RoomKeys.HasKey(channelName))
            {
                try
                {
                    ChannelJoinResult unlockResult = await ConnectionService.UnlockRoomKeyAsync(
                        serverUrl, channelName, password, crypto!.EncryptionSalt!, result.WrappedRoomKey ?? "");
                    if (unlockResult.History.Count > 0)
                    {
                        result = unlockResult;
                    }
                }
                catch (Exception ex)
                {
                    IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(
                        "Decrypt Error", $"Decrypt failed: {ex.Message}", ButtonEnum.Ok);
                    _ = await box.ShowWindowDialogAsync(_mainWindow);
                    return;
                }
            }

            ChannelModel? channelModel = FindChannel(serverUrl, channelName);
            if (channelModel is not null)
            {
                ChannelViewModel? channelVm = Sidebar.GetServer(serverUrl)?.Channels
                    .FirstOrDefault(c => c.Name == channelName);
                if (channelVm is not null)
                {
                    foreach (MessageModel msg in result.History)
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

        _commandHandler.OnTestSound += _notificationService.PlayTestAsync;

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
            serverVm.CreateChannelRequested += () => HandleCreateChannelRequested(serverVm);
            serverVm.DeleteChannelRequested += () => HandleDeleteChannelRequested(serverVm);
            _ = serverVm.WhenAnyValue(s => s.SelectedChannel)
                .Where(channel => channel is not null)
                .Subscribe(channel => Sidebar.SelectedChannel = channel!);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                int insertIndex = Sidebar.Servers.Count;
                ServerViewModel? existing = Sidebar.GetServer(server.ServerUrl);
                if (existing is not null)
                {
                    insertIndex = Sidebar.Servers.IndexOf(existing);
                    _ = Sidebar.Servers.Remove(existing);
                }

                Sidebar.Servers.Insert(insertIndex, serverVm);

                ChannelViewModel? defaultChannel = serverVm.Channels.FirstOrDefault(c => c.Name == HubConstants.DefaultChannel);
                defaultChannel ??= serverVm.Channels.FirstOrDefault(c => c.IsPublic);
                defaultChannel ??= serverVm.Channels.FirstOrDefault();
                serverVm.SelectedChannel = defaultChannel;

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
            string? username = ConnectionService.GetCurrentUsername(serverUrl);
            bool isMention = !string.IsNullOrEmpty(username)
                && message.Content.Contains($"@{username}", StringComparison.OrdinalIgnoreCase);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ChannelViewModel? channelVm = FindChannelViewModel(serverUrl, message.ChannelName);
                if (channelVm is not null)
                {
                    channelVm.AddMessage(message);
                    bool isSelected = string.Equals(Chat.CurrentChannelName, message.ChannelName, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(Chat.CurrentServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase);
                    if (!isSelected)
                    {
                        channelVm.IncrementUnread(isMention);
                    }
                }
            });

            if (isMention)
            {
                _ = _notificationService.PlayAsync();
            }
        };

        ConnectionService.UserJoined += (serverUrl, channelName, username) =>
        {
            if (string.Equals(channelName, Chat.CurrentChannelName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                _ = RefreshOnlineUsersAsync(serverUrl, channelName);
            }
        };

        ConnectionService.UserLeft += (serverUrl, channelName) =>
        {
            if (string.Equals(channelName, Chat.CurrentChannelName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                _ = RefreshOnlineUsersAsync(serverUrl, channelName);
            }
        };

        ConnectionService.ErrorOccurred += (serverUrl, error) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                StatusText = $"Error: {error}";
            });
        };

        ConnectionService.ChannelAdded += (serverUrl, channel) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ServerViewModel? serverVm = Sidebar.GetServer(serverUrl);
                if (serverVm is not null && serverVm.Channels.All(c => c.Name != channel.Name))
                {
                    serverVm.Channels.Add(new ChannelViewModel(channel));
                }
            });
        };
    }

    private async Task RefreshOnlineUsersAsync(string serverUrl, string channelName)
    {
        try
        {
            List<UserPresenceDto> users = await ConnectionService.GetOnlineUsersAsync(serverUrl, channelName);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Chat.SetOnlineUsers(users);
            });
        }
        catch
        {
            // silently ignore
        }
    }

    private async Task<string?> HandleCommandAsync(string commandText)
    {
        CommandResult result = await _commandHandler.HandleAsync(commandText);
        if (result.Message is not null)
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

    private async Task HandleCreateChannelRequested(ServerViewModel server)
    {
        if (_mainWindow is null)
        {
            return;
        }

        CreateChannelWindow dialog = new CreateChannelWindow();
        bool? result = await dialog.ShowDialog<bool?>(_mainWindow);
        if (result != true)
        {
            return;
        }

        try
        {
            StatusText = "Creating channel...";

            ChannelDto? channel = await ConnectionService.CreateChannelAsync(
                server.ServerUrl, dialog.ResultName!, dialog.ResultTopic, dialog.ResultIsPublic, dialog.ResultPassword);

            if (channel is null)
            {
                StatusText = "Failed to create channel";
                return;
            }

            ChannelJoinResult joinResult = await ConnectionService.JoinChannelAsync(server.ServerUrl, channel.Name);

            ChannelModel channelModel = new ChannelModel(
                channel.Id.ToString(), channel.Name, [], channel.Topic, channel.IsPublic, channel.IsProtected);

            ServerViewModel? serverVm = Sidebar.GetServer(server.ServerUrl);
            if (serverVm is not null)
            {
                ChannelViewModel channelVm = new ChannelViewModel(channelModel);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    serverVm.Channels.Add(channelVm);
                    serverVm.SelectedChannel = channelVm;

                    foreach (MessageModel msg in joinResult.History)
                    {
                        channelVm.AddMessage(msg);
                    }

                    StatusText = $"Created #{channel.Name}";
                });
            }
        }
        catch (Exception ex)
        {
            IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(
                "Error", $"Could not create channel:\n{ex.Message}", ButtonEnum.Ok);
            _ = await box.ShowWindowDialogAsync(_mainWindow);
        }
    }

    private async Task HandleDeleteChannelRequested(ServerViewModel server)
    {
        if (_mainWindow is null)
        {
            return;
        }

        string channelName = Chat.CurrentChannelName;
        if (string.IsNullOrEmpty(channelName) || !string.Equals(Chat.CurrentServerUrl, server.ServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = "No channel selected on this server";
            return;
        }

        if (string.Equals(channelName, HubConstants.DefaultChannel, StringComparison.OrdinalIgnoreCase))
        {
            IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(
                "Cannot Delete", $"The #{HubConstants.DefaultChannel} channel cannot be deleted.", ButtonEnum.Ok);
            _ = await box.ShowWindowDialogAsync(_mainWindow);
            return;
        }

        IMsBox<ButtonResult> confirmBox = MessageBoxManager.GetMessageBoxStandard(
            "Delete Channel",
            $"Are you sure you want to delete #{channelName}?\nThis will remove all messages permanently.",
            ButtonEnum.YesNo);
        ButtonResult confirm = await confirmBox.ShowWindowDialogAsync(_mainWindow);
        if (confirm != ButtonResult.Yes)
        {
            return;
        }

        try
        {
            await ConnectionService.DeleteChannelAsync(server.ServerUrl, channelName);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ChannelViewModel? channelVm = server.Channels.FirstOrDefault(c => c.Name == channelName);
                if (channelVm is not null)
                {
                    _ = server.Channels.Remove(channelVm);
                }

                ChannelViewModel? defaultChannel = server.Channels.FirstOrDefault(c => c.Name == HubConstants.DefaultChannel)
                    ?? server.Channels.FirstOrDefault();
                server.SelectedChannel = defaultChannel;

                StatusText = $"Deleted #{channelName}";
            });
        }
        catch (Exception ex)
        {
            IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(
                "Error", $"Could not delete channel:\n{ex.Message}", ButtonEnum.Ok);
            _ = await box.ShowWindowDialogAsync(_mainWindow);
        }
    }

    private void HandleChannelSelected(ChannelViewModel? channel)
    {
        if (channel is null)
        {
            Chat.SetChannel(null);
            return;
        }

        channel.ClearUnread();

        string serverUrl = FindServerUrlForChannel(channel);
        bool isServerConnected = Sidebar.GetServer(serverUrl)?.IsConnected ?? false;
        Chat.SetChannel(channel, serverUrl, isServerConnected);

        ServerViewModel? currentServer = Sidebar.GetServer(serverUrl);
        if (currentServer is not null)
        {
            Chat.Composer.UpdateAvailableChannels(currentServer.Channels.Select(c => c.Name));
        }

        if (!string.IsNullOrEmpty(serverUrl))
        {
            Chat.Composer.SetCommandHandler(new CommandHandler());

            if (channel.IsProtected && channel.Messages.Count == 0)
            {
                channel.IsLocked = true;
                Chat.Composer.IsConnected = false;
            }

            _ = Task.Run(async () =>
            {
                string? password = null;
                while (true)
                {
                    try
                    {
                        ChannelCryptoDto? crypto = await ConnectionService.GetChannelCryptoAsync(serverUrl, channel.Name);
                        bool isEncrypted = crypto is not null && crypto.IsEncrypted;

                        string? wirePassword = password;

                        if (isEncrypted)
                        {
                            ServerConnection entry = ConnectionService.Connections[serverUrl];
                            entry.Manager.RoomKeys.MarkChannelEncrypted(channel.Name, true);

                            if (!entry.Manager.RoomKeys.HasKey(channel.Name) && password is null)
                            {
                                string? passphrase = await ShowPromptWindowAsync("Unlock Channel", "Enter the passphrase to unlock messages:", "Unlock");
                                if (string.IsNullOrEmpty(passphrase))
                                {
                                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                    {
                                        channel.IsLocked = true;
                                        Chat.Composer.IsConnected = false;
                                    });
                                    break;
                                }
                                password = passphrase;
                            }

                            if (password is not null)
                            {
                                byte[] salt = Convert.FromBase64String(crypto!.EncryptionSalt!);
                                wirePassword = RoomCrypto.DeriveKeys(password, salt).AuthKeyHex;
                            }
                        }

                        ChannelJoinResult joinResult = await ConnectionService.JoinChannelAsync(serverUrl, channel.Name, wirePassword);

                        if (isEncrypted && !ConnectionService.Connections[serverUrl].Manager.RoomKeys.HasKey(channel.Name))
                        {
                            try
                            {
                                ChannelJoinResult unlockResult = await ConnectionService.UnlockRoomKeyAsync(
                                    serverUrl, channel.Name, password, crypto!.EncryptionSalt!, joinResult.WrappedRoomKey ?? "");
                                if (unlockResult.History.Count > 0)
                                {
                                    joinResult = unlockResult;
                                }
                            }
                            catch (Exception ex)
                            {
                                IMsBox<ButtonResult> errBox = MessageBoxManager.GetMessageBoxStandard(
                                    "Decrypt Error", $"Decrypt failed: {ex.Message}", ButtonEnum.Ok);
                                _ = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                                    () => errBox.ShowWindowDialogAsync(_mainWindow));
                            }
                        }

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            channel.IsLocked = false;
                            Chat.Composer.IsConnected = isServerConnected;

                            if (channel.Messages.Count == 0)
                            {
                                foreach (MessageModel msg in joinResult.History)
                                {
                                    channel.AddMessage(msg);
                                }
                            }
                        });

                        _ = RefreshOnlineUsersAsync(serverUrl, channel.Name);
                        return;
                    }
                    catch (EchoHub.Client.Services.ChannelPasswordRequiredException)
                    {
                        string? pwd = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            if (_mainWindow is null)
                            {
                                return null;
                            }

                            return await ShowPromptWindowAsync("Channel Password", "This channel is password protected.\nEnter the channel password:", "Join");
                        });

                        if (string.IsNullOrEmpty(pwd))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                Chat.OnlineUsers.Clear();
                                Chat.ShowOnlineUsers = false;
                                Chat.OnlineUserCount = string.Empty;
                            });
                            return;
                        }

                        password = pwd;
                    }
                    catch
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            Chat.OnlineUsers.Clear();
                            Chat.ShowOnlineUsers = false;
                            Chat.OnlineUserCount = string.Empty;
                        });
                        return;
                    }
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
        catch (Exception ex)
        {
            serverVm.IsConnecting = false;
            StatusText = $"Auto-connect failed for {saved.Name}: {ex.Message}";
        }
    }

    private async Task HandleServerConnectRequested(ServerViewModel serverVm)
    {
        SavedServer? prefill = ConfigManager.Load().SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverVm.ServerUrl, StringComparison.OrdinalIgnoreCase));
        ConnectDialogResult? result = await ShowConnectDialogAsync(prefill);
        if (result is null)
        {
            return;
        }

        try
        {
            serverVm.IsConnecting = true;
            await ConnectAndSaveAsync(result);
        }
        catch (Exception ex)
        {
            serverVm.IsConnecting = false;
            IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(
                "Connection Failed",
                $"Could not connect to server:\n{ex.Message}",
                ButtonEnum.Ok);
            _ = await box.ShowWindowDialogAsync(_mainWindow);
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
using Avalonia.Controls;

using Decho.Models;
using Decho.Services;
using Decho.Views;

using EchoHub.Client.Commands;
using EchoHub.Client.Config;
using EchoHub.Client.Services;
using EchoHub.Client.UI.Dialogs;
using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;
using EchoHub.Core.Services;

using MsBox.Avalonia;
using MsBox.Avalonia.Base;
using MsBox.Avalonia.Enums;

using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Linq;

namespace Decho.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly CommandHandler _commandHandler;
    private readonly NotificationSoundService _notificationService;
    private readonly OsNotificationService _osNotificationService;
    private readonly IReadPositionStore _readPositions;
    private Window? _mainWindow;
    private ChannelViewModel? _openChannel;
    private string _openChannelServerUrl = string.Empty;

    public string Title { get; } = "Decho";

    public SidebarViewModel Sidebar { get; }

    public ChatViewModel Chat { get; }

    public IConnectionService ConnectionService { get; }

    public IChannelService ChannelService { get; }

    public IUserService UserService { get; }

    public IInviteService InviteService { get; }

    public ReactiveCommand<Unit, Unit> AddServerCommand { get; }

    public MainWindowViewModel(IConnectionService connectionService, IChannelService channelService, IUserService userService, IInviteService inviteService, CommandHandler commandHandler, NotificationSoundService notificationService, OsNotificationService osNotificationService, IReadPositionStore readPositions)
    {
        ConnectionService = connectionService;
        ChannelService = channelService;
        UserService = userService;
        InviteService = inviteService;
        _commandHandler = commandHandler;
        _notificationService = notificationService;
        _osNotificationService = osNotificationService;
        _readPositions = readPositions;

        Sidebar = new SidebarViewModel();
        Chat = new ChatViewModel();

        AddServerCommand = ReactiveCommand.Create(AddServer);

        Chat.Composer.SendRequested += HandleSendRequested;
        Chat.Composer.CommandRequested += HandleCommandAsync;
        Chat.LoadMoreRequested += HandleLoadMoreRequested;

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
        SaveOpenChannelReadPosition();
        Chat.Composer.ClearStagedFiles();
        ConnectionService.Dispose();
    }

    public async Task ShowProfileAsync(string serverUrl, string username)
    {
        UserProfileDto? profile;
        try
        {
            profile = await UserService.GetUserProfileAsync(serverUrl, username);
        }
        catch (Exception ex)
        {
            ShowSystemMessage($"Could not load profile: {ex.Message}");
            return;
        }

        if (profile is null)
        {
            return;
        }

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_mainWindow is null)
            {
                return;
            }

            bool isOwnProfile = string.Equals(profile.Username, UserService.GetCurrentUsername(serverUrl), StringComparison.OrdinalIgnoreCase);
            ProfileWindow dialog = new ProfileWindow(profile, serverUrl, UserService, isOwnProfile);
            await dialog.ShowDialog(_mainWindow);
        });
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
            await ShowErrorAsync("Connection Failed", $"Could not connect to server:\n{ex.Message}");
        }
    }

    private async Task ConnectAndSaveAsync(ConnectDialogResult result)
    {
        if (result.SavedRefreshToken is not null)
        {
            await ConnectionService.ConnectWithSavedTokenAsync(
                result.ServerUrl, result.Username, result.SavedRefreshToken, result.RememberMe);
        }
        else
        {
            _ = await ConnectionService.ConnectAsync(
                result.ServerUrl, result.Username, result.Password, result.IsRegister, result.RememberMe, result.DisplayName, result.InviteCode);
        }

        string? refreshToken = ConnectionService.GetRefreshToken(result.ServerUrl);
        ClientConfig config = ConfigManager.Load();
        SavedServer? savedServer = config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, result.ServerUrl, StringComparison.OrdinalIgnoreCase));

        if (savedServer is null)
        {
            savedServer = new SavedServer { Name = new Uri(result.ServerUrl).Host, Url = result.ServerUrl };
            config.SavedServers.Add(savedServer);
        }

        // The entry also holds room keys, left channels and read positions, so only the login fields change.
        savedServer.Username = result.Username;
        savedServer.RefreshToken = result.RememberMe ? refreshToken : null;
        savedServer.RememberMe = result.RememberMe;
        savedServer.LastConnected = DateTimeOffset.Now;
        ConfigManager.Save(config);
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

    private void ShowSystemMessage(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            Chat.Messages.Add(new MessageViewModel(CreateSystemMessage(text, Chat.CurrentChannelName, Chat.CurrentServerUrl)));
        });
    }

    private static MessageModel CreateSystemMessage(string text, string channelName, string serverUrl)
    {
        return new MessageModel(
            Guid.NewGuid().ToString("N"),
            new UserModel("system", "System"),
            DateTimeOffset.Now,
            text,
            channelName,
            serverUrl)
        {
            IsLocal = true,
        };
    }

    /// <summary>
    /// The server rejects things like a muted user's message through this channel rather than by
    /// failing the send, so this can be the only sign that a message did not go through.
    /// </summary>
    private void ShowServerError(string serverUrl, string error)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            bool isOpenServer = string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase);
            if (isOpenServer && !string.IsNullOrEmpty(Chat.CurrentChannelName))
            {
                ShowSystemMessage(error);
            }
            else
            {
                _ = ShowErrorAsync($"Error from {new Uri(serverUrl).Host}", error);
            }
        });
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        IMsBox<ButtonResult> box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok);
        _ = await box.ShowWindowDialogAsync(_mainWindow);
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

            await UserService.UpdateStatusAsync(serverUrl, status ?? UserStatus.Online, message);
        };

        _commandHandler.OnJoinChannel += async (channelName, password) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            string? promptMessage = null;
            while (true)
            {
                try
                {
                    ChannelCryptoDto? crypto = await ChannelService.GetChannelCryptoAsync(serverUrl, channelName);
                    bool isEncrypted = crypto is not null && crypto.IsEncrypted;

                    if (isEncrypted && !ChannelService.HasChannelKey(serverUrl, channelName) && password is null)
                    {
                        password = await ShowPromptWindowAsync("Unlock Channel", promptMessage ?? "Enter the passphrase to unlock messages:", "Unlock");
                        if (string.IsNullOrEmpty(password))
                        {
                            return;
                        }
                    }

                    ChannelJoinResult result = await ChannelService.JoinWithCryptoAsync(serverUrl, channelName, password);
                    EnsureChannelInList(serverUrl, channelName);

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

                    return;
                }
                catch (InvalidOperationException ex) when (ex.Message == "Wrong passphrase")
                {
                    password = null;
                    promptMessage = "Wrong passphrase - try again.";
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

            await ChannelService.LeaveChannelAsync(serverUrl, channel);
        };

        _commandHandler.OnListUsers += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            List<UserPresenceDto> users = await UserService.GetOnlineUsersAsync(serverUrl, channel);
            string userList = string.Join(", ", users.Select(u => u.DisplayName ?? u.Username));
            ShowSystemMessage($"Online in #{channel}: {userList}");
        };

        _commandHandler.OnSetTopic += async topic =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            await UserService.UpdateProfileAsync(serverUrl, null, null, null);
            ChannelService.UpdateChannelTopic(serverUrl, channel, topic);
            Chat.ChannelTopic = topic;
        };

        _commandHandler.OnKickUser += async (username, reason) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.KickUserAsync(serverUrl, username, reason);
        };

        _commandHandler.OnBanUser += async (username, reason) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.BanUserAsync(serverUrl, username, reason);
        };

        _commandHandler.OnUnbanUser += async username =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.UnbanUserAsync(serverUrl, username);
        };

        _commandHandler.OnMuteUser += async (username, duration) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.MuteUserAsync(serverUrl, username, duration);
        };

        _commandHandler.OnUnmuteUser += async username =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.UnmuteUserAsync(serverUrl, username);
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
            await UserService.AssignRoleAsync(serverUrl, username, role);
        };

        _commandHandler.OnNukeChannel += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            string channel = Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channel))
            {
                return;
            }

            await ChannelService.NukeChannelAsync(serverUrl, channel);
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

        _commandHandler.OnSendAction += async text =>
        {
            await HandleSendTextAsync(MessageConventions.FormatAction(text));
        };

        _commandHandler.OnSendBanner += async text =>
        {
            string? banner = AsciiBannerService.Render(text);
            if (banner is null) return;
            await HandleSendTextAsync(banner);
        };

        _commandHandler.OnCreateInvite += async (maxUses, expiresInHours) =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            try
            {
                InviteDto? invite = await InviteService.CreateInviteAsync(serverUrl, maxUses, expiresInHours);
                if (invite is not null)
                    ShowSystemMessage($"Invite code: {invite.Code}");
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Failed to create invite: {ex.Message}");
            }
        };

        _commandHandler.OnListInvites += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            try
            {
                List<InviteDto> invites = await InviteService.GetInvitesAsync(serverUrl);
                if (invites.Count == 0)
                    ShowSystemMessage("No invite codes.");
                else
                    ShowSystemMessage(string.Join(" | ", invites.Select(i => $"{i.Code} ({i.UseCount}/{i.MaxUses})")));
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Failed to list invites: {ex.Message}");
            }
        };

        _commandHandler.OnRevokeInvite += async code =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            try
            {
                await InviteService.RevokeInviteAsync(serverUrl, code);
                ShowSystemMessage($"Invite {code} revoked.");
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Failed to revoke invite: {ex.Message}");
            }
        };

        _commandHandler.OnExportData += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            try
            {
                string data = await UserService.ExportMyDataAsync(serverUrl);
                string fileName = $"echohub-export-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json";
                string downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string filePath = Path.Combine(downloadsPath, "Downloads", fileName);
                await File.WriteAllTextAsync(filePath, data);
                ShowSystemMessage($"Data exported to {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Export failed: {ex.Message}");
            }
        };

        _commandHandler.OnDeleteAccount += async () =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl)) return;

            IMsBox<ButtonResult> confirmBox = MessageBoxManager.GetMessageBoxStandard(
                "Delete Account", "Are you sure you want to permanently delete your account? This cannot be undone.", ButtonEnum.YesNo);
            ButtonResult confirm = await confirmBox.ShowWindowDialogAsync(_mainWindow);
            if (confirm != ButtonResult.Yes) return;

            string? pwd = await ShowPromptWindowAsync("Confirm Password", "Enter your password to confirm account deletion:", "Delete");
            if (string.IsNullOrEmpty(pwd)) return;

            try
            {
                await UserService.DeleteMyAccountAsync(serverUrl, pwd);
                ShowSystemMessage("Account deleted.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Delete failed: {ex.Message}");
            }
        };

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
                    await ChannelService.SendUrlAsync(serverUrl, channel, target, size);
                }
                else
                {
                    await ChannelService.UploadFileAsync(serverUrl, channel, target, size);
                }
            }
            catch (Exception ex)
            {
                ShowSystemMessage($"Send failed: {ex.Message}");
            }
        };

        _commandHandler.OnSetNick += async displayName =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.UpdateProfileAsync(serverUrl, displayName, null, null);
        };

        _commandHandler.OnSetColor += async color =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.UpdateProfileAsync(serverUrl, null, null, color);
        };

        _commandHandler.OnSetAvatar += async target =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            await UserService.SetAvatarAsync(serverUrl, target);
        };

        _commandHandler.OnOpenProfile += async username =>
        {
            string serverUrl = GetCurrentServerUrl();
            if (string.IsNullOrEmpty(serverUrl))
            {
                return;
            }

            string target = username ?? UserService.GetCurrentUsername(serverUrl) ?? string.Empty;
            if (string.IsNullOrEmpty(target))
            {
                return;
            }

            await ShowProfileAsync(serverUrl, target);
        };

        _commandHandler.OnOpenServers += () =>
        {
            ClientConfig config = ConfigManager.Load();
            string servers = string.Join("\n", config.SavedServers.Select(s =>
                $"{s.Name} ({s.Url}) - {s.Username ?? "?"}"));
            ShowSystemMessage(servers);
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
            serverVm.ProfileRequested += () => HandleProfileRequested(serverVm);
            serverVm.JoinChannelRequested += () => HandleJoinChannelRequested(serverVm);
            serverVm.InvitesRequested += () => HandleInvitesRequested(serverVm);
            serverVm.UnmuteUserRequested += () => HandleUnmuteUserRequested(serverVm);
            serverVm.UnbanUserRequested += () => HandleUnbanUserRequested(serverVm);
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
                    Debug.WriteLine("Ready");
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
                }
            });
        };

        ConnectionService.MessageReceived += (serverUrl, message) =>
        {
            string? username = UserService.GetCurrentUsername(serverUrl);
            bool isReplyToMe = !string.IsNullOrEmpty(username)
                && string.Equals(message.ReplyTo?.SenderUsername, username, StringComparison.OrdinalIgnoreCase);
            bool isMention = isReplyToMe
                || (!string.IsNullOrEmpty(username)
                    && message.Content.Contains($"@{username}", StringComparison.OrdinalIgnoreCase));

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
                string title = $"@{username} – {new Uri(serverUrl).Host}";
                string body = $"{message.Author.DisplayName} in #{message.ChannelName}: {MessageViewModel.FormatContent(message.Content, message.Author.DisplayName)}";
                _osNotificationService.Show(title, body);
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

        ConnectionService.UserStatusChanged += (serverUrl, presence) =>
        {
            // Refreshing also covers invisible users, who drop out of the server's list.
            if (string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(Chat.CurrentChannelName))
            {
                _ = RefreshOnlineUsersAsync(serverUrl, Chat.CurrentChannelName);
            }
        };

        ConnectionService.ChannelTopicChanged += (serverUrl, channelName, topic) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (FindChannelViewModel(serverUrl, channelName) is { } channel)
                {
                    ApplyChannelTopic(channel, topic);
                }
            });
        };

        ConnectionService.ChannelDeleted += (serverUrl, channelName) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ServerViewModel? serverVm = Sidebar.GetServer(serverUrl);
                if (serverVm is null) return;

                ChannelViewModel? channelVm = serverVm.Channels
                    .FirstOrDefault(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
                if (channelVm is not null)
                    serverVm.Channels.Remove(channelVm);

                if (string.Equals(channelName, Chat.CurrentChannelName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    Chat.ClearMessages();
                }
            });
        };

        ConnectionService.MessageDeleted += (serverUrl, channelName, messageId) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => RemoveMessage(serverUrl, channelName, messageId));
        };

        ConnectionService.ChannelNuked += (serverUrl, channelName) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                ChannelViewModel? channelVm = FindChannelViewModel(serverUrl, channelName);
                if (channelVm is not null)
                {
                    if (Chat.Composer.ReplyTarget is { } target && channelVm.Messages.Contains(target))
                    {
                        Chat.Composer.ClearReplyTarget();
                    }

                    channelVm.ClearMessages();
                    channelVm.AddMessage(CreateSystemMessage("Channel history has been cleared by a moderator.", channelName, serverUrl));
                }
            });
        };

        ConnectionService.UserKicked += (serverUrl, channelName, username, reason) =>
        {
            if (IsCurrentUser(serverUrl, username))
            {
                return;
            }

            string reasonText = reason is not null ? $" ({reason})" : string.Empty;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                FindChannelViewModel(serverUrl, channelName)?.AddMessage(
                    CreateSystemMessage($"{username} was kicked{reasonText}", channelName, serverUrl));
            });
        };

        ConnectionService.UserBanned += (serverUrl, username, reason) =>
        {
            if (IsCurrentUser(serverUrl, username))
            {
                return;
            }

            string reasonText = reason is not null ? $" ({reason})" : string.Empty;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Bans apply to the whole server, so one notice in the open channel is enough.
                if (string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    FindChannelViewModel(serverUrl, Chat.CurrentChannelName)?.AddMessage(
                        CreateSystemMessage($"{username} was banned{reasonText}", Chat.CurrentChannelName, serverUrl));
                }
            });
        };

        ConnectionService.ForceDisconnected += (serverUrl, reason) =>
        {
            // The server only asks clients to leave, so the connection has to be closed here.
            // Posted because stopping it from inside its own event handler is not safe.
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                SaveOpenChannelReadPosition();

                try
                {
                    await ConnectionService.DisconnectAsync(serverUrl);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Disconnect failed: {ex.Message}");
                }

                await ShowErrorAsync($"Disconnected from {new Uri(serverUrl).Host}", reason);
            });
        };

        ConnectionService.ErrorOccurred += ShowServerError;

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

        ConnectionService.Reconnected += serverUrl =>
        {
            _ = RefreshCurrentChannelAsync(serverUrl);
        };
    }

    /// <summary>
    /// Hides the action when it is sure to fail. The server has the final say, because a
    /// moderator also needs a higher role than the author, which isn't known for offline users.
    /// </summary>
    public bool CanDeleteMessage(MessageViewModel message)
    {
        string serverUrl = message.ServerUrl ?? Chat.CurrentServerUrl;
        if (message.Model.IsLocal || Sidebar.GetServer(serverUrl)?.IsConnected != true)
        {
            return false;
        }

        if (IsCurrentUser(serverUrl, message.AuthorUsername))
        {
            return true;
        }

        UserViewModel? self = Chat.OnlineUsers.FirstOrDefault(u => IsCurrentUser(serverUrl, u.Username));
        return self is null || self.Role >= ServerRole.Mod;
    }

    public async Task DeleteMessageAsync(MessageViewModel message)
    {
        IMsBox<ButtonResult> confirmBox = MessageBoxManager.GetMessageBoxStandard(
            "Delete Message", "Delete this message? This cannot be undone.", ButtonEnum.YesNo);
        ButtonResult confirm = await confirmBox.ShowWindowDialogAsync(_mainWindow);
        if (confirm != ButtonResult.Yes)
        {
            return;
        }

        string serverUrl = message.ServerUrl ?? Chat.CurrentServerUrl;
        try
        {
            await ChannelService.DeleteMessageAsync(serverUrl, message.Model.Id);
            RemoveMessage(serverUrl, message.Model.ChannelName, message.Model.Id);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could Not Delete Message", ex.Message);
        }
    }

    /// <summary>
    /// Like <see cref="CanDeleteMessage"/>, this hides what is sure to fail and leaves the rest to
    /// the server. A user who isn't in the open channel has an unknown role, so they stay eligible.
    /// </summary>
    public ModerationOptions GetModerationOptions(string serverUrl, string username)
    {
        ServerViewModel? server = Sidebar.GetServer(serverUrl);
        if (server is not { IsConnected: true, OwnRole: { } ownRole } || IsCurrentUser(serverUrl, username))
        {
            return ModerationOptions.None;
        }

        UserViewModel? target = string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase)
            ? Chat.OnlineUsers.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase))
            : null;
        bool outranksTarget = target is null || target.Role < ownRole;
        bool isMod = ownRole >= ServerRole.Mod;
        bool isAdmin = ownRole >= ServerRole.Admin;

        ServerRole[] assignableRoles = isAdmin && target?.Role != ServerRole.Owner
            ? [.. new[] { ServerRole.Admin, ServerRole.Mod, ServerRole.Member }.Where(role => role < ownRole)]
            : [];

        return new ModerationOptions(
            // Kicking only disconnects, so it needs someone who is known to be online.
            CanKick: isMod && target is not null && outranksTarget,
            CanMute: isMod && outranksTarget,
            CanUnmute: isMod,
            CanBan: isAdmin && outranksTarget,
            CanUnban: isAdmin && target is null,
            assignableRoles,
            target?.Role);
    }

    public async Task KickUserAsync(string serverUrl, string username)
    {
        ModerationWindow dialog = ModerationWindow.ForReason($"Kick {username}", $"{username} will be disconnected but can reconnect right away.", "Kick");
        if (await dialog.ShowDialog<bool?>(_mainWindow!) == true)
        {
            await RunModerationAsync("Could Not Kick User", () => UserService.KickUserAsync(serverUrl, username, dialog.ResultReason));
        }
    }

    public async Task BanUserAsync(string serverUrl, string username)
    {
        ModerationWindow dialog = ModerationWindow.ForReason($"Ban {username}", $"{username} will be disconnected and can't log in until unbanned.", "Ban");
        if (await dialog.ShowDialog<bool?>(_mainWindow!) == true)
        {
            await RunModerationAsync("Could Not Ban User", () => UserService.BanUserAsync(serverUrl, username, dialog.ResultReason));
        }
    }

    public async Task MuteUserAsync(string serverUrl, string username)
    {
        ModerationWindow dialog = ModerationWindow.ForMute(username);
        if (await dialog.ShowDialog<bool?>(_mainWindow!) == true)
        {
            await RunModerationAsync(
                "Could Not Mute User",
                () => UserService.MuteUserAsync(serverUrl, username, dialog.ResultDurationMinutes),
                $"{username} was muted.");
        }
    }

    public Task UnmuteUserAsync(string serverUrl, string username)
    {
        return RunModerationAsync("Could Not Unmute User", () => UserService.UnmuteUserAsync(serverUrl, username), $"{username} was unmuted.");
    }

    public Task UnbanUserAsync(string serverUrl, string username)
    {
        return RunModerationAsync("Could Not Unban User", () => UserService.UnbanUserAsync(serverUrl, username), $"{username} was unbanned.");
    }

    public async Task AssignRoleAsync(string serverUrl, string username, ServerRole role)
    {
        IMsBox<ButtonResult> confirmBox = MessageBoxManager.GetMessageBoxStandard("Set Role", $"Make {username} {role}?", ButtonEnum.YesNo);
        if (await confirmBox.ShowWindowDialogAsync(_mainWindow) != ButtonResult.Yes)
        {
            return;
        }

        await RunModerationAsync("Could Not Set Role", () => UserService.AssignRoleAsync(serverUrl, username, role), $"{username} is now {role}.");

        // Role changes aren't broadcast, so the users panel has to ask for them.
        if (string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
        {
            await RefreshOnlineUsersAsync(serverUrl, Chat.CurrentChannelName);
        }
    }

    private async Task RunModerationAsync(string failureTitle, Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(failureTitle, ex.Message);
            return;
        }

        if (successMessage is not null)
        {
            ShowSystemMessage(successMessage);
        }
    }

    public async Task OpenChannelAsync(string serverUrl, string channelName)
    {
        if (await TryOpenChannelAsync(serverUrl, channelName) is { } error)
        {
            ShowSystemMessage(error);
        }
    }

    /// <summary>Opens a channel by name, adding it to the list if it exists but isn't listed. Returns what went wrong, if anything.</summary>
    private async Task<string?> TryOpenChannelAsync(string serverUrl, string channelName)
    {
        ServerViewModel? server = Sidebar.GetServer(serverUrl);
        if (server is not { IsConnected: true })
        {
            return "Not connected to this server.";
        }

        ChannelViewModel? channel = FindListedChannel(server, channelName);
        if (channel is null)
        {
            ChannelMetaDto? meta;
            try
            {
                meta = await ChannelService.GetChannelMetaAsync(serverUrl, channelName);
            }
            catch (Exception ex)
            {
                return $"Could not open #{channelName}: {ex.Message}";
            }

            if (meta is null)
            {
                return $"#{channelName} doesn't exist.";
            }

            channel = FindListedChannel(server, channelName);
            if (channel is null)
            {
                channel = new ChannelViewModel(new ChannelModel(
                    meta.Id.ToString(),
                    meta.Name,
                    [],
                    meta.Topic,
                    isPublic: false,
                    meta.IsProtected,
                    meta.IsEncrypted,
                    isSystem: false));
                server.Channels.Add(channel);
            }
        }

        server.SelectedChannel = channel;
        return null;
    }

    private static ChannelViewModel? FindListedChannel(ServerViewModel server, string channelName)
    {
        return server.Channels.FirstOrDefault(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task SetChannelTopicAsync(ServerViewModel server, ChannelViewModel channel)
    {
        TextPromptWindow dialog = new TextPromptWindow(
            $"Topic for #{channel.Name}",
            "Only the channel's creator can change its topic. Leave this empty to clear it.",
            "Topic",
            "Save",
            channel.Topic,
            ValidationConstants.MaxChannelTopicLength,
            allowEmpty: true);
        await dialog.ShowDialog(_mainWindow!);
        if (dialog.Result is not { } text)
        {
            return;
        }

        string? topic = text.Length == 0 ? null : text;
        try
        {
            await ChannelService.SetChannelTopicAsync(server.ServerUrl, channel.Name, topic);
            ApplyChannelTopic(channel, topic);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could Not Set Topic", ex.Message);
        }
    }

    public async Task ClearChannelHistoryAsync(ServerViewModel server, ChannelViewModel channel)
    {
        IMsBox<ButtonResult> confirmBox = MessageBoxManager.GetMessageBoxStandard(
            "Clear Channel History",
            $"Delete all messages and attachments in #{channel.Name} for everyone?\nThis cannot be undone.",
            ButtonEnum.YesNo);
        if (await confirmBox.ShowWindowDialogAsync(_mainWindow) == ButtonResult.Yes)
        {
            await RunModerationAsync("Could Not Clear History", () => ChannelService.NukeChannelAsync(server.ServerUrl, channel.Name));
        }
    }

    public async Task LeaveChannelAsync(ServerViewModel server, ChannelViewModel channel)
    {
        try
        {
            await ChannelService.LeaveChannelAsync(server.ServerUrl, channel.Name);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could Not Leave Channel", ex.Message);
            return;
        }

        channel.ClearUnread();

        // Moves away because a left channel can't be used, and opening it again rejoins it.
        if (ReferenceEquals(channel, _openChannel))
        {
            server.SelectedChannel = FindListedChannel(server, HubConstants.DefaultChannel);
        }
    }

    public async Task DeleteChannelAsync(ServerViewModel server, ChannelViewModel channel)
    {
        IMsBox<ButtonResult> confirmBox = MessageBoxManager.GetMessageBoxStandard(
            "Delete Channel",
            $"Are you sure you want to delete #{channel.Name}?\nThis will remove all messages permanently.",
            ButtonEnum.YesNo);
        if (await confirmBox.ShowWindowDialogAsync(_mainWindow) != ButtonResult.Yes)
        {
            return;
        }

        try
        {
            await ChannelService.DeleteChannelAsync(server.ServerUrl, channel.Name);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Could Not Delete Channel", ex.Message);
            return;
        }

        bool wasOpen = ReferenceEquals(channel, _openChannel);
        _ = server.Channels.Remove(channel);
        if (wasOpen)
        {
            server.SelectedChannel = FindListedChannel(server, HubConstants.DefaultChannel);
        }
    }

    private async Task HandleJoinChannelRequested(ServerViewModel server)
    {
        TextPromptWindow dialog = new TextPromptWindow("Join Channel", "Enter the name of the channel to join.", "Channel name", "Join");
        await dialog.ShowDialog(_mainWindow!);
        if (dialog.Result is { } name
            && await TryOpenChannelAsync(server.ServerUrl, name.TrimStart('#')) is { } error)
        {
            await ShowErrorAsync("Could Not Join Channel", error);
        }
    }

    private async Task HandleInvitesRequested(ServerViewModel server)
    {
        await new InvitesWindow(InviteService, server.ServerUrl).ShowDialog(_mainWindow!);
    }

    private async Task HandleUnmuteUserRequested(ServerViewModel server)
    {
        TextPromptWindow dialog = new TextPromptWindow("Unmute User", "Enter the username to unmute.", "Username", "Unmute");
        await dialog.ShowDialog(_mainWindow!);
        if (dialog.Result is { } username)
        {
            await UnmuteUserAsync(server.ServerUrl, username.TrimStart('@'));
        }
    }

    private async Task HandleUnbanUserRequested(ServerViewModel server)
    {
        TextPromptWindow dialog = new TextPromptWindow("Unban User", "Enter the username to unban.", "Username", "Unban");
        await dialog.ShowDialog(_mainWindow!);
        if (dialog.Result is { } username)
        {
            await UnbanUserAsync(server.ServerUrl, username.TrimStart('@'));
        }
    }

    private void ApplyChannelTopic(ChannelViewModel channel, string? topic)
    {
        channel.Topic = topic;
        if (ReferenceEquals(channel, _openChannel))
        {
            Chat.UpdateTopic(channel);
        }
    }

    private void RemoveMessage(string serverUrl, string channelName, string messageId)
    {
        if (Chat.Composer.ReplyTarget?.Model.Id == messageId)
        {
            Chat.Composer.ClearReplyTarget();
        }

        FindChannelViewModel(serverUrl, channelName)?.RemoveMessage(messageId);
    }

    private bool IsCurrentUser(string serverUrl, string username)
    {
        return string.Equals(username, UserService.GetCurrentUsername(serverUrl), StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleSendTextAsync(string text)
    {
        string serverUrl = GetCurrentServerUrl();
        string channelName = Chat.CurrentChannelName;
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channelName))
        {
            return;
        }

        try
        {
            await ChannelService.SendMessageAsync(serverUrl, channelName, text);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Send failed: {ex.Message}");
        }
    }

    private async Task RefreshOnlineUsersAsync(string serverUrl, string channelName)
    {
        try
        {
            List<UserPresenceDto> users = await UserService.GetOnlineUsersAsync(serverUrl, channelName);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Chat.SetOnlineUsers(users);
            });

            await UpdateOwnRoleAsync(serverUrl, users);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshOnlineUsers failed: {ex.Message}");
        }
    }

    private async Task UpdateOwnRoleAsync(string serverUrl, List<UserPresenceDto> users)
    {
        ServerViewModel? server = Sidebar.GetServer(serverUrl);
        if (server is null)
        {
            return;
        }

        ServerRole? role = users.FirstOrDefault(u => IsCurrentUser(serverUrl, u.Username))?.Role;

        // Invisible users aren't listed, so their role has to come from their profile.
        if (role is null && server.OwnRole is null && UserService.GetCurrentUsername(serverUrl) is { } username)
        {
            role = (await UserService.GetUserProfileAsync(serverUrl, username))?.Role;
        }

        if (role is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => server.OwnRole = role);
        }
    }

    private async Task RefreshCurrentChannelAsync(string serverUrl)
    {
        if (!string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase))
            return;

        string channelName = Chat.CurrentChannelName;
        if (string.IsNullOrEmpty(channelName))
            return;

        ChannelViewModel? channel = FindChannelViewModel(serverUrl, channelName);
        if (channel is null)
            return;

        try
        {
            List<MessageModel> latest = await ChannelService.GetHistoryAsync(serverUrl, channelName, HubConstants.DefaultHistoryCount, 0);
            HashSet<string> existingIds = channel.Messages.Select(m => m.Model.Id).ToHashSet();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                foreach (MessageModel msg in latest)
                {
                    if (!existingIds.Contains(msg.Id))
                    {
                        channel.AddMessage(msg);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RefreshCurrentChannel failed: {ex.Message}");
        }
    }

    private async Task<string?> HandleCommandAsync(string commandText)
    {
        CommandResult result = await _commandHandler.HandleAsync(commandText);
        if (result.Message is not null)
        {
            ShowSystemMessage(result.Message);
        }
        return result.Message;
    }

    private void HandleSendRequested(string serverUrl, string text, IReadOnlyList<string> filePaths, Guid? replyToMessageId)
    {
        if (string.IsNullOrEmpty(Chat.CurrentChannelName))
        {
            return;
        }

        if (filePaths.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ChannelService.SendMessageWithAttachmentsAsync(serverUrl, Chat.CurrentChannelName, text, filePaths);
                }
                catch (Exception ex)
                {
                    ShowSystemMessage($"Could not send attachments: {ex.Message}");
                }
                finally
                {
                    foreach (string filePath in filePaths)
                    {
                        PastedImageStore.Delete(filePath);
                    }
                }
            });
            return;
        }

        if (_commandHandler.IsCommand(text))
        {
            _ = HandleCommandAsync(text);
            return;
        }

        string channelName = Chat.CurrentChannelName;
        _ = Task.Run(async () =>
        {
            try
            {
                await ChannelService.SendMessageAsync(serverUrl, channelName, text, replyToMessageId);
            }
            catch (Exception ex)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    ShowSendFailure(serverUrl, channelName, text, ex.Message);
                });
            }
        });
    }

    private void ShowSendFailure(string serverUrl, string channelName, string text, string error)
    {
        FindChannelViewModel(serverUrl, channelName)?.AddMessage(
            CreateSystemMessage($"Could not send message: {error}", channelName, serverUrl));

        // Gives the text back unless the user has moved on to another channel or a new message.
        bool isOpenChannel = string.Equals(channelName, Chat.CurrentChannelName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(serverUrl, Chat.CurrentServerUrl, StringComparison.OrdinalIgnoreCase);
        if (isOpenChannel && string.IsNullOrEmpty(Chat.Composer.Draft))
        {
            Chat.Composer.Draft = text;
        }
    }

    private async Task HandleProfileRequested(ServerViewModel server)
    {
        string? username = UserService.GetCurrentUsername(server.ServerUrl);
        if (!string.IsNullOrEmpty(username))
        {
            await ShowProfileAsync(server.ServerUrl, username);
        }
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
            ChannelDto? channel = await ChannelService.CreateChannelAsync(
                server.ServerUrl, dialog.ResultName!, dialog.ResultTopic, dialog.ResultIsPublic, dialog.ResultPassword);

            if (channel is null)
            {
                ShowSystemMessage("Failed to create channel");
                return;
            }

            ChannelJoinResult joinResult = await ChannelService.JoinChannelAsync(server.ServerUrl, channel.Name);

            ChannelModel channelModel = new ChannelModel(
                channel.Id.ToString(), channel.Name, [], channel.Topic, channel.IsPublic, channel.IsProtected, channel.IsEncrypted, channel.IsSystem);

            ServerViewModel? serverVm = Sidebar.GetServer(server.ServerUrl);
            if (serverVm is not null)
            {
                ChannelViewModel channelVm = new ChannelViewModel(channelModel);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!serverVm.Channels.Any(c => c.Name == channel.Name))
                    {
                        serverVm.Channels.Add(channelVm);
                    }
                    serverVm.SelectedChannel = channelVm;

                    foreach (MessageModel msg in joinResult.History)
                    {
                        channelVm.AddMessage(msg);
                    }

                    ShowSystemMessage($"Created #{channel.Name}");
                });
            }
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Error", $"Could not create channel:\n{ex.Message}");
        }
    }

    private async void HandleLoadMoreRequested()
    {
        string serverUrl = Chat.CurrentServerUrl;
        string channelName = Chat.CurrentChannelName;
        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channelName))
        {
            return;
        }

        ChannelViewModel? channel = FindChannelViewModel(serverUrl, channelName);
        if (channel is null)
        {
            return;
        }

        Chat.IsLoadingMore = true;

        try
        {
            int offset = channel.Messages.Count(m => !m.Model.IsLocal);
            List<MessageModel> older = await ChannelService.GetHistoryAsync(serverUrl, channelName, HubConstants.DefaultHistoryCount, offset);

            // A channel that was left in the meantime got trimmed, so these would leave a gap in it.
            if (older.Count > 0 && ReferenceEquals(_openChannel, channel))
            {
                channel.InsertMessages(older);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadMore failed: {ex.Message}");
        }
        finally
        {
            Chat.IsLoadingMore = false;
        }
    }

    private void SaveOpenChannelReadPosition()
    {
        if (_openChannel is not null && _openChannel.MarkAllRead() && _openChannel.LastReadMessageId is { } messageId)
        {
            _readPositions.Save(_openChannelServerUrl, _openChannel.Name, messageId);
        }
    }

    private void HandleChannelSelected(ChannelViewModel? channel)
    {
        // Leaving a channel uses up its marker, so the next unread messages get a fresh one.
        SaveOpenChannelReadPosition();
        _openChannel?.ClearUnreadMarker();
        ChannelViewModel? previousChannel = _openChannel;
        _openChannel = channel;

        if (channel is null)
        {
            Chat.SetChannel(null);
            previousChannel?.TrimHistory(HubConstants.DefaultHistoryCount);
            return;
        }

        channel.ClearUnread();

        string serverUrl = FindServerUrlForChannel(channel);
        _openChannelServerUrl = serverUrl;
        channel.LastReadMessageId ??= _readPositions.Get(serverUrl, channel.Name);
        bool isServerConnected = Sidebar.GetServer(serverUrl)?.IsConnected ?? false;
        Chat.SetChannel(channel, serverUrl, isServerConnected);
        previousChannel?.TrimHistory(HubConstants.DefaultHistoryCount);

        ServerViewModel? currentServer = Sidebar.GetServer(serverUrl);
        if (currentServer is not null)
        {
            Chat.Composer.UpdateAvailableChannels(currentServer.Channels.Select(c => c.Name));
        }

        if (!string.IsNullOrEmpty(serverUrl))
        {
            Chat.Composer.SetCommandHandler(new CommandHandler());

            if (channel.IsSystem)
            {
                Chat.Composer.IsReadOnly = true;
            }
            else if (channel.IsProtected && channel.Messages.Count == 0)
            {
                channel.IsLocked = true;
                Chat.Composer.IsReadOnly = true;
            }
            else
            {
                Chat.Composer.IsReadOnly = false;
            }

            _ = Task.Run(async () =>
            {
                string? password = null;
                string? promptMessage = null;
                while (true)
                {
                    try
                    {
                        ChannelCryptoDto? crypto = await ChannelService.GetChannelCryptoAsync(serverUrl, channel.Name);
                        bool isEncrypted = crypto is not null && crypto.IsEncrypted;

                        if (isEncrypted && !ChannelService.HasChannelKey(serverUrl, channel.Name))
                        {
                            string? passphrase = await ShowPromptWindowAsync("Unlock Channel", promptMessage ?? "Enter the passphrase to unlock messages:", "Unlock");
                            if (string.IsNullOrEmpty(passphrase))
                            {
                                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                                {
                                    channel.IsLocked = true;
                                    Chat.Composer.IsReadOnly = true;
                                });
                                break;
                            }
                            password = passphrase;
                        }

                        ChannelJoinResult joinResult = await ChannelService.JoinWithCryptoAsync(serverUrl, channel.Name, password);

                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            channel.IsLocked = false;
                            Chat.Composer.IsConnected = isServerConnected;

                            if (channel.IsProtected)
                            {
                                Chat.Composer.IsReadOnly = false;
                            }

                            HashSet<string> existingIds = channel.Messages.Select(m => m.Model.Id).ToHashSet();
                            foreach (var msg in joinResult.History.Where(msg => !existingIds.Contains(msg.Id)))
                            {
                                channel.AddMessage(msg);
                            }

                            if (ReferenceEquals(_openChannel, channel)
                                && channel.PlaceUnreadMarker(UserService.GetCurrentUsername(serverUrl)) is { } marker)
                            {
                                Chat.RevealMessageRequested?.Invoke(marker.Model.Id);
                            }
                        });

                        _ = RefreshOnlineUsersAsync(serverUrl, channel.Name);
                        return;
                    }
                    catch (ChannelPasswordRequiredException)
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
                                Chat.OnlineUserCount = string.Empty;
                            });
                            return;
                        }

                        password = pwd;
                    }
                    catch (InvalidOperationException ex) when (ex.Message == "Wrong passphrase")
                    {
                        password = null;
                        promptMessage = "Wrong passphrase - try again.";
                    }
                    catch (Exception ex)
                    {
                        ShowSystemMessage($"Could not join #{channel.Name}: {ex.Message}");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            Chat.OnlineUsers.Clear();
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
            Debug.WriteLine($"Auto-connect failed for {saved.Name}: {ex.Message}");
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
            await ShowErrorAsync("Connection Failed", $"Could not connect to server:\n{ex.Message}");
        }
    }

    private async Task HandleServerDisconnectRequested(ServerViewModel serverVm)
    {
        SaveOpenChannelReadPosition();

        try
        {
            await ConnectionService.DisconnectAsync(serverVm.ServerUrl);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Disconnect error: {ex.Message}");
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
            Debug.WriteLine($"Remove error: {ex.Message}");
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
                [],
                isPublic: true,
                isProtected: false,
                isEncrypted: false,
                isSystem: false);
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
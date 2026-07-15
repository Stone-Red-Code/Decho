using Avalonia.Controls;

using EchoHub.Client.Config;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using System.Linq;

namespace Decho.Views;

public sealed partial class ConnectDialogWindow : Window
{
    private readonly List<SavedServer> _savedServers;

    public ConnectDialogWindow()
    {
        InitializeComponent();
        _savedServers = [];
    }

    public ConnectDialogWindow(List<SavedServer> savedServers, SavedServer? prefill = null)
    {
        InitializeComponent();
        _savedServers = savedServers;

        if (savedServers.Count > 0)
        {
            SavedHeader.IsVisible = true;
            ServersList.IsVisible = true;
            ServersList.ItemsSource = savedServers.Select(s =>
                $"{s.Name} ({s.Url}) - {s.Username ?? "?"}").ToList();

            ServersList.SelectionChanged += (_, _) =>
            {
                int idx = ServersList.SelectedIndex;
                if (idx >= 0 && idx < savedServers.Count)
                {
                    SavedServer s = savedServers[idx];
                    UrlBox.Text = s.Url;
                    UserBox.Text = s.Username ?? "";
                    RememberCheck.IsChecked = s.RememberMe;
                    if (!string.IsNullOrEmpty(s.RefreshToken))
                    {
                        PassBox.Text = "";
                    }
                }
            };
        }

        if (prefill is not null)
        {
            UrlBox.Text = prefill.Url;
            UserBox.Text = prefill.Username ?? "";
            int idx = savedServers.FindIndex(s =>
                string.Equals(s.Url, prefill.Url, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                ServersList.SelectedIndex = idx;
            }
        }
    }

    private async void OnLoginClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string url = UrlBox.Text?.Trim() ?? "";
        string user = UserBox.Text?.Trim() ?? "";
        string pass = PassBox.Text ?? "";

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user))
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Validation", "Server URL and username are required.", ButtonEnum.Ok);
            await box.ShowWindowDialogAsync(this);
            return;
        }

        SavedServer? saved = _savedServers.FirstOrDefault(s =>
            string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(s.Username, user, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(s.RefreshToken));

        if (string.IsNullOrEmpty(pass) && saved is not null)
        {
            Close(new ConnectDialogResult
            {
                ServerUrl = url,
                Username = user,
                IsSavedSession = true,
                SavedRefreshToken = saved.RefreshToken,
                RememberMe = saved.RememberMe,
            });
        }
        else
        {
            if (string.IsNullOrEmpty(pass))
            {
                var box = MessageBoxManager.GetMessageBoxStandard("Validation", "Password is required.", ButtonEnum.Ok);
                await box.ShowWindowDialogAsync(this);
                return;
            }

            Close(new ConnectDialogResult
            {
                ServerUrl = url,
                Username = user,
                Password = pass,
                IsRegister = false,
                RememberMe = RememberCheck.IsChecked ?? false,
            });
        }
    }

    private async void OnRegisterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string url = UrlBox.Text?.Trim() ?? "";
        string user = UserBox.Text?.Trim() ?? "";
        string pass = PassBox.Text ?? "";

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            var box = MessageBoxManager.GetMessageBoxStandard("Validation", "Server URL, username, and password are required.", ButtonEnum.Ok);
            await box.ShowWindowDialogAsync(this);
            return;
        }

        Close(new ConnectDialogResult
        {
            ServerUrl = url,
            Username = user,
            Password = pass,
            IsRegister = true,
            RememberMe = RememberCheck.IsChecked ?? false,
        });
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
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

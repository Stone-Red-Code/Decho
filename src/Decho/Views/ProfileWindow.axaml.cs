using Avalonia.Controls;
using Avalonia.Media;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

namespace Decho.Views;

public sealed partial class ProfileWindow : Window
{
    public ProfileWindow()
    {
        InitializeComponent();
    }

    public ProfileWindow(UserProfileDto profile, string serverUrl)
    {
        InitializeComponent();

        Title = $"Profile - {profile.Username}";
        Avatar.ServerUrl = serverUrl;
        Avatar.Username = profile.Username;

        ShowProfile(profile);
    }

    private static string FormatStatus(UserStatus status)
    {
        return status switch
        {
            UserStatus.Online => "Online",
            UserStatus.Away => "Away",
            UserStatus.DoNotDisturb => "Do Not Disturb",
            UserStatus.Invisible => "Invisible",
            _ => "Unknown",
        };
    }

    private void ShowProfile(UserProfileDto profile)
    {
        Avatar.FallbackName = profile.DisplayName;
        UsernameText.Text = profile.Username;

        StatusText.Text = FormatStatus(profile.Status);
        StatusDot.Fill = GetStatusBrush(profile.Status);

        StatusMessageText.Text = profile.StatusMessage;
        StatusMessageText.IsVisible = !string.IsNullOrWhiteSpace(profile.StatusMessage);

        DisplayNameText.Text = profile.DisplayName ?? profile.Username;

        if (!string.IsNullOrWhiteSpace(profile.NicknameColor)
            && Color.TryParse(profile.NicknameColor, out Color parsedColor))
        {
            ColorText.Text = profile.NicknameColor;
            ColorSwatch.Background = new SolidColorBrush(parsedColor);
            ColorSwatch.IsVisible = true;
        }
        else
        {
            ColorText.Text = "-";
            ColorSwatch.IsVisible = false;
        }

        RoleText.Text = FormatRole(profile.Role);
        BioText.Text = string.IsNullOrWhiteSpace(profile.Bio) ? "-" : profile.Bio;
        JoinedText.Text = profile.CreatedAt.ToString("g");
        LastSeenText.Text = profile.LastSeenAt.ToString("g");
    }

    private static IBrush GetStatusBrush(UserStatus status)
    {
        return status switch
        {
            UserStatus.Online => new SolidColorBrush(Colors.LimeGreen),
            UserStatus.Away => new SolidColorBrush(Colors.Goldenrod),
            UserStatus.DoNotDisturb => new SolidColorBrush(Colors.IndianRed),
            UserStatus.Invisible => new SolidColorBrush(Colors.Gray),
            _ => new SolidColorBrush(Colors.White),
        };
    }

    private static string FormatRole(ServerRole role)
    {
        return role switch
        {
            ServerRole.Admin => "Admin",
            ServerRole.Mod => "Mod",
            ServerRole.Member => "Member",
            _ => role.ToString(),
        };
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
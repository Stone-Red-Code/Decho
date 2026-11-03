using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Decho.Services;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using System.Diagnostics;

namespace Decho.Views;

public sealed partial class ProfileWindow : Window
{
    private const double EnlargedAvatarSize = 480;

    private readonly string _serverUrl = string.Empty;
    private readonly IUserService? _userService;
    private UserProfileDto? _profile;

    public ProfileWindow()
    {
        InitializeComponent();
    }

    public ProfileWindow(UserProfileDto profile, string serverUrl, IUserService userService, bool isOwnProfile)
    {
        InitializeComponent();

        _serverUrl = serverUrl;
        _userService = userService;
        EditButton.IsVisible = isOwnProfile;

        Title = $"Profile - {profile.Username}";
        Avatar.ServerUrl = serverUrl;
        Avatar.Username = profile.Username;
        Avatar.PictureChanged += () => Avatar.Cursor = Avatar.CurrentPicture is null ? Cursor.Default : new Cursor(StandardCursorType.Hand);

        ShowProfile(profile);
    }

    internal static string FormatStatus(UserStatus status)
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
        _profile = profile;

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

    private async void OnAvatarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (Avatar.CurrentPicture is not Bitmap picture || _profile is null)
        {
            return;
        }

        Func<Task<string?>> downloadAsync = () =>
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
            picture.Save(tempPath);
            return Task.FromResult<string?>(tempPath);
        };

        ImageViewerWindow viewer = new ImageViewerWindow(picture, $"{_profile.Username}.png", downloadAsync, EnlargedAvatarSize);
        await viewer.ShowDialog(this);
    }

    private async void OnEditClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_profile is null || _userService is null)
        {
            return;
        }

        EditProfileWindow dialog = new EditProfileWindow(_profile, _serverUrl, _userService);
        bool saved = await dialog.ShowDialog<bool>(this);
        if (!saved)
        {
            return;
        }

        try
        {
            UserProfileDto? updated = await _userService.GetUserProfileAsync(_serverUrl, _profile.Username);
            if (updated is not null)
            {
                ShowProfile(updated);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to reload profile: {ex.Message}");
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
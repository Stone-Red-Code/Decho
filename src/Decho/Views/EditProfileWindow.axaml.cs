using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Decho.Services;

using EchoHub.Core.Constants;
using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

namespace Decho.Views;

public partial class EditProfileWindow : Window
{
    private static readonly UserStatus[] SelectableStatuses =
        [UserStatus.Online, UserStatus.Away, UserStatus.DoNotDisturb, UserStatus.Invisible];

    private readonly UserProfileDto? _profile;
    private readonly string _serverUrl = string.Empty;
    private readonly IUserService? _userService;
    private byte[]? _pendingAvatar;

    public EditProfileWindow()
    {
        InitializeComponent();
    }

    public EditProfileWindow(UserProfileDto profile, string serverUrl, IUserService userService)
    {
        InitializeComponent();

        _profile = profile;
        _serverUrl = serverUrl;
        _userService = userService;

        AvatarPreview.ServerUrl = serverUrl;
        AvatarPreview.Username = profile.Username;
        AvatarPreview.FallbackName = profile.DisplayName;

        DisplayNameBox.Text = profile.DisplayName ?? profile.Username;
        DisplayNameBox.MaxLength = ValidationConstants.MaxDisplayNameLength;
        BioBox.Text = profile.Bio;
        BioBox.MaxLength = ValidationConstants.MaxBioLength;
        ColorBox.Text = profile.NicknameColor;

        StatusBox.ItemsSource = SelectableStatuses.Select(ProfileWindow.FormatStatus).ToList();
        StatusBox.SelectedIndex = Math.Max(Array.IndexOf(SelectableStatuses, profile.Status), 0);
        StatusMessageBox.Text = profile.StatusMessage;
    }

    private async void OnChangePictureClicked(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select profile picture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp"] },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using MemoryStream buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;

            byte[] prepared = await Task.Run(() => AvatarImageProcessor.PrepareForUpload(buffer));

            using MemoryStream preview = new MemoryStream(prepared);
            AvatarPreview.ShowPreview(new Bitmap(preview));
            _pendingAvatar = prepared;
            ShowError(null);
        }
        catch (Exception ex)
        {
            ShowError($"Could not read that image: {ex.Message}");
        }
    }

    private void OnColorTextChanged(object? sender, TextChangedEventArgs e)
    {
        ColorSwatch.Background = Color.TryParse(ColorBox.Text?.Trim(), out Color color)
            ? new SolidColorBrush(color)
            : null;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_profile is null || _userService is null)
        {
            return;
        }

        string color = ColorBox.Text?.Trim() ?? string.Empty;
        if (color.Length > 0 && !ValidationConstants.HexColorRegex().IsMatch(color))
        {
            ShowError("Name color must be a hex color such as #FF5500.");
            _ = ColorBox.Focus();
            return;
        }

        // The server stores a blank display name as-is, which would hide the user's name in chat.
        string displayName = string.IsNullOrWhiteSpace(DisplayNameBox.Text) ? _profile.Username : DisplayNameBox.Text.Trim();
        string bio = BioBox.Text?.Trim() ?? string.Empty;
        string? statusMessage = string.IsNullOrWhiteSpace(StatusMessageBox.Text) ? null : StatusMessageBox.Text.Trim();
        UserStatus status = SelectableStatuses[Math.Max(StatusBox.SelectedIndex, 0)];

        SaveButton.IsEnabled = false;
        ShowError(null);

        try
        {
            if (_pendingAvatar is not null)
            {
                await _userService.SetAvatarAsync(_serverUrl, _pendingAvatar);

                // Uploads are rate limited, so a retry after a later failure must not send it again.
                _pendingAvatar = null;
            }

            string? changedDisplayName = displayName == (_profile.DisplayName ?? _profile.Username) ? null : displayName;
            string? changedBio = bio == (_profile.Bio ?? string.Empty) ? null : bio;
            string? changedColor = color == (_profile.NicknameColor ?? string.Empty) ? null : color;

            if (changedDisplayName is not null || changedBio is not null || changedColor is not null)
            {
                await _userService.UpdateProfileAsync(_serverUrl, changedDisplayName, changedBio, changedColor);
            }

            if (status != _profile.Status || statusMessage != _profile.StatusMessage)
            {
                await _userService.UpdateStatusAsync(_serverUrl, status, statusMessage);
            }

            Close(true);
        }
        catch (Exception ex)
        {
            ShowError($"Could not save your profile: {ex.Message}");
            SaveButton.IsEnabled = true;
        }
    }

    private void ShowError(string? message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = message is not null;
    }
}

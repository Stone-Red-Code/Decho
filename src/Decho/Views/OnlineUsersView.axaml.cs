using Avalonia.Controls;

using Decho.ViewModels;

using EchoHub.Core.DTOs;

namespace Decho.Views;

public partial class OnlineUsersView : UserControl
{
    public OnlineUsersView()
    {
        InitializeComponent();
    }

    private async void OnUserNamePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.DataContext is not UserViewModel user)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window parent)
        {
            return;
        }

        if (parent.DataContext is not MainWindowViewModel mainVm)
        {
            return;
        }

        string serverUrl = mainVm.Chat.CurrentServerUrl;
        UserProfileDto? profile = await mainVm.ConnectionService.GetUserProfileAsync(serverUrl, user.Username);
        if (profile is null)
        {
            return;
        }

        ProfileWindow dialog = new ProfileWindow(profile);
        await dialog.ShowDialog(parent);
    }
}
using Avalonia.Controls;

using Decho.ViewModels;

namespace Decho.Views;

public partial class OnlineUsersView : UserControl
{
    public OnlineUsersView()
    {
        InitializeComponent();
    }

    private async void OnUserNamePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not UserViewModel user)
        {
            return;
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return;
        }

        await mainVm.ShowProfileAsync(user.ServerUrl, user.Username);
    }
}

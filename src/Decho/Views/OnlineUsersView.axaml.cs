using Avalonia.Controls;
using Avalonia.Input;

using Decho.ViewModels;

namespace Decho.Views;

public partial class OnlineUsersView : UserControl
{
    public OnlineUsersView()
    {
        InitializeComponent();
    }

    private async void OnUserNamePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || sender is not Control control
            || control.DataContext is not UserViewModel user)
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

    private void OnUserRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && sender is Control { ContextMenu: null } row)
        {
            row.ContextMenu = CreateContextMenu(row);
        }
    }

    private ContextMenu CreateContextMenu(Control row)
    {
        MenuItem mentionItem = new MenuItem { Header = "Mention" };
        mentionItem.Click += (_, _) =>
        {
            if (row.DataContext is UserViewModel user)
            {
                this.GetMainWindowViewModel()?.Chat.Composer.InsertMention(user.Username);
            }
        };

        MenuItem profileItem = new MenuItem { Header = "View profile" };
        profileItem.Click += (_, _) =>
        {
            if (row.DataContext is UserViewModel user)
            {
                _ = this.GetMainWindowViewModel()?.ShowProfileAsync(user.ServerUrl, user.Username);
            }
        };

        MenuItem moderateItem = new MenuItem { Header = "Moderate" };

        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, args) =>
        {
            MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
            if (mainVm is null || row.DataContext is not UserViewModel user)
            {
                args.Cancel = true;
                return;
            }

            mentionItem.IsVisible = mainVm.Chat.Composer.IsEditable;
            ModerationMenu.Populate(moderateItem, mainVm, user.ServerUrl, user.Username);
        };
        menu.Items.Add(mentionItem);
        menu.Items.Add(profileItem);
        menu.Items.Add(moderateItem);
        return menu;
    }
}

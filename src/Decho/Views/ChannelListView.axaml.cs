using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

using Decho.ViewModels;

using EchoHub.Core.Constants;

namespace Decho.Views;

public partial class ChannelListView : UserControl
{
    public ChannelListView()
    {
        InitializeComponent();

        // Runs before the list sees the press, because selecting a channel opens and joins it.
        ChannelList.AddHandler(PointerPressedEvent, OnChannelListPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnChannelListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (e.Source is Control source && source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item)
        {
            item.ContextMenu ??= CreateContextMenu(item);
            e.Handled = true;
        }
    }

    private ContextMenu CreateContextMenu(ListBoxItem item)
    {
        MenuItem setTopicItem = CreateMenuItem(item, "Set Topic...", (mainVm, server, channel) => mainVm.SetChannelTopicAsync(server, channel));
        MenuItem passphraseItem = CreateMenuItem(item, "Change Passphrase...", (mainVm, server, channel) => mainVm.ChangePassphraseAsync(server, channel));
        MenuItem clearHistoryItem =CreateMenuItem(item, "Clear History...", (mainVm, server, channel) => mainVm.ClearChannelHistoryAsync(server, channel));
        MenuItem leaveItem = CreateMenuItem(item, "Leave Channel", (mainVm, server, channel) => mainVm.LeaveChannelAsync(server, channel));
        MenuItem deleteItem = CreateMenuItem(item, "Delete Channel...", (mainVm, server, channel) => mainVm.DeleteChannelAsync(server, channel));

        ContextMenu menu = new ContextMenu();
        menu.Opening += (_, args) =>
        {
            if (DataContext is not ServerViewModel { IsConnected: true } server || item.DataContext is not ChannelViewModel channel)
            {
                args.Cancel = true;
                return;
            }

            bool isDefault = string.Equals(channel.Name, HubConstants.DefaultChannel, StringComparison.OrdinalIgnoreCase);
            setTopicItem.IsVisible = !channel.IsSystem;
            passphraseItem.IsVisible = channel.IsEncrypted;
            clearHistoryItem.IsVisible = server.CanModerate;
            leaveItem.IsVisible = !isDefault;
            deleteItem.IsVisible = !isDefault && !channel.IsSystem;

            args.Cancel = !setTopicItem.IsVisible && !clearHistoryItem.IsVisible && !leaveItem.IsVisible;
        };
        menu.Items.Add(setTopicItem);
        menu.Items.Add(passphraseItem);
        menu.Items.Add(clearHistoryItem);
        menu.Items.Add(leaveItem);
        menu.Items.Add(deleteItem);
        return menu;
    }

    private MenuItem CreateMenuItem(ListBoxItem item, string header, Func<MainWindowViewModel, ServerViewModel, ChannelViewModel, Task> action)
    {
        MenuItem menuItem = new MenuItem { Header = header };
        menuItem.Click += (_, _) =>
        {
            if (this.GetMainWindowViewModel() is { } mainVm
                && DataContext is ServerViewModel server
                && item.DataContext is ChannelViewModel channel)
            {
                _ = action(mainVm, server, channel);
            }
        };
        return menuItem;
    }
}

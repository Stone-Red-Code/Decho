using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Decho.ViewModels;

namespace Decho.Views;

public partial class SidebarView : UserControl
{
    public static readonly StyledProperty<ChannelViewModel?> SelectedChannelProperty =
        AvaloniaProperty.Register<SidebarView, ChannelViewModel?>(
            nameof(SelectedChannel),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public ChannelViewModel? SelectedChannel
    {
        get => GetValue(SelectedChannelProperty);
        set => SetValue(SelectedChannelProperty, value);
    }

    public SidebarView()
    {
        InitializeComponent();
    }

    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open(button);
        }
    }
}
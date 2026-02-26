using Avalonia;
using Avalonia.Controls;

using Decho.ViewModels;

namespace Decho.Views;

public partial class SidebarView : UserControl
{
    public static readonly StyledProperty<ChannelViewModel?> SelectedChannelProperty =
        AvaloniaProperty.Register<SidebarView, ChannelViewModel?>(
            nameof(SelectedChannel),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public SidebarView()
    {
        InitializeComponent();
    }

    public ChannelViewModel? SelectedChannel
    {
        get => GetValue(SelectedChannelProperty);
        set => SetValue(SelectedChannelProperty, value);
    }
}

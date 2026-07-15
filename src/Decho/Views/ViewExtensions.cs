using Avalonia;
using Avalonia.Controls;

using Decho.ViewModels;

namespace Decho.Views;

internal static class ViewExtensions
{
    public static T? GetDataContext<T>(this Control control) where T : class
        => control.DataContext as T;

    public static MainWindowViewModel? GetMainWindowViewModel(this Control control)
        => TopLevel.GetTopLevel(control)?.DataContext as MainWindowViewModel;
}

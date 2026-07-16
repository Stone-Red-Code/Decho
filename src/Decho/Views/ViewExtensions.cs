using Avalonia.Controls;

using Decho.ViewModels;

namespace Decho.Views;

internal static class ViewExtensions
{
    public static T? GetDataContext<T>(this Control control) where T : class
    {
        return control.DataContext as T;
    }

    public static MainWindowViewModel? GetMainWindowViewModel(this Control control)
    {
        return TopLevel.GetTopLevel(control)?.DataContext as MainWindowViewModel;
    }
}
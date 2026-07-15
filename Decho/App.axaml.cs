using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Decho.ViewModels;
using Decho.Views;

namespace Decho;

public partial class App : Application
{
    private MainWindowViewModel? _viewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _viewModel = new MainWindowViewModel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };

            _viewModel.SetMainWindow(desktop.MainWindow);

            desktop.Exit += (_, _) => _viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
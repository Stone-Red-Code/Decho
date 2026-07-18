using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Decho.Services;
using Decho.ViewModels;
using Decho.Views;

using EchoHub.Client.Commands;
using EchoHub.Client.Config;
using EchoHub.Client.Services;

using Splat;

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
            RegisterDependencies();

            _viewModel = Locator.Current.GetService<MainWindowViewModel>()!;

            desktop.MainWindow = new MainWindow
            {
                DataContext = _viewModel,
            };

            _viewModel.SetMainWindow(desktop.MainWindow);

            desktop.Exit += (_, _) => _viewModel.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterDependencies()
    {
        ConnectionService connectionService = new();
        Locator.CurrentMutable.RegisterLazySingleton<IConnectionService>(() => connectionService);

        IConnectionStore store = connectionService.Store;
        Locator.CurrentMutable.RegisterLazySingleton<IConnectionStore>(() => store);
        ICryptoService crypto = new CryptoService(store);
        Locator.CurrentMutable.RegisterLazySingleton<ICryptoService>(() => crypto);

        IChannelService channelService = new ChannelService(store, crypto);
        Locator.CurrentMutable.RegisterLazySingleton<IChannelService>(() => channelService);

        IUserService userService = new UserService(store);
        Locator.CurrentMutable.RegisterLazySingleton<IUserService>(() => userService);

        IInviteService inviteService = new InviteService(store);
        Locator.CurrentMutable.RegisterLazySingleton<IInviteService>(() => inviteService);

        CommandHandler commandHandler = new CommandHandler();
        Locator.CurrentMutable.RegisterLazySingleton<CommandHandler>(() => commandHandler);

        NotificationSoundService notificationService = new(ConfigManager.Load().Notifications);
        Locator.CurrentMutable.RegisterLazySingleton<NotificationSoundService>(() => notificationService);

        Locator.CurrentMutable.Register<MainWindowViewModel>(() =>
            new MainWindowViewModel(
                Locator.Current.GetService<IConnectionService>()!,
                Locator.Current.GetService<IChannelService>()!,
                Locator.Current.GetService<IUserService>()!,
                Locator.Current.GetService<IInviteService>()!,
                Locator.Current.GetService<CommandHandler>()!,
                Locator.Current.GetService<NotificationSoundService>()!));
    }
}

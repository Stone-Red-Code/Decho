using OsNotifications;

using Serilog;

namespace Decho.Services;

public sealed class OsNotificationService
{
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized) return;
        try
        {
            Notifications.SetGuiApplication(true);
            _initialized = true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize OS notifications");
        }
    }

    public void Show(string title, string? body = null)
    {
        if (!_initialized) return;
        try
        {
            Notifications.ShowNotification(title, body ?? string.Empty);
        }
        catch (PlatformNotSupportedException ex)
        {
            Log.Warning(ex, "OS notifications not supported on this platform");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to show OS notification");
        }
    }
}
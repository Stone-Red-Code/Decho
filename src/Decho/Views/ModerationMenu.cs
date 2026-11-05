using Avalonia.Controls;

using Decho.ViewModels;

using EchoHub.Core.Models;

namespace Decho.Views;

internal static class ModerationMenu
{
    /// <summary>Fills a menu item with what the user may do to <paramref name="username"/> and hides it when that is nothing.</summary>
    public static void Populate(MenuItem menu, MainWindowViewModel mainVm, string serverUrl, string username)
    {
        ModerationOptions options = mainVm.GetModerationOptions(serverUrl, username);

        menu.Items.Clear();
        AddIf(options.CanKick, "Kick...", () => mainVm.KickUserAsync(serverUrl, username));
        AddIf(options.CanMute, "Mute...", () => mainVm.MuteUserAsync(serverUrl, username));
        AddIf(options.CanUnmute, "Unmute", () => mainVm.UnmuteUserAsync(serverUrl, username));
        AddIf(options.CanBan, "Ban...", () => mainVm.BanUserAsync(serverUrl, username));
        AddIf(options.CanUnban, "Unban", () => mainVm.UnbanUserAsync(serverUrl, username));

        if (options.AssignableRoles.Count > 0)
        {
            MenuItem roles = new MenuItem { Header = "Set role" };
            foreach (ServerRole role in options.AssignableRoles)
            {
                MenuItem item = new MenuItem { Header = role.ToString(), IsEnabled = role != options.CurrentRole };
                item.Click += (_, _) => _ = mainVm.AssignRoleAsync(serverUrl, username, role);
                _ = roles.Items.Add(item);
            }

            _ = menu.Items.Add(roles);
        }

        menu.IsVisible = menu.Items.Count > 0;

        void AddIf(bool condition, string header, Func<Task> action)
        {
            if (condition)
            {
                MenuItem item = new MenuItem { Header = header };
                item.Click += (_, _) => _ = action();
                _ = menu.Items.Add(item);
            }
        }
    }
}

using EchoHub.Client.Config;

namespace Decho.Services;

internal sealed class ConfigPersistenceService : IConfigPersistenceService
{
    public void SaveRefreshToken(string serverUrl, string token, string userId)
    {
        ModifyConfig(serverUrl, (config, saved) =>
        {
            if (saved is null)
            {
                saved = new SavedServer
                {
                    Name = new Uri(serverUrl).Host,
                    Url = serverUrl,
                    Username = userId,
                    RememberMe = true,
                    LastConnected = DateTimeOffset.Now,
                };
                config.SavedServers.Add(saved);
            }

            saved.RefreshToken = token;
            saved.LastConnected = DateTimeOffset.Now;
        });
    }

    public void RemoveServerFromConfig(string serverUrl)
    {
        ModifyConfig(serverUrl, (config, saved) =>
        {
            if (saved is not null)
            {
                _ = config.SavedServers.Remove(saved);
            }
        });
    }

    public void RemoveFromLeftChannels(string serverUrl, string channelName)
    {
        ClientConfig config = ConfigManager.Load();
        SavedServer? saved = config.SavedServers
            .FirstOrDefault(s => string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase));
        if (saved is not null && saved.LeftChannels.Remove(channelName))
        {
            ConfigManager.Save(config);
        }
    }

    public void AddLeftChannel(string serverUrl, string channelName)
    {
        ClientConfig config = ConfigManager.Load();
        SavedServer? saved = config.SavedServers
            .FirstOrDefault(s => string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase));
        if (saved is not null && !saved.LeftChannels.Contains(channelName, StringComparer.OrdinalIgnoreCase))
        {
            saved.LeftChannels.Add(channelName);
            ConfigManager.Save(config);
        }
    }

    private static void ModifyConfig(string serverUrl, Action<ClientConfig, SavedServer?> action)
    {
        ClientConfig config = ConfigManager.Load();
        SavedServer? saved = config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase));
        action(config, saved);
        ConfigManager.Save(config);
    }
}
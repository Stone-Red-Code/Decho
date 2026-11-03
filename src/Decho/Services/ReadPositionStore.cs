using EchoHub.Client.Config;

namespace Decho.Services;

/// <summary>
/// Keeps read positions in the config entry of each saved server. EchoHub's own client uses the
/// same field and writes dashed ids, while Decho's message ids have no dashes, so ids are converted here.
/// </summary>
internal sealed class ReadPositionStore : IReadPositionStore
{
    public string? Get(string serverUrl, string channelName)
    {
        SavedServer? server = FindServer(ConfigManager.Load(), serverUrl);
        if (server is not null
            && server.LastReadMessages.TryGetValue(channelName, out string? saved)
            && Guid.TryParse(saved, out Guid id))
        {
            return id.ToString("N");
        }

        return null;
    }

    public void Save(string serverUrl, string channelName, string messageId)
    {
        ClientConfig config = ConfigManager.Load();
        SavedServer? server = FindServer(config, serverUrl);
        if (server is not null && Guid.TryParse(messageId, out Guid id))
        {
            server.LastReadMessages[channelName] = id.ToString();
            ConfigManager.Save(config);
        }
    }

    private static SavedServer? FindServer(ClientConfig config, string serverUrl)
    {
        return config.SavedServers.FirstOrDefault(s =>
            string.Equals(s.Url, serverUrl, StringComparison.OrdinalIgnoreCase));
    }
}

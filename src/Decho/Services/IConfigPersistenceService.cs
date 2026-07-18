namespace Decho.Services;

public interface IConfigPersistenceService
{
    void SaveRefreshToken(string serverUrl, string token, string userId);

    void RemoveServerFromConfig(string serverUrl);

    void RemoveFromLeftChannels(string serverUrl, string channelName);

    void AddLeftChannel(string serverUrl, string channelName);
}
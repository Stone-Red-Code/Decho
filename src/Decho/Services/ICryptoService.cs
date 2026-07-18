namespace Decho.Services;

public interface ICryptoService
{
    void MarkChannelEncrypted(string serverUrl, string channelName, bool isEncrypted);

    bool HasChannelKey(string serverUrl, string channelName);

    bool TryGetRoomKey(string serverUrl, string channelName, out byte[]? key);
}
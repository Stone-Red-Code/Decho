namespace Decho.Services;

public interface IReadPositionStore
{
    /// <summary>Returns the id of the last message read in a channel, or null when none is saved.</summary>
    string? Get(string serverUrl, string channelName);

    void Save(string serverUrl, string channelName, string messageId);
}

using Avalonia.Media.Imaging;

namespace Decho.Services;

public interface IAvatarService
{
    /// <summary>Raised with (serverUrl, username) when a known avatar changes. May fire off the UI thread.</summary>
    event Action<string, string>? AvatarChanged;

    /// <summary>Returns the user's picture, or null when they have none or it could not be loaded.</summary>
    Task<Bitmap?> GetAvatarAsync(string serverUrl, string username);

    /// <summary>Stores avatar data that was just received from the server.</summary>
    void Publish(string serverUrl, string username, string? avatarAscii);
}

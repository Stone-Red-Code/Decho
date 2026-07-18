using EchoHub.Core.Security;

using System.Diagnostics;

namespace Decho.Services;

internal sealed class AttachmentService : IAttachmentService
{
    private readonly IConnectionStore _store;

    public AttachmentService(IConnectionStore store)
    {
        _store = store;
    }

    public async Task<string?> DownloadAttachmentAsync(string serverUrl, string channelName, string relativeUrl, string fileName)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null)
        {
            return null;
        }

        try
        {
            string? tempPath = await entry.ApiClient.DownloadFileToTempAsync(relativeUrl, fileName);
            if (tempPath is null)
            {
                return null;
            }

            if (entry.Manager.RoomKeys.TryGetKey(channelName, out byte[]? roomKey))
            {
                try
                {
                    byte[] encrypted = await File.ReadAllBytesAsync(tempPath);
                    await File.WriteAllBytesAsync(tempPath, RoomCrypto.DecryptBytes(encrypted, roomKey));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Decrypt failed for attachment: {ex.Message}");
                }
            }

            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Download failed: {ex.Message}");
            return null;
        }
    }

    public async Task<byte[]?> DownloadImageBytesAsync(string serverUrl, string channelName, string relativeUrl)
    {
        ServerConnection? entry = _store.Get(serverUrl);
        if (entry is null)
        {
            return null;
        }

        try
        {
            string? tempPath = await entry.ApiClient.DownloadFileToTempAsync(relativeUrl, "image");
            if (tempPath is null)
            {
                return null;
            }

            byte[] bytes = await File.ReadAllBytesAsync(tempPath);
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Temp file deletion failed: {ex.Message}");
            }

            if (entry.Manager.RoomKeys.TryGetKey(channelName, out byte[]? roomKey))
            {
                try
                {
                    bytes = RoomCrypto.DecryptBytes(bytes, roomKey);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Decrypt failed for image: {ex.Message}");
                }
            }

            return bytes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Image download failed: {ex.Message}");
            return null;
        }
    }
}

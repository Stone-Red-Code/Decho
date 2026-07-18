namespace Decho.Services;

public interface IAttachmentService
{
    Task<string?> DownloadAttachmentAsync(string serverUrl, string channelName, string relativeUrl, string fileName);

    Task<byte[]?> DownloadImageBytesAsync(string serverUrl, string channelName, string relativeUrl);
}
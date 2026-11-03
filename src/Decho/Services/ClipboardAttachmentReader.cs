using EchoHub.Client.Services;

namespace Decho.Services;

internal sealed class ClipboardAttachmentReader : IClipboardAttachmentReader
{
    public IReadOnlyList<string> GetFiles()
    {
        return ClipboardFiles.TryGetFiles(out List<string> files) ? files : [];
    }

    public byte[]? GetImage()
    {
        return ClipboardImage.TryGetPng(out byte[] png) ? png : null;
    }
}

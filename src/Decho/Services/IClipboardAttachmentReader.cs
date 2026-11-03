namespace Decho.Services;

/// <summary>Reads what Avalonia's text clipboard can't: copied files and raw image data.</summary>
public interface IClipboardAttachmentReader
{
    IReadOnlyList<string> GetFiles();

    /// <summary>Returns PNG bytes, or null when the clipboard holds no image.</summary>
    byte[]? GetImage();
}

using System.Diagnostics;

namespace Decho.Services;

/// <summary>
/// Keeps pasted images on disk while they are staged, because attachments are sent from file paths.
/// </summary>
internal static class PastedImageStore
{
    private const string FileName = "image.png";

    private static readonly string Root = Path.Combine(Path.GetTempPath(), "Decho", "pasted");

    public static string Save(byte[] png)
    {
        // Each image gets its own folder so that all of them can keep the same file name.
        string directory = Path.Combine(Root, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, FileName);
        File.WriteAllBytes(path, png);
        return path;
    }

    /// <summary>Deletes a pasted image. Any other path is left alone, so this is safe for every staged file.</summary>
    public static void Delete(string path)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (directory is null || !string.Equals(Path.GetDirectoryName(directory), Root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Failed to delete pasted image: {ex.Message}");
        }
    }
}

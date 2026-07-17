using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Decho.Views;

public sealed partial class ImageViewerWindow : Window
{
    private readonly Func<Task<string?>>? _downloadAsync;

    public ImageViewerWindow()
    {
        InitializeComponent();
    }

    public ImageViewerWindow(Bitmap bitmap, string fileName, Func<Task<string?>>? downloadAsync = null)
    {
        InitializeComponent();
        Title = fileName;
        ImageView.Source = bitmap;
        _downloadAsync = downloadAsync;
        DownloadButton.IsVisible = downloadAsync is not null;

        Opened += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            if (Width > MaxWidth) Width = MaxWidth;
            if (Height > MaxHeight) Height = MaxHeight;

            MaxHeight = double.MaxValue;
            MaxWidth = double.MaxValue;
        };
    }

    private async void OnDownloadClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_downloadAsync is null) return;

        string? tempPath = await _downloadAsync();
        if (tempPath is null) return;

        IStorageFile? file = await this.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = Title,
        });

        if (file?.TryGetLocalPath() is string savePath)
        {
            File.Copy(tempPath, savePath, overwrite: true);
        }

        try
        {
            File.Delete(tempPath);
        }
        catch
        {
            // Ignore if temp file cannot be deleted
        }
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
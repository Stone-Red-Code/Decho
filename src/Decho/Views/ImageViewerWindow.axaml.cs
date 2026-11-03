using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Decho.Views;

public sealed partial class ImageViewerWindow : Window
{
    private const double ImagePadding = 16;
    private const double ButtonRowHeight = 48;

    private readonly Func<Task<string?>>? _downloadAsync;

    public ImageViewerWindow()
    {
        InitializeComponent();
    }

    /// <param name="initialImageSize">Opens at this image size for square images too small to fill the window sharply.</param>
    public ImageViewerWindow(Bitmap bitmap, string fileName, Func<Task<string?>>? downloadAsync = null, double? initialImageSize = null)
    {
        InitializeComponent();
        Title = fileName;
        ImageView.Source = bitmap;
        _downloadAsync = downloadAsync;
        DownloadButton.IsVisible = downloadAsync is not null;

        if (initialImageSize is double imageSize)
        {
            RenderOptions.SetBitmapInterpolationMode(ImageView, BitmapInterpolationMode.HighQuality);
            SizeToContent = SizeToContent.Manual;
            Width = imageSize + ImagePadding;
            Height = imageSize + ImagePadding + ButtonRowHeight;
        }

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
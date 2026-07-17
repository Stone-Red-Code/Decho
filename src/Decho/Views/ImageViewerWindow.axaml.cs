using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace Decho.Views;

public sealed partial class ImageViewerWindow : Window
{
    public ImageViewerWindow()
    {
        InitializeComponent();
    }

    public ImageViewerWindow(Bitmap bitmap, string fileName)
    {
        InitializeComponent();
        Title = fileName;
        ImageView.Source = bitmap;

        Opened += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            if (Width > MaxWidth) Width = MaxWidth;
            if (Height > MaxHeight) Height = MaxHeight;

            MaxHeight = double.MaxValue;
            MaxWidth = double.MaxValue;
        };
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
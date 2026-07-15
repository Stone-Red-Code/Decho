using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Decho.ViewModels;

namespace Decho.Views;

public partial class MessageItemView : UserControl
{
    private CancellationTokenSource? _loadCts;
    private bool _pendingLoad;

    public MessageItemView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _pendingLoad = DataContext is MessageViewModel msg && msg.IsImage && msg.AttachmentUrl is not null;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_pendingLoad) return;
        _pendingLoad = false;
        var msg = (MessageViewModel)DataContext!;
        _ = LoadImageAsync(msg, _loadCts!.Token);
    }

    private async Task LoadImageAsync(MessageViewModel msg, CancellationToken ct)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.DataContext is not MainWindowViewModel mainVm) return;

            var bytes = await mainVm.ConnectionService.DownloadImageBytesAsync(
                msg.ServerUrl ?? "", msg.AttachmentUrl!);

            ct.ThrowIfCancellationRequested();
            if (bytes is null || bytes.Length == 0) return;

            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            ct.ThrowIfCancellationRequested();
            var image = this.FindControl<Image>("MessageImage");
            if (image is not null)
                image.Source = bitmap;
        }
        catch
        {
            // Image failed to load — filename is shown as fallback
        }
    }

    private async void OnDownloadClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MessageViewModel msg) return;
        if (msg.AttachmentUrl is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var mainVm = topLevel.DataContext as MainWindowViewModel;
        if (mainVm is null) return;

        var tempPath = await mainVm.ConnectionService.DownloadAttachmentAsync(
            msg.ServerUrl ?? "", msg.AttachmentUrl, msg.AttachmentFileName ?? "download");

        if (tempPath is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = msg.AttachmentFileName ?? "download",
        });

        if (file?.TryGetLocalPath() is string savePath)
        {
            File.Copy(tempPath, savePath, overwrite: true);
        }

        try { File.Delete(tempPath); }
        catch { }
    }
}

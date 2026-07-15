using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Decho.ViewModels;

using EchoHub.Core.DTOs;

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
        if (!_pendingLoad)
        {
            return;
        }

        _pendingLoad = false;
        MessageViewModel msg = (MessageViewModel)DataContext!;
        _ = LoadImageAsync(msg, _loadCts!.Token);
    }

    private async Task LoadImageAsync(MessageViewModel msg, CancellationToken ct)
    {
        try
        {
            MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
            if (mainVm is null)
            {
                return;
            }

            byte[]? bytes = await mainVm.ConnectionService.DownloadImageBytesAsync(
                msg.ServerUrl ?? "", msg.AttachmentUrl!);

            ct.ThrowIfCancellationRequested();
            if (bytes is null || bytes.Length == 0)
            {
                return;
            }

            using MemoryStream stream = new MemoryStream(bytes);
            Bitmap bitmap = new Bitmap(stream);
            ct.ThrowIfCancellationRequested();
            Image? image = this.FindControl<Image>("MessageImage");
            _ = image?.Source = bitmap;
        }
        catch
        {
            // Image failed to load — filename is shown as fallback
        }
    }

    private async void OnAuthorNamePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        MessageViewModel? msg = this.GetDataContext<MessageViewModel>();
        if (msg is null)
        {
            return;
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return;
        }

        string serverUrl = msg.ServerUrl ?? mainVm.Chat.CurrentServerUrl;
        string username = msg.Model.Author.Id;
        UserProfileDto? profile = await mainVm.ConnectionService.GetUserProfileAsync(serverUrl, username);
        if (profile is null)
        {
            return;
        }

        ProfileWindow dialog = new ProfileWindow(profile);

        if (TopLevel.GetTopLevel(this) is Window parent)
        {
            await dialog.ShowDialog(parent);
        }
    }

    private async void OnDownloadClicked(object? sender, RoutedEventArgs e)
    {
        MessageViewModel? msg = this.GetDataContext<MessageViewModel>();
        if (msg is null)
        {
            return;
        }

        if (msg.AttachmentUrl is null)
        {
            return;
        }

        TopLevel? topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return;
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return;
        }

        string? tempPath = await mainVm.ConnectionService.DownloadAttachmentAsync(msg.ServerUrl ?? "", msg.AttachmentUrl, msg.AttachmentFileName ?? "download");

        if (tempPath is null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = msg.AttachmentFileName ?? "download",
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
}
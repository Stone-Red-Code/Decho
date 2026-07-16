using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Decho.ViewModels;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using System.Text.RegularExpressions;

namespace Decho.Views;

public partial class MessageItemView : UserControl
{
    private CancellationTokenSource? _loadCts;
    private string? _loadedMessageId;

    public MessageItemView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private static readonly Regex MentionRegex = new(@"@(\w+)", RegexOptions.Compiled);

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (DataContext is MessageViewModel newMsg && newMsg.Model.Id == _loadedMessageId)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _loadedMessageId = null;

        if (DataContext is MessageViewModel msg)
        {
            _loadedMessageId = msg.Model.Id;
            BuildMessageInlines(msg.Content);
        }
    }

    private void BuildMessageInlines(string content)
    {
        TextBlock? tb = MessageContent;
        if (tb is null)
        {
            return;
        }

        tb.Inlines?.Clear();

        int lastIndex = 0;
        foreach (Match match in MentionRegex.Matches(content))
        {
            if (match.Index > lastIndex)
            {
                tb.Inlines?.Add(new Run(content[lastIndex..match.Index]));
            }

            tb.Inlines?.Add(new Run(match.Value)
            {
                Foreground = new SolidColorBrush(Color.Parse("#FEE75C")),
                FontWeight = FontWeight.Bold,
            });

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < content.Length)
        {
            tb.Inlines?.Add(new Run(content[lastIndex..]));
        }
    }

    private async void OnAttachmentImageLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (image.DataContext is not AttachmentDto att)
        {
            return;
        }

        if (att.Kind != AttachmentKind.Image)
        {
            return;
        }

        if (DataContext is not MessageViewModel msg)
        {
            return;
        }

        CancellationTokenSource? cts = _loadCts;
        if (cts is null)
        {
            return;
        }

        if (msg.ImageCache.TryGetValue(att.Url, out Bitmap? cached))
        {
            image.Source = cached;
            return;
        }

        try
        {
            MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
            if (mainVm is null)
            {
                return;
            }

            byte[]? bytes = await mainVm.ConnectionService.DownloadImageBytesAsync(
                msg.ServerUrl ?? "", att.Url);

            cts.Token.ThrowIfCancellationRequested();
            if (bytes is null || bytes.Length == 0)
            {
                return;
            }

            using MemoryStream stream = new MemoryStream(bytes);
            Bitmap bitmap = new Bitmap(stream);
            cts.Token.ThrowIfCancellationRequested();
            msg.ImageCache[att.Url] = bitmap;
            image.Source = bitmap;
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

    private async void OnAttachmentDownloadClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.DataContext is not AttachmentDto att)
        {
            return;
        }

        if (DataContext is not MessageViewModel msg)
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

        string? tempPath = await mainVm.ConnectionService.DownloadAttachmentAsync(msg.ServerUrl ?? "", att.Url, att.FileName);

        if (tempPath is null)
        {
            return;
        }

        IStorageFile? file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = att.FileName,
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
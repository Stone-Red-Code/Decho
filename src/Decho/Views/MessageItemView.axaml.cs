using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Decho.Models;
using Decho.ViewModels;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using Romzetron.Avalonia;

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Decho.Views;

public partial class MessageItemView : UserControl
{
    private const int MaxPreviewDecodeWidth = 800;
    private const double MaxImageDisplayWidth = 400;
    private const double MaxImageDisplayHeight = 300;

    private static readonly Regex MentionRegex = new(@"@(\w+)", RegexOptions.Compiled);
    private static readonly Regex ChannelRegex = new(@"(?<!\w)#([\w-]+)", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly List<Image> _attachmentImages = [];
    private readonly HashSet<Image> _loadingImages = [];
    private CancellationTokenSource? _loadCts;
    private string? _loadedMessageId;

    private MenuItem? _replyMenuItem;
    private MenuItem? _mentionMenuItem;
    private MenuItem? _profileMenuItem;
    private MenuItem? _moderateMenuItem;
    private Separator? _actionsSeparator;
    private MenuItem? _copyTextMenuItem;
    private MenuItem? _copyIdMenuItem;
    private Separator? _deleteSeparator;
    private MenuItem? _deleteMenuItem;

    public MessageItemView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // silently ignore
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (DataContext is MessageViewModel newMsg && newMsg.Model.Id == _loadedMessageId)
        {
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        _loadedMessageId = null;

        foreach (Image image in _attachmentImages)
        {
            image.Source = null;
        }
        _attachmentImages.Clear();
        _loadingImages.Clear();

        if (DataContext is MessageViewModel msg)
        {
            _loadedMessageId = msg.Model.Id;
            BuildMessageInlines(msg.DisplayContent);

            TextBlock? replyQuote = ReplyQuote;
            if (replyQuote is not null)
            {
                replyQuote.Text = msg.ReplyTo is { } reply
                    ? FormatReplyQuote(reply)
                    : string.Empty;
            }
        }
        else
        {
            MessageContent?.ClearSelection();
            MessageContent?.Inlines?.Clear();
            MessageContent?.Text = null;
            if (ReplyQuote is { } replyQuote)
            {
                replyQuote.Text = string.Empty;
            }
        }
    }

    private static string FormatReplyQuote(ReplyRefDto reply)
    {
        string content = MessageViewModel.FormatContent(reply.Content, reply.SenderUsername).Replace('\n', ' ');
        return $"└ {reply.SenderUsername}: {content[..Math.Min(100, content.Length)]}";
    }

    private static Size CalculateDisplaySize(double pixelWidth, double pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return new Size(MaxImageDisplayWidth, MaxImageDisplayHeight);
        }

        double scale = Math.Min(1.0, Math.Min(MaxImageDisplayWidth / pixelWidth, MaxImageDisplayHeight / pixelHeight));
        return new Size(Math.Round(pixelWidth * scale), Math.Round(pixelHeight * scale));
    }

    private static void ApplyImageDimensions(Image image, MessageViewModel msg, AttachmentDto att, Bitmap? bitmap = null)
    {
        if (!msg.AttachmentSizes.TryGetValue(att.Url, out Size displaySize))
        {
            if (bitmap is not null)
            {
                displaySize = CalculateDisplaySize(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
                msg.AttachmentSizes[att.Url] = displaySize;
            }
            else
            {
                image.Width = double.NaN;
                image.Height = double.NaN;
                return;
            }
        }

        image.Width = displaySize.Width;
        image.Height = displaySize.Height;
    }

    private async Task<Bitmap?> GetOrDownloadImageAsync(MessageViewModel msg, AttachmentDto att, CancellationToken ct = default)
    {
        if (msg.TryGetCachedImage(att.Url, out Bitmap? cached) && cached is not null)
        {
            return cached;
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return null;
        }

        byte[]? bytes = await mainVm.ConnectionService.DownloadImageBytesAsync(
            ResolveServerUrl(), msg.Model.ChannelName, att.Url);

        if (bytes is null || bytes.Length == 0 || ct.IsCancellationRequested)
        {
            return null;
        }

        using MemoryStream stream = new MemoryStream(bytes);
        SixLabors.ImageSharp.ImageInfo? info = SixLabors.ImageSharp.Image.Identify(stream);
        stream.Position = 0;

        Bitmap bitmap = info is not null && info.Width > MaxPreviewDecodeWidth
            ? Bitmap.DecodeToWidth(stream, MaxPreviewDecodeWidth)
            : new Bitmap(stream);

        if (ct.IsCancellationRequested)
        {
            bitmap.Dispose();
            return null;
        }

        msg.AttachmentSizes[att.Url] = CalculateDisplaySize(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
        msg.StoreCachedImage(att.Url, bitmap);
        return bitmap;
    }

    private async Task<Bitmap?> DownloadFullImageAsync(MessageViewModel msg, AttachmentDto att)
    {
        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return null;
        }

        byte[]? bytes = await mainVm.ConnectionService.DownloadImageBytesAsync(
            ResolveServerUrl(), msg.Model.ChannelName, att.Url);

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        using MemoryStream stream = new MemoryStream(bytes);
        return new Bitmap(stream);
    }

    private async Task OpenProfileAsync(string username, string serverUrl)
    {
        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return;
        }

        await mainVm.ShowProfileAsync(serverUrl, username);
    }

    private void BuildMessageInlines(string content)
    {
        SelectableTextBlock? tb = MessageContent;
        if (tb is null)
        {
            return;
        }

        tb.ClearSelection();
        tb.Inlines?.Clear();

        if (!content.Contains('@') && !content.Contains('#') && !content.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            tb.Text = content;
            return;
        }

        tb.Text = null;

        IEnumerable<Match> mentionMatches = MentionRegex.Matches(content).Cast<Match>();
        IEnumerable<Match> channelMatches = ChannelRegex.Matches(content).Cast<Match>();
        IEnumerable<Match> urlMatches = UrlRegex.Matches(content).Cast<Match>();
        List<Match> allMatches = mentionMatches.Concat(channelMatches).Concat(urlMatches)
            .OrderBy(m => m.Index)
            .ToList();

        if (allMatches.Count == 0)
        {
            tb.Text = content;
            return;
        }

        int lastIndex = 0;
        foreach (Match match in allMatches)
        {
            if (match.Index > lastIndex)
            {
                tb.Inlines?.Add(new Run(content[lastIndex..match.Index]));
            }

            bool isMention = match.Value[0] == '@';
            InlineUIContainer container = new InlineUIContainer
            {
                BaselineAlignment = BaselineAlignment.Center,
            };
            TextBlock label = new TextBlock
            {
                Text = match.Value,
                FontWeight = FontWeight.Bold,
                Cursor = HandCursor,
                Padding = new Thickness(0),
                LineHeight = double.NaN,
            };

            if (isMention)
            {
                string username = match.Groups[1].Value;
                label.Margin = new Thickness(0, 0, 0, -2);
                label.Foreground = ColorPalette.Yellow05;
                string serverUrl = ResolveServerUrl();
                label.PointerPressed += (_, args) =>
                {
                    if (IsLeftClick(args))
                    {
                        _ = OpenProfileAsync(username, serverUrl);
                    }
                };
            }
            else if (match.Value[0] == '#')
            {
                string channelName = match.Groups[1].Value;
                label.Margin = new Thickness(0, 0, 0, -2);
                label.Foreground = ColorPalette.Blue05;
                label.PointerPressed += (_, args) => OnChannelPointerPressed(channelName, args);
            }
            else
            {
                string url = match.Value;
                label.Foreground = ColorPalette.Blue05;
                label.TextDecorations = TextDecorations.Underline;
                label.PointerPressed += (_, args) =>
                {
                    if (IsLeftClick(args))
                    {
                        OpenUrl(url);
                    }
                };
            }

            container.Child = label;
            tb.Inlines?.Add(container);

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < content.Length)
        {
            tb.Inlines?.Add(new Run(content[lastIndex..]));
        }
    }

    private string ResolveServerUrl()
    {
        if (DataContext is MessageViewModel msg)
        {
            MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
            return msg.ServerUrl ?? mainVm?.Chat.CurrentServerUrl ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool IsLeftClick(PointerPressedEventArgs e)
    {
        return e.GetCurrentPoint(null).Properties.IsLeftButtonPressed;
    }

    private void OnMessageGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed && MessageGrid.ContextMenu is null)
        {
            EnsureContextMenu();
        }
    }

    private void EnsureContextMenu()
    {
        if (MessageGrid.ContextMenu is not null)
        {
            return;
        }

        _replyMenuItem = new MenuItem { Header = "Reply" };
        _replyMenuItem.Click += OnReplyMenuClick;

        _mentionMenuItem = new MenuItem { Header = "Mention" };
        _mentionMenuItem.Click += OnMentionMenuClick;

        _profileMenuItem = new MenuItem { Header = "View profile" };
        _profileMenuItem.Click += OnProfileMenuClick;

        _moderateMenuItem = new MenuItem { Header = "Moderate" };

        _actionsSeparator = new Separator();

        _copyTextMenuItem = new MenuItem { Header = "Copy text" };
        _copyTextMenuItem.Click += OnCopyTextMenuClick;

        _copyIdMenuItem = new MenuItem { Header = "Copy message ID" };
        _copyIdMenuItem.Click += OnCopyIdMenuClick;

        _deleteSeparator = new Separator();

        _deleteMenuItem = new MenuItem { Header = "Delete message" };
        _deleteMenuItem.Click += OnDeleteMenuClick;

        ContextMenu menu = new ContextMenu();
        menu.Opening += OnContextMenuOpening;
        menu.Items.Add(_replyMenuItem);
        menu.Items.Add(_mentionMenuItem);
        menu.Items.Add(_profileMenuItem);
        menu.Items.Add(_moderateMenuItem);
        menu.Items.Add(_actionsSeparator);
        menu.Items.Add(_copyTextMenuItem);
        menu.Items.Add(_copyIdMenuItem);
        menu.Items.Add(_deleteSeparator);
        menu.Items.Add(_deleteMenuItem);

        MessageGrid.ContextMenu = menu;
    }

    private void OnContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (DataContext is not MessageViewModel msg || mainVm is null)
        {
            e.Cancel = true;
            return;
        }

        bool isRemote = !msg.Model.IsLocal;
        bool canCompose = isRemote && mainVm.Chat.Composer.IsEditable;
        bool isConnected = mainVm.Sidebar.GetServer(ResolveServerUrl())?.IsConnected ?? false;

        _replyMenuItem!.IsVisible = canCompose;
        _mentionMenuItem!.IsVisible = canCompose;
        _profileMenuItem!.IsVisible = isRemote && isConnected;

        if (isRemote)
        {
            ModerationMenu.Populate(_moderateMenuItem!, mainVm, ResolveServerUrl(), msg.AuthorUsername);
        }
        else
        {
            _moderateMenuItem!.IsVisible = false;
        }

        _actionsSeparator!.IsVisible = canCompose || _profileMenuItem.IsVisible;

        _copyTextMenuItem!.IsVisible = msg.ShowContent;
        _copyIdMenuItem!.IsVisible = isRemote;

        _deleteMenuItem!.IsVisible = mainVm.CanDeleteMessage(msg);
        _deleteSeparator!.IsVisible = _deleteMenuItem.IsVisible;
    }

    private void OnReplyMenuClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (DataContext is MessageViewModel msg && mainVm is not null)
        {
            mainVm.Chat.Composer.ReplyTarget = msg;
            mainVm.Chat.Composer.RequestFocus();
        }
    }

    private void OnMentionMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageViewModel msg)
        {
            this.GetMainWindowViewModel()?.Chat.Composer.InsertMention(msg.AuthorUsername);
        }
    }

    private void OnProfileMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageViewModel msg)
        {
            _ = OpenProfileAsync(msg.AuthorUsername, ResolveServerUrl());
        }
    }

    private async void OnCopyTextMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageViewModel msg)
        {
            string selected = MessageContent.SelectedText;
            await CopyToClipboardAsync(string.IsNullOrEmpty(selected) ? msg.DisplayContent : selected);
        }
    }

    private async void OnCopyIdMenuClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MessageViewModel msg)
        {
            await CopyToClipboardAsync(Guid.TryParse(msg.Model.Id, out Guid id) ? id.ToString() : msg.Model.Id);
        }
    }

    private async void OnDeleteMenuClick(object? sender, RoutedEventArgs e)
    {
        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (DataContext is MessageViewModel msg && mainVm is not null)
        {
            await mainVm.DeleteMessageAsync(msg);
        }
    }

    private async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Copy to clipboard failed: {ex.Message}");
        }
    }

    private void OnChannelPointerPressed(string channelName, PointerPressedEventArgs e)
    {
        if (!IsLeftClick(e) || DataContext is not MessageViewModel msg)
        {
            return;
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return;
        }

        _ = mainVm.OpenChannelAsync(ResolveServerUrl(), channelName);
    }

    private void OnAttachmentImageLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is Image image)
        {
            PrepareAttachmentImage(image);
        }
    }

    private void OnAttachmentImageDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is Image image)
        {
            PrepareAttachmentImage(image);
        }
    }

    private void PrepareAttachmentImage(Image image)
    {
        if (image.DataContext is not AttachmentDto att || att.Kind != AttachmentKind.Image || DataContext is not MessageViewModel msg)
        {
            image.Source = null;
            image.Width = double.NaN;
            image.Height = double.NaN;
            return;
        }

        if (!_attachmentImages.Contains(image))
        {
            _attachmentImages.Add(image);
        }

        ApplyImageDimensions(image, msg, att);

        if (msg.TryGetCachedImage(att.Url, out Bitmap? cached) && cached is not null)
        {
            ApplyImageDimensions(image, msg, att, cached);
            image.Source = cached;
            return;
        }

        image.Source = null;
        _ = LoadAttachmentImageAsync(image);
    }

    private async Task LoadAttachmentImageAsync(Image image)
    {
        if (image.Source is not null || !_loadingImages.Add(image))
        {
            return;
        }

        try
        {
            if (image.DataContext is not AttachmentDto att || att.Kind != AttachmentKind.Image)
            {
                return;
            }

            if (DataContext is not MessageViewModel msg || _loadCts is null)
            {
                return;
            }

            CancellationToken ct = _loadCts.Token;
            Bitmap? bitmap = await GetOrDownloadImageAsync(msg, att, ct);
            ct.ThrowIfCancellationRequested();

            if (bitmap is not null && ReferenceEquals(DataContext, msg) && ReferenceEquals(image.DataContext, att))
            {
                ApplyImageDimensions(image, msg, att, bitmap);
                image.Source = bitmap;
            }
        }
        catch
        {
            // Image failed to load filename is shown as fallback
        }
        finally
        {
            _loadingImages.Remove(image);
        }
    }

    private async void OnAuthorNamePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!IsLeftClick(e) || DataContext is not MessageViewModel msg)
        {
            return;
        }

        string serverUrl = ResolveServerUrl();
        await OpenProfileAsync(msg.Model.Author.Id, serverUrl);
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

        string? tempPath = await mainVm.ConnectionService.DownloadAttachmentAsync(
            msg.ServerUrl ?? "", msg.Model.ChannelName, att.Url, att.FileName);

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

    private async void OnAttachmentImagePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!IsLeftClick(e) || sender is not Image image)
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

        Bitmap? fullBitmap = await DownloadFullImageAsync(msg, att);
        Bitmap? displayBitmap = fullBitmap ?? await GetOrDownloadImageAsync(msg, att);
        if (displayBitmap is null)
        {
            fullBitmap?.Dispose();
            return;
        }

        Window? parent = this.GetParentWindow();
        if (parent is null)
        {
            fullBitmap?.Dispose();
            return;
        }

        string serverUrl = ResolveServerUrl();
        string channelName = msg.Model.ChannelName;
        Func<Task<string?>> downloadAsync = async () =>
        {
            MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
            if (mainVm is null)
            {
                return null;
            }
            return await mainVm.ConnectionService.DownloadAttachmentAsync(
                serverUrl, channelName, att.Url, att.FileName);
        };

        try
        {
            ImageViewerWindow viewer = new ImageViewerWindow(displayBitmap, att.FileName, downloadAsync);
            await viewer.ShowDialog(parent);
        }
        finally
        {
            fullBitmap?.Dispose();
        }
    }

    private void OnReplyPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!IsLeftClick(e) || DataContext is not MessageViewModel msg)
            return;

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
            return;

        mainVm.Chat.Composer.ReplyTarget = msg;
    }

    private void OnReplyQuotePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!IsLeftClick(e) || DataContext is not MessageViewModel msg || msg.ReplyTo is null)
            return;

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
            return;

        string serverUrl = ResolveServerUrl();
        ServerViewModel? serverVm = mainVm.Sidebar.GetServer(serverUrl);
        if (serverVm is null)
            return;

        ChannelViewModel? channel = serverVm.Channels.FirstOrDefault(c =>
            string.Equals(c.Name, msg.Model.ChannelName, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
            return;

        string targetId = msg.ReplyTo.MessageId.ToString("N");
        bool needsSwitch = !string.Equals(channel.Name, mainVm.Chat.CurrentChannelName, StringComparison.OrdinalIgnoreCase);

        if (needsSwitch)
        {
            mainVm.Sidebar.SelectedChannel = channel;
        }

        JumpToReply(targetId, needsSwitch);
    }

    private async void JumpToReply(string targetId, bool needsSwitch)
    {
        if (needsSwitch)
        {
            await Task.Delay(100);
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null) return;

        bool found = mainVm.Chat.Messages.Any(m => m.Model.Id == targetId);

        if (!found)
        {
            string serverUrl = ResolveServerUrl();
            string channelName = mainVm.Chat.CurrentChannelName;
            if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(channelName))
                return;

            try
            {
                List<MessageModel> allMessages = await mainVm.ChannelService.GetHistoryAsync(
                    serverUrl, channelName, 200, 0);

                if (allMessages.Any(m => m.Id == targetId))
                {
                    ChannelViewModel? channelVm = mainVm.Sidebar.GetServer(serverUrl)?.Channels
                        .FirstOrDefault(c => string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));

                    channelVm?.InsertMessages(allMessages);

                    await Task.Delay(50);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch reply target: {ex.Message}");
                return;
            }
        }

        mainVm.Chat.ScrollToMessageRequested?.Invoke(targetId);
    }
}

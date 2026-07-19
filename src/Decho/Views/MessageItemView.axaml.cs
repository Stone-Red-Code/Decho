using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Decho.ViewModels;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using Romzetron.Avalonia;

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Decho.Views;

public partial class MessageItemView : UserControl
{
    private static readonly Regex MentionRegex = new(@"@(\w+)", RegexOptions.Compiled);
    private static readonly Regex ChannelRegex = new(@"(?<!\w)#([\w-]+)", RegexOptions.Compiled);
    private static readonly Regex UrlRegex = new(@"https?://[^\s]+", RegexOptions.Compiled);
    private CancellationTokenSource? _loadCts;
    private string? _loadedMessageId;

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

        if (DataContext is MessageViewModel msg)
        {
            _loadedMessageId = msg.Model.Id;
            BuildMessageInlines(msg.DisplayContent);

            TextBlock? replyQuote = ReplyQuote;
            if (replyQuote is not null && msg.ReplyTo is { } reply)
            {
                replyQuote.Text = $"\u2514 {reply.SenderUsername}: {reply.Content}";
            }
        }
    }

    private async Task<Bitmap?> GetOrDownloadImageAsync(MessageViewModel msg, AttachmentDto att)
    {
        if (msg.ImageCache.TryGetValue(att.Url, out Bitmap? cached))
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

        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        using MemoryStream stream = new MemoryStream(bytes);
        Bitmap bitmap = new Bitmap(stream);
        msg.ImageCache[att.Url] = bitmap;
        return bitmap;
    }

    private async Task OpenProfileAsync(string username, string serverUrl)
    {
        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        Window? parent = this.GetParentWindow();
        if (mainVm is null || parent is null)
        {
            return;
        }

        UserProfileDto? profile = await mainVm.UserService.GetUserProfileAsync(serverUrl, username);
        if (profile is null)
        {
            return;
        }

        await new ProfileWindow(profile).ShowDialog(parent);
    }

    private void BuildMessageInlines(string content)
    {
        TextBlock? tb = MessageContent;
        if (tb is null)
        {
            return;
        }

        tb.Inlines?.Clear();

        IEnumerable<Match> mentionMatches = MentionRegex.Matches(content).Cast<Match>();
        IEnumerable<Match> channelMatches = ChannelRegex.Matches(content).Cast<Match>();
        IEnumerable<Match> urlMatches = UrlRegex.Matches(content).Cast<Match>();
        List<Match> allMatches = mentionMatches.Concat(channelMatches).Concat(urlMatches)
            .OrderBy(m => m.Index)
            .ToList();

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
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(0),
                LineHeight = double.NaN,
            };

            if (isMention)
            {
                string username = match.Groups[1].Value;
                label.Margin = new Thickness(0, 0, 0, -2);
                label.Foreground = ColorPalette.Yellow05;
                string serverUrl = ResolveServerUrl();
                label.PointerPressed += (_, args) => _ = OpenProfileAsync(username, serverUrl);
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
                label.PointerPressed += (_, args) => OpenUrl(url);
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

    private void OnChannelPointerPressed(string channelName, PointerPressedEventArgs e)
    {
        if (DataContext is not MessageViewModel msg)
        {
            return;
        }

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
        {
            return;
        }

        string serverUrl = ResolveServerUrl();
        ServerViewModel? serverVm = mainVm.Sidebar.GetServer(serverUrl);
        if (serverVm is null)
        {
            return;
        }

        ChannelViewModel? channel = serverVm.Channels.FirstOrDefault(c =>
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase));
        if (channel is null)
        {
            return;
        }

        mainVm.Sidebar.SelectedChannel = channel;
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

        if (_loadCts is null)
        {
            return;
        }

        try
        {
            Bitmap? bitmap = await GetOrDownloadImageAsync(msg, att);
            _loadCts.Token.ThrowIfCancellationRequested();
            if (bitmap is not null)
            {
                image.Source = bitmap;
            }
        }
        catch
        {
            // Image failed to load filename is shown as fallback
        }
    }

    private async void OnAuthorNamePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is not MessageViewModel msg)
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

        Bitmap? bitmap = await GetOrDownloadImageAsync(msg, att);
        if (bitmap is null)
        {
            return;
        }

        Window? parent = this.GetParentWindow();
        if (parent is null)
        {
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

        ImageViewerWindow viewer = new ImageViewerWindow(bitmap, att.FileName, downloadAsync);
        await viewer.ShowDialog(parent);
    }

    private void OnReplyPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is not MessageViewModel msg)
            return;

        MainWindowViewModel? mainVm = this.GetMainWindowViewModel();
        if (mainVm is null)
            return;

        mainVm.Chat.Composer.ReplyTarget = msg;
    }

    private void OnReplyQuotePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is not MessageViewModel msg || msg.ReplyTo is null)
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

        // Switch to the channel containing the original message
        if (!string.Equals(channel.Name, mainVm.Chat.CurrentChannelName, StringComparison.OrdinalIgnoreCase))
            mainVm.Sidebar.SelectedChannel = channel;
    }
}
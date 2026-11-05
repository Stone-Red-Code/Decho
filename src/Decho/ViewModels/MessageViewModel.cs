using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using Decho.Models;

using EchoHub.Core.DTOs;

namespace Decho.ViewModels;

public sealed class MessageViewModel(MessageModel model) : ViewModelBase
{
    private const int MaxTotalCachedImages = 25;
    private static readonly TimeSpan GroupingWindow = TimeSpan.FromMinutes(7);
    private static readonly Lock CacheLock = new();
    private static readonly LinkedList<(MessageViewModel Owner, string Url, Bitmap Bitmap)> LruImages = new();

    private IBrush? _authorColor;
    private bool _authorColorResolved;

    public MessageModel Model { get; } = model;

    public string AuthorName => Model.Author.DisplayName;

    public string AuthorUsername => Model.Author.Id;

    public IBrush? AuthorColor
    {
        get
        {
            if (_authorColorResolved)
            {
                return _authorColor;
            }

            _authorColorResolved = true;
            string? color = Model.Author.NicknameColor;
            if (string.IsNullOrEmpty(color))
            {
                _authorColor = Brushes.White;
                return _authorColor;
            }

            try
            {
                _authorColor = new SolidColorBrush(Color.Parse(color));
            }
            catch
            {
                _authorColor = null;
            }

            return _authorColor;
        }
    }

    public string Content => Model.Content;

    public bool IsAction => Model.Content.StartsWith("/me ", StringComparison.Ordinal);

    public string ActionText => IsAction ? Model.Content[4..] : Model.Content;

    public string DisplayContent => IsAction ? $"* {AuthorName} {ActionText}" : Model.Content;

    public ReplyRefDto? ReplyTo => Model.ReplyTo;

    public bool HasReply => ReplyTo is not null;

    public string? ServerUrl => Model.ServerUrl;

    public Dictionary<string, Bitmap> ImageCache { get; } = [];

    public Dictionary<string, Size> AttachmentSizes { get; } = [];

    public IReadOnlyList<AttachmentDto> Attachments => Model.Attachments;

    public bool HasAttachments => Attachments.Count > 0;

    public IReadOnlyList<EmbedDto> Embeds => Model.Embeds;

    public bool HasEmbeds => Embeds.Count > 0;

    public bool ShowContent => !string.IsNullOrEmpty(Content);

    public bool ShowUnreadMarker
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsGroupContinuation));
        }
    }

    public bool FollowsPreviousMessage
    {
        get;
        set
        {
            this.RaiseAndSetIfChanged(ref field, value);
            this.RaisePropertyChanged(nameof(IsGroupContinuation));
        }
    }

    /// <summary>True when the author's name and picture are hidden because the message above already shows them.</summary>
    public bool IsGroupContinuation => FollowsPreviousMessage && !ShowUnreadMarker;

    public string ShortTimeText => Model.SentAt.ToLocalTime().ToString("t");

    public bool CanFollow(MessageViewModel previous)
    {
        DateTimeOffset sentAt = Model.SentAt.ToLocalTime();
        DateTimeOffset previousSentAt = previous.Model.SentAt.ToLocalTime();

        return !HasReply
            && Model.IsLocal == previous.Model.IsLocal
            && string.Equals(AuthorUsername, previous.AuthorUsername, StringComparison.OrdinalIgnoreCase)
            && sentAt.Date == previousSentAt.Date
            && (sentAt - previousSentAt).Duration() <= GroupingWindow;
    }

    public bool IsHighlighted
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public string TimeText
    {
        get
        {
            DateTimeOffset now = DateTimeOffset.Now.ToLocalTime();

            if (Model.SentAt.ToLocalTime().Date == now.Date)
            {
                return Model.SentAt.ToLocalTime().ToString("t");
            }

            return Model.SentAt.ToLocalTime().ToString("g");
        }
    }

    public bool TryGetCachedImage(string url, out Bitmap? bitmap)
    {
        lock (CacheLock)
        {
            if (ImageCache.TryGetValue(url, out bitmap))
            {
                TouchLruLocked(this, url);
                return true;
            }

            bitmap = null;
            return false;
        }
    }

    public void StoreCachedImage(string url, Bitmap bitmap)
    {
        List<Bitmap>? toDispose = null;
        lock (CacheLock)
        {
            if (ImageCache.TryGetValue(url, out Bitmap? existing))
            {
                if (!ReferenceEquals(existing, bitmap))
                {
                    RemoveLruLocked(this, url);
                    toDispose ??= [];
                    toDispose.Add(existing);
                }
                else
                {
                    TouchLruLocked(this, url);
                    return;
                }
            }

            ImageCache[url] = bitmap;
            LruImages.AddLast((this, url, bitmap));

            while (LruImages.Count > MaxTotalCachedImages)
            {
                (MessageViewModel owner, string evictedUrl, Bitmap evictedBitmap) = LruImages.First!.Value;
                LruImages.RemoveFirst();
                owner.ImageCache.Remove(evictedUrl);
                toDispose ??= [];
                toDispose.Add(evictedBitmap);
            }
        }

        if (toDispose is not null)
        {
            foreach (Bitmap bmp in toDispose)
            {
                bmp.Dispose();
            }
        }
    }

    public void DisposeImages()
    {
        List<Bitmap>? toDispose = null;
        lock (CacheLock)
        {
            if (ImageCache.Count == 0)
            {
                return;
            }

            toDispose = new List<Bitmap>(ImageCache.Values);
            ImageCache.Clear();

            LinkedListNode<(MessageViewModel Owner, string Url, Bitmap Bitmap)>? node = LruImages.First;
            while (node is not null)
            {
                LinkedListNode<(MessageViewModel Owner, string Url, Bitmap Bitmap)>? next = node.Next;
                if (ReferenceEquals(node.Value.Owner, this))
                {
                    LruImages.Remove(node);
                }
                node = next;
            }
        }

        foreach (Bitmap bitmap in toDispose)
        {
            bitmap.Dispose();
        }
    }

    private static void TouchLruLocked(MessageViewModel owner, string url)
    {
        LinkedListNode<(MessageViewModel Owner, string Url, Bitmap Bitmap)>? node = LruImages.First;
        while (node is not null)
        {
            if (ReferenceEquals(node.Value.Owner, owner) && node.Value.Url == url)
            {
                if (node != LruImages.Last)
                {
                    (MessageViewModel Owner, string Url, Bitmap Bitmap) val = node.Value;
                    LruImages.Remove(node);
                    LruImages.AddLast(val);
                }
                return;
            }
            node = node.Next;
        }
    }

    private static void RemoveLruLocked(MessageViewModel owner, string url)
    {
        LinkedListNode<(MessageViewModel Owner, string Url, Bitmap Bitmap)>? node = LruImages.First;
        while (node is not null)
        {
            if (ReferenceEquals(node.Value.Owner, owner) && node.Value.Url == url)
            {
                LruImages.Remove(node);
                return;
            }
            node = node.Next;
        }
    }
}

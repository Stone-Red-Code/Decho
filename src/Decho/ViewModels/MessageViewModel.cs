using Avalonia.Media;
using Avalonia.Media.Imaging;

using Decho.Models;

using EchoHub.Core.DTOs;

namespace Decho.ViewModels;

public sealed class MessageViewModel(MessageModel model) : ViewModelBase
{
    private static readonly TimeSpan GroupingWindow = TimeSpan.FromMinutes(7);

    public MessageModel Model { get; } = model;

    public string AuthorName => Model.Author.DisplayName;

    public string AuthorUsername => Model.Author.Id;

    public IBrush? AuthorColor
    {
        get
        {
            string? color = Model.Author.NicknameColor;
            if (string.IsNullOrEmpty(color))
            {
                return Brushes.White;
            }

            try
            {
                return new SolidColorBrush(Color.Parse(color));
            }
            catch
            {
                return null;
            }
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
}
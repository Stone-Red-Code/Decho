using Avalonia.Media;

using Decho.Models;

using EchoHub.Core.Models;

namespace Decho.ViewModels;

public sealed class MessageViewModel(MessageModel model) : ViewModelBase
{
    public MessageModel Model { get; } = model;

    public string AuthorName => Model.Author.DisplayName;

    public IBrush? AuthorColor
    {
        get
        {
            string? color = Model.Author.NicknameColor;
            if (string.IsNullOrEmpty(color))
            {
                return null;
            }

            try { return new SolidColorBrush(Avalonia.Media.Color.Parse(color)); }
            catch { return null; }
        }
    }

    public string Content => Model.Content;

    public string? ServerUrl => Model.ServerUrl;

    public MessageType Type => Model.Type;

    public bool HasAttachment => Model.AttachmentUrl is not null;

    public string? AttachmentFileName => Model.AttachmentFileName;

    public string? AttachmentUrl => Model.AttachmentUrl;

    public bool IsImage => Type == MessageType.Image;

    public bool ShowContent => !IsImage && !IsFile;

    public bool IsAudio => Type == MessageType.Audio;

    public bool IsFile => Type == MessageType.File;

    public string TimeText
    {
        get
        {
            DateTimeOffset now = DateTimeOffset.Now;

            if (Model.SentAt.Date == now.Date)
            {
                return Model.SentAt.ToLocalTime().ToString("t");
            }

            return Model.SentAt.ToLocalTime().ToString("g");
        }
    }
}
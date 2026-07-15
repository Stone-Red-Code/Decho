using EchoHub.Core.Models;

namespace Decho.Models;

public sealed class MessageModel
{
    public MessageModel(
        string id,
        UserModel author,
        DateTimeOffset sentAt,
        string content,
        string channelName,
        string? serverUrl = null,
        MessageType type = MessageType.Text,
        string? attachmentUrl = null,
        string? attachmentFileName = null,
        long? attachmentFileSize = null)
    {
        Id = id;
        Author = author;
        SentAt = sentAt;
        Content = content;
        ChannelName = channelName;
        ServerUrl = serverUrl;
        Type = type;
        AttachmentUrl = attachmentUrl;
        AttachmentFileName = attachmentFileName;
        AttachmentFileSize = attachmentFileSize;
    }

    public string Id { get; }

    public UserModel Author { get; }

    public DateTimeOffset SentAt { get; }

    public string Content { get; }

    public string ChannelName { get; }

    public string? ServerUrl { get; }

    public MessageType Type { get; }

    public string? AttachmentUrl { get; }

    public string? AttachmentFileName { get; }

    public long? AttachmentFileSize { get; }
}

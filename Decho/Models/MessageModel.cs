using EchoHub.Core.Models;

namespace Decho.Models;

public sealed class MessageModel(string id, UserModel author, DateTimeOffset sentAt, string content, string channelName, string? serverUrl = null, MessageType type = MessageType.Text, string? attachmentUrl = null, string? attachmentFileName = null, long? attachmentFileSize = null)
{
    public string Id { get; } = id;

    public UserModel Author { get; } = author;

    public DateTimeOffset SentAt { get; } = sentAt;

    public string Content { get; } = content;

    public string ChannelName { get; } = channelName;

    public string? ServerUrl { get; } = serverUrl;

    public MessageType Type { get; } = type;

    public string? AttachmentUrl { get; } = attachmentUrl;

    public string? AttachmentFileName { get; } = attachmentFileName;

    public long? AttachmentFileSize { get; } = attachmentFileSize;
}
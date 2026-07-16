using EchoHub.Core.DTOs;

namespace Decho.Models;

public sealed class MessageModel(string id, UserModel author, DateTimeOffset sentAt, string content, string channelName, string? serverUrl = null, List<AttachmentDto>? attachments = null)
{
    public string Id { get; } = id;

    public UserModel Author { get; } = author;

    public DateTimeOffset SentAt { get; } = sentAt;

    public string Content { get; } = content;

    public string ChannelName { get; } = channelName;

    public string? ServerUrl { get; } = serverUrl;

    public List<AttachmentDto> Attachments { get; } = attachments ?? [];
}
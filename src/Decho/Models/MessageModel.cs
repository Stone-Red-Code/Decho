using EchoHub.Core.DTOs;

namespace Decho.Models;

public sealed class MessageModel(
    string id,
    UserModel author,
    DateTimeOffset sentAt,
    string content,
    string channelName,
    string? serverUrl = null,
    List<AttachmentDto>? attachments = null,
    ReplyRefDto? replyTo = null,
    List<EmbedDto>? embeds = null)
{
    public string Id { get; } = id;

    public UserModel Author { get; } = author;

    public DateTimeOffset SentAt { get; } = sentAt;

    public string Content { get; } = content;

    public string ChannelName { get; } = channelName;

    public string? ServerUrl { get; } = serverUrl;

    public List<AttachmentDto> Attachments { get; } = attachments ?? [];

    public ReplyRefDto? ReplyTo { get; } = replyTo;

    public List<EmbedDto> Embeds { get; } = embeds ?? [];

    /// <summary>Only exists on this client, such as a system notice, so it has no place in server history.</summary>
    public bool IsLocal { get; init; }
}
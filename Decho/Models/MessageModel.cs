using System;

namespace Decho.Models;

public sealed class MessageModel
{
    public MessageModel(string id, UserModel author, DateTimeOffset sentAt, string content)
    {
        Id = id;
        Author = author;
        SentAt = sentAt;
        Content = content;
    }

    public string Id { get; }

    public UserModel Author { get; }

    public DateTimeOffset SentAt { get; }

    public string Content { get; }
}

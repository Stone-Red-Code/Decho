using Decho.Models;

using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Decho.ViewModels;

public sealed class ChannelViewModel : ViewModelBase
{
    private MessageViewModel? _unreadMarker;

    public ChannelViewModel(ChannelModel model)
    {
        Model = model;
        Messages = new ObservableCollection<MessageViewModel>(
            model.Messages.Select(message => new MessageViewModel(message)));
        Messages.CollectionChanged += OnMessagesChanged;
        UpdateMessageGrouping();
    }

    public ChannelModel Model { get; }

    public string Name => Model.Name;

    public string? Topic
    {
        get => Model.Topic;
        set
        {
            if (Model.Topic != value)
            {
                Model.Topic = value;
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(HasTopic));
            }
        }
    }

    public bool HasTopic => !string.IsNullOrWhiteSpace(Topic);

    public bool IsPublic => Model.IsPublic;

    public bool IsProtected => Model.IsProtected;

    public bool IsNotProtected => !Model.IsProtected;

    public bool IsSystem => Model.IsSystem;

    public bool IsNotSystem => !Model.IsSystem;

    public bool ShowLockIcon => Model.IsProtected && IsLocked;

    public bool ShowUnlockIcon => Model.IsProtected && !IsLocked;

    public bool ShowServerIcon => Model.IsSystem && !Model.IsProtected;

    public bool ShowHashtagIcon => !Model.IsProtected && !Model.IsSystem;

    public bool IsLocked
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public int UnreadCount
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasUnread));
            this.RaisePropertyChanged(nameof(BadgeText));
            this.RaisePropertyChanged(nameof(BadgeColor));
        }
    }

    public bool HasUnread => UnreadCount > 0;

    public int MentionCount
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(HasMentions));
            this.RaisePropertyChanged(nameof(BadgeText));
            this.RaisePropertyChanged(nameof(BadgeColor));
        }
    }

    public bool HasMentions => MentionCount > 0;

    public string BadgeColor => HasMentions ? "#E53935" : "#78909C";

    public string BadgeText => HasMentions ? MentionCount.ToString() : UnreadCount.ToString();

    public ObservableCollection<MessageViewModel> Messages { get; }

    public string? LastReadMessageId { get; set; }

    /// <summary>Puts the marker above the first message from someone else since the last read one.</summary>
    public MessageViewModel? PlaceUnreadMarker(string? currentUsername)
    {
        ClearUnreadMarker();

        int lastRead = LastReadMessageId is null
            ? -1
            : Messages.ToList().FindIndex(m => m.Model.Id == LastReadMessageId);
        if (lastRead < 0)
        {
            return null;
        }

        _unreadMarker = Messages.Skip(lastRead + 1).FirstOrDefault(m =>
            !m.Model.IsLocal && !string.Equals(m.AuthorUsername, currentUsername, StringComparison.OrdinalIgnoreCase));
        _unreadMarker?.ShowUnreadMarker = true;
        return _unreadMarker;
    }

    public void ClearUnreadMarker()
    {
        _unreadMarker?.ShowUnreadMarker = false;
        _unreadMarker = null;
    }

    /// <summary>Returns true when the read position moved.</summary>
    public bool MarkAllRead()
    {
        string? newest = Messages.LastOrDefault(m => !m.Model.IsLocal)?.Model.Id;
        if (newest is null || newest == LastReadMessageId)
        {
            return false;
        }

        LastReadMessageId = newest;
        return true;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateMessageGrouping();
    }

    private void UpdateMessageGrouping()
    {
        MessageViewModel? previous = null;
        foreach (MessageViewModel message in Messages)
        {
            message.FollowsPreviousMessage = previous is not null && message.CanFollow(previous);
            previous = message;
        }
    }

    public void IncrementUnread(bool isMention = false)
    {
        UnreadCount++;
        if (isMention)
        {
            MentionCount++;
        }
    }

    public void ClearUnread()
    {
        UnreadCount = 0;
        MentionCount = 0;
    }

    public void ClearMessages()
    {
        _unreadMarker = null;
        foreach (MessageViewModel message in Messages)
        {
            message.DisposeImages();
        }
        Model.Messages.Clear();
        Messages.Clear();
    }

    public void AddMessage(MessageModel message)
    {
        Model.Messages.Add(message);
        Messages.Add(new MessageViewModel(message));
    }

    public void RemoveMessage(string messageId)
    {
        MessageViewModel? message = Messages.FirstOrDefault(m => m.Model.Id == messageId);
        if (message is not null)
        {
            int index = Messages.IndexOf(message);

            // Keep the read position and marker on messages that still exist.
            if (messageId == LastReadMessageId)
            {
                LastReadMessageId = Messages.Take(index).LastOrDefault(m => !m.Model.IsLocal)?.Model.Id;
            }

            if (ReferenceEquals(message, _unreadMarker))
            {
                ClearUnreadMarker();
                _unreadMarker = Messages.Skip(index + 1).FirstOrDefault(m => !m.Model.IsLocal);
                _unreadMarker?.ShowUnreadMarker = true;
            }

            message.DisposeImages();
            _ = Model.Messages.Remove(message.Model);
            _ = Messages.Remove(message);
        }
    }

    /// <summary>
    /// Drops all but the newest server messages so that the channel opens quickly next time.
    /// Scrolling up loads the rest again. Only call this on a channel that isn't being shown.
    /// </summary>
    public void TrimHistory(int keepCount)
    {
        int firstKept = Messages.Count;
        int kept = 0;
        while (firstKept > 0 && kept < keepCount)
        {
            firstKept--;
            if (!Messages[firstKept].Model.IsLocal)
            {
                kept++;
            }
        }

        if (firstKept == 0)
        {
            return;
        }

        // Regrouping after every removal would make this quadratic.
        Messages.CollectionChanged -= OnMessagesChanged;
        for (int i = firstKept - 1; i >= 0; i--)
        {
            Messages[i].DisposeImages();
            _ = Model.Messages.Remove(Messages[i].Model);
            Messages.RemoveAt(i);
        }
        Messages.CollectionChanged += OnMessagesChanged;

        UpdateMessageGrouping();
    }

    public void InsertMessages(IList<MessageModel> olderMessages)
    {
        HashSet<string> existingIds = Messages.Select(m => m.Model.Id).ToHashSet();
        List<MessageModel> fresh = olderMessages.Where(m => !existingIds.Contains(m.Id)).Reverse().ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        Messages.CollectionChanged -= OnMessagesChanged;
        foreach (MessageModel msg in fresh)
        {
            Model.Messages.Insert(0, msg);
            Messages.Insert(0, new MessageViewModel(msg));
        }
        Messages.CollectionChanged += OnMessagesChanged;

        UpdateMessageGrouping();
    }
}
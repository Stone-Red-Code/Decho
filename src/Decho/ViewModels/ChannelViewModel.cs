using Decho.Models;

using System.Collections.ObjectModel;

namespace Decho.ViewModels;

public sealed class ChannelViewModel(ChannelModel model) : ViewModelBase
{
    public ChannelModel Model { get; } = model;

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

    public ObservableCollection<MessageViewModel> Messages { get; } = new ObservableCollection<MessageViewModel>(
            model.Messages.Select(message => new MessageViewModel(message)));

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
        Model.Messages.Clear();
        Messages.Clear();
    }

    public void AddMessage(MessageModel message)
    {
        Model.Messages.Add(message);
        Messages.Add(new MessageViewModel(message));
    }

    public void InsertMessages(IList<MessageModel> olderMessages)
    {
        HashSet<string> existingIds = Messages.Select(m => m.Model.Id).ToHashSet();
        List<MessageModel> fresh = olderMessages.Where(m => !existingIds.Contains(m.Id)).ToList();
        if (fresh.Count == 0)
        {
            return;
        }

        foreach (MessageModel msg in fresh)
        {
            Model.Messages.Insert(0, msg);
            Messages.Insert(0, new MessageViewModel(msg));
        }
    }
}
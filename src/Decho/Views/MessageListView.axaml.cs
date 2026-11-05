using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

using Decho.ViewModels;

using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Decho.Views;

public partial class MessageListView : UserControl
{
    private const double RevealMargin = 40;

    private ChatViewModel? _chat;
    private ObservableCollection<MessageViewModel>? _observedMessages;
    private bool _hasPendingScroll;
    private bool _wasAtBottom = true;
    private bool _loadingMore;
    private double _loadingMoreExtent;
    private string? _loadingMoreAnchorId;

    private MessageViewModel? _highlightedMessage;

    private CancellationTokenSource? _highlightCts;

    public MessageListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public void ScrollToMessage(string messageId)
    {
        if (_chat is null) return;

        int index = IndexOfMessage(messageId);
        if (index < 0) return;

        MessageViewModel target = _chat.Messages[index];
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        ItemsControl? items = this.FindControl<ItemsControl>("MessageItemsControl");
        if (scroll is null || items is null) return;

        _wasAtBottom = false;

        ClearHighlight();
        target.IsHighlighted = true;
        _highlightedMessage = target;
        _highlightCts = new CancellationTokenSource();
        CancellationToken ct = _highlightCts.Token;

        Dispatcher.UIThread.Post(() =>
        {
            int currentIndex = IndexOfMessage(messageId);
            if (currentIndex < 0) return;

            if (items.ItemsPanelRoot is VirtualizingMessagePanel panel)
            {
                panel.ScrollToIndex(scroll, currentIndex);
            }
            else
            {
                items.ScrollIntoView(currentIndex);
            }
        }, DispatcherPriority.Render);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(2000, ct);
                Dispatcher.UIThread.Post(() =>
                {
                    target.IsHighlighted = false;
                    if (_highlightedMessage == target)
                        _highlightedMessage = null;
                });
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    /// <summary>Scrolls to a message without highlighting it, unless it is already in view.</summary>
    public void RevealMessage(string messageId)
    {
        int index = IndexOfMessage(messageId);
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        ItemsControl? items = this.FindControl<ItemsControl>("MessageItemsControl");
        if (index < 0 || scroll is null || items is null) return;

        // Set now so that layout from the newly added messages doesn't scroll back to the end.
        _wasAtBottom = false;

        Dispatcher.UIThread.Post(() =>
        {
            int currentIndex = IndexOfMessage(messageId);
            if (currentIndex < 0) return;

            if (items.ItemsPanelRoot is VirtualizingMessagePanel panel)
            {
                double itemY = panel.GetItemY(currentIndex);
                bool isInView = itemY >= scroll.Offset.Y && itemY + RevealMargin <= scroll.Offset.Y + scroll.Viewport.Height;
                if (!isInView)
                {
                    panel.ScrollToIndex(scroll, currentIndex, RevealMargin);
                }
            }
        }, DispatcherPriority.Background);
    }

    private int IndexOfMessage(string messageId)
    {
        if (_chat is null) return -1;

        ObservableCollection<MessageViewModel> messages = _chat.Messages;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Model.Id == messageId)
                return i;
        }

        return -1;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_chat is not null)
        {
            _chat.PropertyChanged -= OnViewModelPropertyChanged;
            _chat.LoadMoreRequested -= OnLoadMoreRequested;
            _chat.ScrollToMessageRequested -= ScrollToMessage;
            _chat.RevealMessageRequested -= RevealMessage;
        }

        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        scroll?.ScrollChanged -= OnScrollChanged;

        _chat = DataContext as ChatViewModel;
        ObserveMessages(_chat?.Messages);
        if (_chat is not null)
        {
            _chat.PropertyChanged += OnViewModelPropertyChanged;
            _chat.LoadMoreRequested += OnLoadMoreRequested;
            _chat.ScrollToMessageRequested += ScrollToMessage;
            _chat.RevealMessageRequested += RevealMessage;
            scroll?.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnLoadMoreRequested()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null) return;

        _loadingMore = true;
        _loadingMoreExtent = scroll.Extent.Height;
        _loadingMoreAnchorId = _chat?.Messages.FirstOrDefault()?.Model.Id;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        ScrollViewer scrollViewer = (ScrollViewer)sender!;

        if (e.OffsetDelta.Y < -0.5)
        {
            _wasAtBottom = false;
        }
        else if (e.OffsetDelta.Y > 0.5)
        {
            _wasAtBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 10;
        }
        else if (e.ExtentDelta.Y > 0 && _wasAtBottom)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_wasAtBottom)
                {
                    scrollViewer.ScrollToEnd();
                }
            }, DispatcherPriority.Background);
        }

        if (!_loadingMore && _chat is not null && !_chat.IsLoadingMore
            && scrollViewer.Offset.Y <= 0
            && scrollViewer.Extent.Height > scrollViewer.Viewport.Height)
        {
            _chat.LoadMoreRequested?.Invoke();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.Messages))
        {
            ObserveMessages(_chat!.Messages);
            ScrollToBottom();
        }
        else if (e.PropertyName == nameof(ChatViewModel.IsLoadingMore) && !_chat!.IsLoadingMore && _loadingMore)
        {
            RestoreScrollPosition();
        }
    }

    private void ObserveMessages(ObservableCollection<MessageViewModel>? messages)
    {
        _observedMessages?.CollectionChanged -= OnMessagesChanged;
        _observedMessages = messages;
        _observedMessages?.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewStartingIndex > 0)
        {
            TryAutoScroll();
        }
    }

    private void RestoreScrollPosition()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        ItemsControl? items = this.FindControl<ItemsControl>("MessageItemsControl");
        if (scroll is null) return;

        string? anchorId = _loadingMoreAnchorId;
        double oldExtent = _loadingMoreExtent;

        Dispatcher.UIThread.Post(() =>
        {
            if (scroll.Offset.Y < 10)
            {
                if (anchorId is not null && items?.ItemsPanelRoot is VirtualizingMessagePanel panel)
                {
                    int anchorIndex = IndexOfMessage(anchorId);
                    if (anchorIndex > 0)
                    {
                        panel.ScrollToIndex(scroll, anchorIndex);
                    }
                }
                else
                {
                    double delta = scroll.Extent.Height - oldExtent;
                    if (delta > 0)
                    {
                        scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + delta);
                    }
                }
            }

            _loadingMore = false;
            _loadingMoreAnchorId = null;
        }, DispatcherPriority.Background);
    }

    private void TryAutoScroll()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        ItemsControl? items = this.FindControl<ItemsControl>("MessageItemsControl");
        if (scroll is null)
        {
            return;
        }

        if (_wasAtBottom && !_hasPendingScroll)
        {
            _hasPendingScroll = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_wasAtBottom)
                {
                    if (items?.ItemsPanelRoot is VirtualizingMessagePanel panel)
                    {
                        panel.ScrollToEnd(scroll);
                    }
                    else
                    {
                        scroll.ScrollToEnd();
                    }
                }
                _hasPendingScroll = false;
            }, DispatcherPriority.Background);
        }
    }

    private void ScrollToBottom()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        ItemsControl? items = this.FindControl<ItemsControl>("MessageItemsControl");
        if (scroll is null)
        {
            return;
        }

        _wasAtBottom = true;
        Dispatcher.UIThread.Post(() =>
        {
            if (items?.ItemsPanelRoot is VirtualizingMessagePanel panel)
            {
                panel.ScrollToEnd(scroll);
            }
            else
            {
                scroll.ScrollToEnd();
            }
        }, DispatcherPriority.Background);
    }

    private void ClearHighlight()
    {
        _highlightCts?.Cancel();
        _highlightCts = null;
        if (_highlightedMessage is not null)
        {
            _highlightedMessage.IsHighlighted = false;
            _highlightedMessage = null;
        }
    }
}

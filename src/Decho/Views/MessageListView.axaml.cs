using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using Decho.ViewModels;

using System.Collections.Specialized;

namespace Decho.Views;

public partial class MessageListView : UserControl
{
    private ChatViewModel? _chat;
    private bool _hasPendingScroll;
    private bool _wasAtBottom;
    private bool _loadingMore;
    private double _loadingMoreExtent;

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

        MessageViewModel? target = _chat.Messages.FirstOrDefault(m => m.Model.Id == messageId);
        if (target is null) return;

        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null) return;

        ClearHighlight();
        target.IsHighlighted = true;
        _highlightedMessage = target;
        _highlightCts = new CancellationTokenSource();
        CancellationToken ct = _highlightCts.Token;

        Dispatcher.UIThread.Post(() =>
        {
            double? targetY = FindItemY(scroll, target);
            if (targetY is not null)
                scroll.Offset = new Vector(scroll.Offset.X, targetY.Value);
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

    private static double? FindItemY(Visual parent, MessageViewModel target)
    {
        foreach (Visual child in parent.GetVisualChildren())
        {
            if (child.DataContext == target)
                return child.Bounds.Y;

            double? result = FindItemY(child, target);
            if (result.HasValue)
                return result;
        }

        return null;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_chat is not null)
        {
            _chat.Messages.CollectionChanged -= OnMessagesChanged;
            _chat.PropertyChanged -= OnViewModelPropertyChanged;
            _chat.LoadMoreRequested -= OnLoadMoreRequested;
            _chat.ScrollToMessageRequested -= ScrollToMessage;
        }

        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        scroll?.ScrollChanged -= OnScrollChanged;

        _chat = DataContext as ChatViewModel;
        if (_chat is not null)
        {
            _chat.Messages.CollectionChanged += OnMessagesChanged;
            _chat.PropertyChanged += OnViewModelPropertyChanged;
            _chat.LoadMoreRequested += OnLoadMoreRequested;
            _chat.ScrollToMessageRequested += ScrollToMessage;
            scroll?.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnLoadMoreRequested()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null) return;

        _loadingMore = true;
        _loadingMoreExtent = scroll.Extent.Height;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y > 0 && _wasAtBottom)
        {
            ScrollViewer scroll = (ScrollViewer)sender!;
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
        }

        ScrollViewer scrollViewer = (ScrollViewer)sender!;

        if (!_loadingMore && _chat is not null && !_chat.IsLoadingMore
            && scrollViewer.Offset.Y <= 0
            && scrollViewer.Extent.Height > scrollViewer.Viewport.Height)
        {
            _chat.LoadMoreRequested?.Invoke();
        }

        _wasAtBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 30;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.Messages))
        {
            _chat!.Messages.CollectionChanged += OnMessagesChanged;
            ScrollToBottom();
        }
        else if (e.PropertyName == nameof(ChatViewModel.IsLoadingMore) && !_chat!.IsLoadingMore && _loadingMore)
        {
            RestoreScrollPosition();
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            TryAutoScroll();
        }
    }

    private void RestoreScrollPosition()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            double delta = scroll.Extent.Height - _loadingMoreExtent;
            scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + delta);
            _loadingMore = false;
        }, DispatcherPriority.Background);
    }

    private void TryAutoScroll()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null)
        {
            return;
        }

        _wasAtBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - 30;
        if (_wasAtBottom && !_hasPendingScroll)
        {
            _hasPendingScroll = true;
            Dispatcher.UIThread.Post(() =>
            {
                scroll.ScrollToEnd();
                _hasPendingScroll = false;
            }, DispatcherPriority.Background);
        }
    }

    private void ScrollToBottom()
    {
        ScrollViewer? scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null)
        {
            return;
        }

        _wasAtBottom = true;
        Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
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
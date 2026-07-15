using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using Decho.ViewModels;

namespace Decho.Views;

public partial class MessageListView : UserControl
{
    private ChatViewModel? _chat;
    private bool _hasPendingScroll;
    private bool _wasAtBottom;

    public MessageListView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs args)
    {
        if (_chat is not null)
        {
            _chat.Messages.CollectionChanged -= OnMessagesChanged;
            _chat.PropertyChanged -= OnViewModelPropertyChanged;
        }

        var scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is not null)
            scroll.ScrollChanged -= OnScrollChanged;

        _chat = DataContext as ChatViewModel;
        if (_chat is not null)
        {
            _chat.Messages.CollectionChanged += OnMessagesChanged;
            _chat.PropertyChanged += OnViewModelPropertyChanged;
            if (scroll is not null)
                scroll.ScrollChanged += OnScrollChanged;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y > 0 && _wasAtBottom)
        {
            var scroll = (ScrollViewer)sender!;
            Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
        }

        var scrollViewer = (ScrollViewer)sender!;
        _wasAtBottom = scrollViewer.Offset.Y + scrollViewer.Viewport.Height >= scrollViewer.Extent.Height - 30;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChatViewModel.Messages))
        {
            _chat!.Messages.CollectionChanged += OnMessagesChanged;
            ScrollToBottom();
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            TryAutoScroll();
        }
    }

    private void TryAutoScroll()
    {
        var scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null) return;

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
        var scroll = this.FindControl<ScrollViewer>("MessageScrollViewer");
        if (scroll is null) return;

        _wasAtBottom = true;
        Dispatcher.UIThread.Post(scroll.ScrollToEnd, DispatcherPriority.Background);
    }
}

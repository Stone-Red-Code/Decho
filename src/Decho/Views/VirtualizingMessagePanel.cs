using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Decho.ViewModels;

using EchoHub.Core.DTOs;
using EchoHub.Core.Models;

using System.Collections.Specialized;

namespace Decho.Views;

/// <summary>
/// Virtualizing vertical stack panel that remembers the measured height of every message
/// and uses deterministic content-based estimates for unmeasured messages so the scrollbar
/// never jumps up and down as items enter or leave the viewport.
/// </summary>
public sealed class VirtualizingMessagePanel : VirtualizingPanel
{
    private const double RealizeBufferPx = 300;
    private const int MaxRecyclePoolSize = 40;

    private readonly List<double> _heights = [];
    private readonly List<bool> _isMeasured = [];
    private readonly Dictionary<int, Control> _realized = [];
    private readonly Dictionary<Control, int> _containerToIndex = [];
    private readonly Stack<Control> _recyclePool = [];

    private ScrollViewer? _scrollViewer;
    private IScrollAnchorProvider? _scrollAnchorProvider;
    private Rect _effectiveViewport;
    private double _lastAvailableWidth;
    private double _pendingPrependOffset;
    private int _pinnedIndex = -1;

    public VirtualizingMessagePanel()
    {
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    public double GetItemY(int index)
    {
        int end = Math.Clamp(index, 0, _heights.Count);
        double y = 0;
        for (int i = 0; i < end; i++)
        {
            y += _heights[i];
        }
        return y;
    }

    public double GetItemHeight(int index)
    {
        return index >= 0 && index < _heights.Count ? _heights[index] : 0;
    }

    public double GetTotalHeight()
    {
        double total = 0;
        for (int i = 0; i < _heights.Count; i++)
        {
            total += _heights[i];
        }
        return total;
    }

    public void ScrollToIndex(ScrollViewer scroll, int index, double topMargin = 0)
    {
        IReadOnlyList<object?> items = Items;
        if (index < 0 || index >= items.Count)
        {
            return;
        }

        double width = _lastAvailableWidth > 0 ? _lastAvailableWidth : Bounds.Width;
        EnsureHeightsSynced(items, width);

        _pinnedIndex = index;
        _pendingPrependOffset = 0;

        double targetY = Math.Max(0, GetItemY(index) - topMargin);
        scroll.Offset = new Vector(scroll.Offset.X, targetY);
        InvalidateMeasure();
        scroll.UpdateLayout();

        targetY = Math.Max(0, GetItemY(index) - topMargin);
        scroll.Offset = new Vector(scroll.Offset.X, targetY);
        InvalidateMeasure();
        scroll.UpdateLayout();

        _pinnedIndex = -1;
    }

    public void ScrollToEnd(ScrollViewer scroll)
    {
        IReadOnlyList<object?> items = Items;
        if (items.Count == 0)
        {
            return;
        }

        double width = _lastAvailableWidth > 0 ? _lastAvailableWidth : Bounds.Width;
        EnsureHeightsSynced(items, width);

        _pinnedIndex = items.Count - 1;
        _pendingPrependOffset = 0;

        scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, GetTotalHeight()));
        InvalidateMeasure();
        scroll.UpdateLayout();

        scroll.ScrollToEnd();
        InvalidateMeasure();
        scroll.UpdateLayout();
        scroll.ScrollToEnd();

        _pinnedIndex = -1;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _scrollViewer = this.FindAncestorOfType<ScrollViewer>();
        _scrollAnchorProvider = this.FindAncestorOfType<IScrollAnchorProvider>();
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged += OnScrollViewerScrollChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_scrollViewer is not null)
        {
            _scrollViewer.ScrollChanged -= OnScrollViewerScrollChanged;
            _scrollViewer = null;
        }
        _scrollAnchorProvider = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnItemsControlChanged(ItemsControl? oldValue)
    {
        base.OnItemsControlChanged(oldValue);
        RecycleAll();
        _heights.Clear();
        _isMeasured.Clear();
        _pendingPrependOffset = 0;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);

        double width = _lastAvailableWidth > 0 ? _lastAvailableWidth : Bounds.Width;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null && e.NewStartingIndex >= 0:
            {
                int insertIndex = e.NewStartingIndex;
                int count = e.NewItems.Count;
                bool isPrepend = insertIndex == 0 && _realized.Count > 0;

                foreach (int oldIndex in _realized.Keys.Where(k => k >= insertIndex).OrderByDescending(k => k).ToList())
                {
                    Control container = _realized[oldIndex];
                    int newIndex = oldIndex + count;
                    _realized.Remove(oldIndex);
                    _realized[newIndex] = container;
                    _containerToIndex[container] = newIndex;
                    ItemContainerGenerator?.ItemContainerIndexChanged(container, oldIndex, newIndex);
                }

                for (int i = 0; i < count; i++)
                {
                    double estimated = EstimateItemHeight(e.NewItems[i], width);
                    _heights.Insert(insertIndex + i, estimated);
                    _isMeasured.Insert(insertIndex + i, false);
                    if (isPrepend)
                    {
                        _pendingPrependOffset += estimated;
                    }
                }
                break;
            }

            case NotifyCollectionChangedAction.Remove when e.OldItems is not null && e.OldStartingIndex >= 0:
            {
                int removeIndex = e.OldStartingIndex;
                int count = e.OldItems.Count;

                for (int i = removeIndex; i < removeIndex + count; i++)
                {
                    if (_realized.TryGetValue(i, out Control? container))
                    {
                        RecycleContainer(i, container);
                    }
                }

                foreach (int oldIndex in _realized.Keys.Where(k => k >= removeIndex + count).OrderBy(k => k).ToList())
                {
                    Control container = _realized[oldIndex];
                    int newIndex = oldIndex - count;
                    _realized.Remove(oldIndex);
                    _realized[newIndex] = container;
                    _containerToIndex[container] = newIndex;
                    ItemContainerGenerator?.ItemContainerIndexChanged(container, oldIndex, newIndex);
                }

                if (removeIndex + count <= _heights.Count)
                {
                    _heights.RemoveRange(removeIndex, count);
                    _isMeasured.RemoveRange(removeIndex, count);
                }
                else
                {
                    RebuildHeights(items, width);
                }
                break;
            }

            case NotifyCollectionChangedAction.Replace when e.NewItems is not null && e.OldStartingIndex >= 0:
            {
                int startIndex = e.OldStartingIndex;
                int count = e.NewItems.Count;
                for (int i = 0; i < count; i++)
                {
                    int idx = startIndex + i;
                    if (_realized.TryGetValue(idx, out Control? container))
                    {
                        RecycleContainer(idx, container);
                    }
                    if (idx < _heights.Count)
                    {
                        _heights[idx] = EstimateItemHeight(e.NewItems[i], width);
                        _isMeasured[idx] = false;
                    }
                }
                break;
            }

            default:
                RecycleAll();
                RebuildHeights(items, width);
                _pendingPrependOffset = 0;
                break;
        }

        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        IReadOnlyList<object?> items = Items;
        int itemCount = items.Count;
        double width = double.IsInfinity(availableSize.Width) ? Bounds.Width : availableSize.Width;

        if (itemCount == 0)
        {
            RecycleAll();
            _heights.Clear();
            _isMeasured.Clear();
            _pendingPrependOffset = 0;
            return new Size(width, 0);
        }

        EnsureHeightsSynced(items, width);

        double rawViewportTop = _scrollViewer?.Offset.Y ?? Math.Max(0, _effectiveViewport.Y);
        double viewportTop = rawViewportTop + _pendingPrependOffset;
        double viewportHeight = _scrollViewer is { Viewport.Height: > 0 and < double.PositiveInfinity }
            ? _scrollViewer.Viewport.Height
            : (_effectiveViewport.Height is > 0 and < double.PositiveInfinity ? _effectiveViewport.Height : 800);

        double realizeTop = Math.Max(0, viewportTop - RealizeBufferPx);
        double realizeBottom = viewportTop + viewportHeight + RealizeBufferPx;

        int firstIndex = -1;
        int lastIndex = -1;
        double y = 0;

        for (int i = 0; i < itemCount; i++)
        {
            double h = _heights[i];
            double itemBottom = y + h;

            if (firstIndex < 0 && itemBottom >= realizeTop)
            {
                firstIndex = i;
            }

            if (firstIndex >= 0 && y <= realizeBottom)
            {
                lastIndex = i;
            }
            else if (firstIndex >= 0 && y > realizeBottom)
            {
                break;
            }

            y = itemBottom;
        }

        if (firstIndex < 0)
        {
            firstIndex = Math.Max(0, itemCount - 1);
            lastIndex = firstIndex;
        }

        if (_pinnedIndex >= 0 && _pinnedIndex < itemCount)
        {
            firstIndex = Math.Min(firstIndex, _pinnedIndex);
            lastIndex = Math.Max(lastIndex, _pinnedIndex);
        }

        foreach (int index in _realized.Keys.ToList())
        {
            if (index < firstIndex || index > lastIndex)
            {
                RecycleContainer(index, _realized[index]);
            }
        }

        Size childConstraint = new Size(width, double.PositiveInfinity);
        for (int i = firstIndex; i <= lastIndex; i++)
        {
            if (!_realized.TryGetValue(i, out Control? container))
            {
                container = GetOrCreateContainer(items[i], i);
                _realized[i] = container;
                _containerToIndex[container] = i;
                _scrollAnchorProvider?.RegisterAnchorCandidate(container);
            }

            container.Measure(childConstraint);
            double measuredHeight = container.DesiredSize.Height;
            _heights[i] = measuredHeight;
            _isMeasured[i] = true;
        }

        return new Size(width, GetTotalHeight());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach ((int index, Control container) in _realized)
        {
            if (index < 0 || index >= _heights.Count)
            {
                continue;
            }

            double y = GetItemY(index);
            double h = _heights[index];
            container.Arrange(new Rect(0, y, finalSize.Width, h));
        }

        _pendingPrependOffset = 0;
        return finalSize;
    }

    protected override Control? ScrollIntoView(int index)
    {
        IReadOnlyList<object?> items = Items;
        if (index < 0 || index >= items.Count)
        {
            return null;
        }

        if (_scrollViewer is not null)
        {
            ScrollToIndex(_scrollViewer, index);
        }

        return ContainerFromIndex(index);
    }

    protected override Control? ContainerFromIndex(int index)
    {
        return _realized.TryGetValue(index, out Control? container) ? container : null;
    }

    protected override int IndexFromContainer(Control container)
    {
        return _containerToIndex.TryGetValue(container, out int index) ? index : -1;
    }

    protected override IEnumerable<Control> GetRealizedContainers()
    {
        return _realized.Values;
    }

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        return null;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        _effectiveViewport = e.EffectiveViewport;
        InvalidateMeasure();
    }

    private void OnScrollViewerScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y != 0 || e.ViewportDelta.Y != 0)
        {
            InvalidateMeasure();
        }
    }

    private void EnsureHeightsSynced(IReadOnlyList<object?> items, double width)
    {
        bool widthChanged = width > 0 && Math.Abs(width - _lastAvailableWidth) > 1.0;
        if (width > 0)
        {
            _lastAvailableWidth = width;
        }

        if (_heights.Count != items.Count)
        {
            RebuildHeights(items, width);
            return;
        }

        if (widthChanged)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (!_isMeasured[i])
                {
                    _heights[i] = EstimateItemHeight(items[i], width);
                }
            }
        }
    }

    private void RebuildHeights(IReadOnlyList<object?> items, double width)
    {
        _heights.Clear();
        _isMeasured.Clear();
        _heights.Capacity = Math.Max(_heights.Capacity, items.Count);
        _isMeasured.Capacity = Math.Max(_isMeasured.Capacity, items.Count);

        for (int i = 0; i < items.Count; i++)
        {
            _heights.Add(EstimateItemHeight(items[i], width));
            _isMeasured.Add(false);
        }
    }

    private static double EstimateItemHeight(object? item, double availableWidth)
    {
        if (item is not MessageViewModel vm)
        {
            return 40;
        }

        double height = vm.IsGroupContinuation ? 0 : 30;
        if (vm.ShowUnreadMarker)
        {
            height += 24;
        }
        if (vm.HasReply)
        {
            height += 16;
        }
        if (vm.ShowContent)
        {
            double textWidth = Math.Max(availableWidth - 74, 150);
            int charsPerLine = Math.Max((int)(textWidth / 7.2), 20);
            int lines = 0;
            foreach (ReadOnlySpan<char> line in vm.DisplayContent.AsSpan().EnumerateLines())
            {
                lines += Math.Max(1, (line.Length + charsPerLine - 1) / charsPerLine);
            }
            height += Math.Max(1, lines) * 19;
        }

        foreach (AttachmentDto att in vm.Attachments)
        {
            if (att.Kind == AttachmentKind.Image)
            {
                if (vm.AttachmentSizes.TryGetValue(att.Url, out Size savedSize))
                {
                    height += savedSize.Height + 40;
                }
                else
                {
                    height += 240;
                }
            }
            else
            {
                height += 40;
            }
        }

        height += vm.Embeds.Count * 76;
        return Math.Max(height, 19);
    }

    private Control GetOrCreateContainer(object? item, int index)
    {
        if (ItemContainerGenerator!.NeedsContainer(item, index, out object? recycleKey))
        {
            if (_recyclePool.Count > 0)
            {
                Control recycled = _recyclePool.Pop();
                recycled.SetCurrentValue(IsVisibleProperty, true);
                ItemContainerGenerator.PrepareItemContainer(recycled, item, index);
                ItemContainerGenerator.ItemContainerPrepared(recycled, item, index);
                return recycled;
            }

            Control created = ItemContainerGenerator.CreateContainer(item, index, recycleKey);
            ItemContainerGenerator.PrepareItemContainer(created, item, index);
            AddInternalChild(created);
            ItemContainerGenerator.ItemContainerPrepared(created, item, index);
            return created;
        }

        Control own = (Control)item!;
        ItemContainerGenerator.PrepareItemContainer(own, own, index);
        AddInternalChild(own);
        ItemContainerGenerator.ItemContainerPrepared(own, own, index);
        return own;
    }

    private void RecycleContainer(int index, Control container)
    {
        _scrollAnchorProvider?.UnregisterAnchorCandidate(container);
        _realized.Remove(index);
        _containerToIndex.Remove(container);
        ItemContainerGenerator?.ClearItemContainer(container);
        container.SetCurrentValue(IsVisibleProperty, false);

        if (_recyclePool.Count < MaxRecyclePoolSize)
        {
            _recyclePool.Push(container);
        }
        else
        {
            RemoveInternalChild(container);
        }
    }

    private void RecycleAll()
    {
        foreach ((int index, Control container) in _realized.ToList())
        {
            RecycleContainer(index, container);
        }
    }
}

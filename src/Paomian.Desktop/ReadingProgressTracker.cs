using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Collections.Specialized;
using Paomian.Core;

namespace Paomian.Desktop;

/// <summary>只观察正文视口。节流保存，并在导航和关闭时由窗口主动补存。</summary>
internal sealed class ReadingProgressTracker : IDisposable
{
    private readonly ListBox _list;
    private readonly MainViewModel _model;
    private readonly DispatcherTimer _timer;
    private ReadingAnchor? _restoreAnchor;
    private DispatcherOperation? _restoreOperation;
    private (double Extent, double Viewport, double ItemHeight, double Offset, double Top)? _restoredLayout;
    private bool _ready, _paused, _restoring;

    public ReadingProgressTracker(ListBox list, MainViewModel model)
    {
        _list = list; _model = model;
        _timer = new(DispatcherPriority.Background, list.Dispatcher) { Interval = TimeSpan.FromMilliseconds(750) };
        _timer.Tick += (_, _) => Flush();
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
        list.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel), true);
        list.PreviewMouseDown += MouseDown; list.PreviewKeyDown += KeyDown;
        list.LayoutUpdated += LayoutUpdated;
        model.ReadingPosts.CollectionChanged += PostsChanged;
    }

    public void Pause() { Flush(); _paused = true; ClearRestore(); }
    public void Resume() { _paused = false; Schedule(); }
    internal void BeginUserScroll() => ClearRestore();

    public void Reset(ReadingPosition? position)
    {
        _ready = false; _paused = false; _timer.Stop(); ClearRestore();
        if (position is not null)
        {
            var posts = _model.ReadingPosts.Where(p => !p.IsTip);
            var item = posts.FirstOrDefault(p => p.Post.Id == position.PostId)
                ?? posts.OrderBy(p => Math.Abs(p.Index - position.Index)).FirstOrDefault();
            if (item is not null)
            {
                // 原帖已删时退到同页邻近回复顶部，不把原帖的偏移套到其他内容上。
                _restoreAnchor = new(item, item.Post.Id == position.PostId ? -position.Offset : 0);
                Restore();
            }
        }
        _ready = true; Schedule();
    }

    public void Flush()
    {
        _timer.Stop();
        if (!_ready || _paused || _restoring || !_list.IsVisible) return;
        if (ReadingAnchor.Capture(_list, skipTips: true) is { Item: ReadPost item } anchor)
            _model.SaveReadingPosition(item, -anchor.Top);
    }

    private void Schedule() { if (_ready && !_paused && !_timer.IsEnabled) _timer.Start(); }
    private void Scrolled(object sender, ScrollChangedEventArgs args)
    {
        if (ReferenceEquals(args.OriginalSource, ReadingAnchor.Children<ScrollViewer>(_list).FirstOrDefault())) Schedule();
    }
    private void Restore()
    {
        if (_restoreAnchor is null || !_list.IsVisible || _restoring) return;
        _restoring = true;
        try
        {
            _restoreAnchor.Restore(_list);
            if (_list.ItemContainerGenerator.ContainerFromItem(_restoreAnchor.Item) is ListBoxItem item)
            {
                // 字号、正文或图片尺寸已改变时，偏移不能把目标回复整个藏到视口之外。
                var top = Math.Max(_restoreAnchor.Top, -Math.Max(0, item.ActualHeight - 1));
                if (top != _restoreAnchor.Top) { _restoreAnchor = _restoreAnchor with { Top = top }; _restoreAnchor.Restore(_list); }
            }
            _restoredLayout = LayoutSize();
        }
        finally { _restoring = false; }
    }
    private void LayoutUpdated(object? sender, EventArgs args)
    {
        // 缩略图稍后改变高度时，在用户开始操作前维持同一锚点。
        if (_restoreAnchor is null || _restoring || _restoreOperation is { Status: DispatcherOperationStatus.Pending } || !_list.IsVisible) return;
        if (!_list.Items.Contains(_restoreAnchor.Item)) { ClearRestore(); return; }
        // 短页/末页可能无法精确置顶。只有布局尺寸真的变化才再校正，避免 LayoutUpdated 无限循环。
        if (LayoutSize() == _restoredLayout) return;
        if (_list.ItemContainerGenerator.ContainerFromItem(_restoreAnchor.Item) is ListBoxItem container &&
            Math.Abs(container.TransformToAncestor(_list).Transform(new Point()).Y - _restoreAnchor.Top) < .5) return;
        _restoreOperation = _list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, Restore);
    }
    private (double Extent, double Viewport, double ItemHeight, double Offset, double Top)? LayoutSize()
    {
        var viewer = ReadingAnchor.Children<ScrollViewer>(_list).FirstOrDefault();
        var item = _list.ItemContainerGenerator.ContainerFromItem(_restoreAnchor!.Item) as ListBoxItem;
        return viewer is null ? null : (viewer.ExtentHeight, viewer.ViewportHeight,
            item?.ActualHeight ?? 0, viewer.VerticalOffset, item?.TransformToAncestor(_list).Transform(new Point()).Y ?? double.MaxValue);
    }
    private void PostsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset) ClearRestore();
        Schedule();
    }
    private void ClearRestore() { _restoreOperation?.Abort(); _restoreOperation = null; _restoreAnchor = null; _restoredLayout = null; }
    private void Wheel(object sender, MouseWheelEventArgs args) => ClearRestore();
    private void MouseDown(object sender, MouseButtonEventArgs args) => ClearRestore();
    private void KeyDown(object sender, KeyEventArgs args) => ClearRestore();
    public void Dispose()
    {
        _timer.Stop(); ClearRestore();
        _list.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
        _list.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel));
        _list.PreviewMouseDown -= MouseDown; _list.PreviewKeyDown -= KeyDown; _list.LayoutUpdated -= LayoutUpdated;
        _model.ReadingPosts.CollectionChanged -= PostsChanged;
    }
}

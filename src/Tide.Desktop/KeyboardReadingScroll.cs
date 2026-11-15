using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Tide.Desktop;

/// <summary>通过实际负责滚动的面板推进像素偏移，保留列表虚拟化。</summary>
internal sealed class KeyboardReadingScroll
{
    private const double TapDistance = 28;
    private readonly DispatcherTimer _timer;
    private ScrollViewer? _viewer;
    private IScrollInfo? _scroller;
    private Key _key;
    private int _direction;
    private bool _held;
    private long _previous;
    private double _seconds, _distance, _requestedOffset;

    internal KeyboardReadingScroll(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(12) };
        _timer.Tick += Tick;
    }

    internal void Move(ListBox list, ScrollViewer viewer, Key key, int direction)
    {
        // 同一键尚未松开时不会重起滚动；反向从真实视口偏移立即接管。
        if (_held && _key == key && _viewer == viewer) return;
        Stop();
        switch (key)
        {
            case Key.Home: viewer.ScrollToTop(); return;
            case Key.End: viewer.ScrollToBottom(); return;
            case Key.PageUp or Key.PageDown:
                viewer.ScrollToVerticalOffset(Math.Clamp(viewer.VerticalOffset + direction * .92 * viewer.ViewportHeight, 0, viewer.ScrollableHeight));
                return;
        }
        var panel = ReadingAnchor.Children<VirtualizingStackPanel>(viewer).FirstOrDefault(panel => panel.ScrollOwner == viewer);
        _scroller = panel is not null && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel
            ? panel : viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) as IScrollInfo;
        _viewer = viewer; _key = key; _direction = direction; _held = true;
        _seconds = _distance = 0;
        _requestedOffset = viewer.VerticalOffset;
        _previous = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    internal void Release(Key key)
    {
        if (key != _key) return;
        _held = false;
        if (_distance >= TapDistance) Stop();
    }

    internal void Stop()
    {
        _timer.Stop(); _held = false; _viewer = null; _scroller = null; _key = Key.None;
    }

    private void Tick(object? sender, EventArgs e)
    {
        if (_viewer is not { IsVisible: true } viewer) { Stop(); return; }
        var now = Stopwatch.GetTimestamp();
        var elapsed = Math.Min(.033, Stopwatch.GetElapsedTime(_previous, now).TotalSeconds);
        _previous = now;
        _seconds += elapsed;
        var amount = elapsed * (220 + 340 * Math.Min(1, _seconds / .18));
        if (!_held) amount = Math.Min(amount, TapDistance - _distance);
        // 布局有时晚于计时器。累计未布局的偏移，虚拟面板明显修正估算时再从其实际位置接续。
        if (Math.Abs(viewer.VerticalOffset - _requestedOffset) > Math.Max(32, viewer.ViewportHeight / 8))
            _requestedOffset = viewer.VerticalOffset;
        var next = Math.Clamp(_requestedOffset + _direction * amount, 0, viewer.ScrollableHeight);
        if (!_held && (_distance >= TapDistance - .01 || next == _requestedOffset)) { Stop(); return; }
        if (next != _requestedOffset)
        {
            if (_scroller is not null) _scroller.SetVerticalOffset(next);
            else viewer.ScrollToVerticalOffset(next);
            _distance += Math.Abs(next - _requestedOffset);
            _requestedOffset = next;
        }
    }
}

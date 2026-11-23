using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Paomian.Desktop;

/// <summary>通过实际负责滚动的面板推进像素偏移，保留列表虚拟化。</summary>
internal sealed class KeyboardReadingScroll
{
    private const double TapDistance = 28;
    private readonly DispatcherTimer _timer;
    private ScrollViewer? _viewer;
    private ReadingScrollPanel? _panel;
    private Key _key;
    private int _direction;
    private bool _held;
    private long _previous;
    private double _seconds, _distance;

    internal KeyboardReadingScroll(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render, dispatcher) { Interval = TimeSpan.FromMilliseconds(12) };
        _timer.Tick += Tick;
    }

    internal void Move(ScrollViewer viewer, Key key, int direction)
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
        _panel = ReadingAnchor.Children<ReadingScrollPanel>(viewer).FirstOrDefault(panel => panel.ScrollOwner == viewer);
        _viewer = viewer; _key = key; _direction = direction; _held = true;
        _seconds = _distance = 0;
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
        _timer.Stop(); _held = false; _viewer = null; _panel = null; _key = Key.None;
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
        if (_panel is not null) _panel.ScrollBy(_direction * amount);
        else viewer.ScrollToVerticalOffset(viewer.VerticalOffset + _direction * amount);
        _distance += amount;
        if (!_held && _distance >= TapDistance - .01) Stop();
    }
}

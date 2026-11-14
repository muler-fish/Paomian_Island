using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace Paomian.KeyboardPrototype;

internal sealed class KeyboardScroll
{
    private const double TapDistance = 28;
    private const double StartSpeed = 220;
    private const double HoldSpeed = 560;
    private const double RampSeconds = .18;
    private readonly ScrollViewer _view;
    private readonly DispatcherTimer _timer;
    private IScrollInfo? _scrollInfo;
    private int _direction;
    private bool _upHeld, _downHeld;
    private long _previousTick;
    private double _elapsed, _travel, _position;

    internal KeyboardScroll(ScrollViewer view)
    {
        _view = view;
        _timer = new DispatcherTimer(DispatcherPriority.Render, view.Dispatcher) { Interval = TimeSpan.FromMilliseconds(12) };
        _timer.Tick += Tick;
    }

    internal static ScrollContentPresenter? Presenter(ScrollViewer viewer) =>
        viewer.Template?.FindName("PART_ScrollContentPresenter", viewer) as ScrollContentPresenter;

    internal void Press(int direction)
    {
        if (direction < 0 ? _upHeld : _downHeld) return;
        if (direction < 0) _upHeld = true;
        else _downHeld = true;
        Begin(direction);
    }

    internal void Release(int direction)
    {
        if (direction < 0) _upHeld = false;
        else _downHeld = false;
        if (direction != _direction) return;

        if (_upHeld || _downHeld) Begin(-direction);
        else if (_travel >= TapDistance) _timer.Stop();
    }

    internal void Stop()
    {
        _upHeld = _downHeld = false;
        _timer.Stop();
    }

    internal void Page(int direction)
    {
        Stop();
        _view.ScrollToVerticalOffset(Math.Clamp(_view.VerticalOffset + direction * _view.ViewportHeight * .92, 0, _view.ScrollableHeight));
    }

    private void Begin(int direction)
    {
        _direction = direction;
        _elapsed = _travel = 0;
        _position = _view.VerticalOffset;
        _scrollInfo = Presenter(_view);
        _previousTick = Stopwatch.GetTimestamp();
        _timer.Start();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var tick = Stopwatch.GetTimestamp();
        var seconds = Math.Min(Stopwatch.GetElapsedTime(_previousTick, tick).TotalSeconds, .033);
        _previousTick = tick;

        _elapsed += seconds;
        var velocity = StartSpeed + (HoldSpeed - StartSpeed) * Math.Min(1, _elapsed / RampSeconds);
        var held = _direction < 0 ? _upHeld : _downHeld;
        var distance = held ? seconds * velocity : Math.Min(seconds * velocity, TapDistance - _travel);
        // 直接更新物理滚动的 presenter，避免逐帧排入 ScrollViewer 的布局命令队列。
        var next = Math.Clamp(_position + _direction * distance, 0, _view.ScrollableHeight);
        if (next != _position) _scrollInfo?.SetVerticalOffset(next);
        _travel += Math.Abs(next - _position);
        _position = next;
        if (next == 0 || next == _view.ScrollableHeight || (!held && _travel >= TapDistance - .01)) _timer.Stop();
    }
}

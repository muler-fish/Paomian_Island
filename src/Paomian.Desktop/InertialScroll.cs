using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Paomian.Desktop;

internal enum WheelMode { Native, Short, Legacy }

/// <summary>阅读区域的滚轮路由。默认使用定时到达目标的短过渡，验收入口可切换原生或旧弹簧。</summary>
public static class InertialScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(InertialScroll), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(ScrollState), typeof(InertialScroll));
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static void Stop(DependencyObject target) => (target.GetValue(StateProperty) as ScrollState)?.Stop();
    internal static bool IsRunning(DependencyObject target) => target.GetValue(StateProperty) is ScrollState { Running: true };
    internal static double RemainingDistance(DependencyObject target) =>
        target.GetValue(StateProperty) is ScrollState state ? state.Remaining : 0;
    internal static bool AllowsAnimation(bool enabled, bool systemAnimation) => enabled && systemAnimation;
    internal static void SetMode(DependencyObject target, WheelMode mode)
    {
        if (target.GetValue(StateProperty) is ScrollState state) { state.Stop(); state.Mode = mode; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct InputSource { public uint DeviceType, OriginId; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCurrentInputMessageSource(out InputSource source);

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not FrameworkElement surface || surface is not (ListBox or ScrollViewer)) return;
        if (surface.GetValue(StateProperty) is not ScrollState state)
        {
            state = new ScrollState(surface); surface.SetValue(StateProperty, state);
        }
        state.Enable((bool)args.NewValue);
    }

    private sealed class ScrollState
    {
        private readonly FrameworkElement _surface;
        private readonly ScrollMotion _motion = new();
        private readonly ShortWheelMotion _short = new();
        internal WheelMode Mode { get; set; } = WheelMode.Short;
        private double _pixelRemainder;
        private double? _queuedOffset;
        private long _segmentStart;
        private long _lastFineInput;
        private TimeSpan? _renderTime;
        private ScrollViewer? _viewer;
        private ReadingScrollPanel? _panel;
        private Window? _window;
        private INotifyCollectionChanged? _items;
        private bool _enabled, _attached, _panelResolved;
        private long _lastFrame;
        private double _requestedOffset;
        public bool Running { get; private set; }
        public double Remaining => !Running ? 0 : Mode == WheelMode.Short ? _short.Remaining : _motion.Target - _motion.Position;

        public ScrollState(FrameworkElement surface)
        {
            _surface = surface;
            surface.Loaded += (_, _) => Attach();
            surface.Unloaded += (_, _) => Detach();
        }

        public void Enable(bool enabled)
        {
            _enabled = enabled;
            if (enabled && _surface.IsLoaded) Attach(); else if (!enabled) Detach();
        }

        private void Attach()
        {
            if (_attached || !_enabled) return;
            _attached = true;
            _viewer = FindViewer(_surface);
            _window = Window.GetWindow(_surface);
            _surface.PreviewMouseWheel += Wheel;
            _surface.IsVisibleChanged += VisibilityChanged;
            _surface.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            if (_surface is ListBox list)
            {
                _items = list.Items; _items.CollectionChanged += ItemsChanged;
            }
            if (_window is not null)
            {
                _window.Deactivated += Deactivated;
                _window.PreviewMouseDown += MouseDown;
                // 主窗的栏位键可能先处理事件，已有的滚轮惯性仍需停止。
                _window.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown), handledEventsToo: true);
            }
            SystemParameters.StaticPropertyChanged += SystemChanged;
        }

        private void Detach()
        {
            Stop();
            if (!_attached) return;
            _attached = false;
            _surface.PreviewMouseWheel -= Wheel;
            _surface.IsVisibleChanged -= VisibilityChanged;
            _surface.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            if (_items is not null) _items.CollectionChanged -= ItemsChanged;
            if (_window is not null)
            {
                _window.Deactivated -= Deactivated;
                _window.PreviewMouseDown -= MouseDown;
                _window.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown));
            }
            SystemParameters.StaticPropertyChanged -= SystemChanged;
            _items = null; _window = null; _viewer = null; _panel = null; _panelResolved = false;
        }

        private void Wheel(object sender, MouseWheelEventArgs args)
        {
            if (args.Handled || !_enabled || Mode == WheelMode.Native) return;
            // Ctrl+滚轮缩放、编辑输入和选区拖动继续交给各自控件。
            if (Keyboard.Modifiers != ModifierKeys.None || Mouse.LeftButton == MouseButtonState.Pressed) { Stop(); return; }
            _viewer ??= FindViewer(_surface);
            if (_viewer is null || _viewer.ScrollableHeight <= 0 || args.Delta == 0 || !OwnsWheel(args.OriginalSource as DependencyObject)) return;
            if (_surface is ListBox list && _viewer.CanContentScroll && VirtualizingPanel.GetScrollUnit(list) != ScrollUnit.Pixel) return;
            if (!_panelResolved)
            {
                _panel = _surface is ListBox ? ReadingAnchor.Children<ReadingScrollPanel>(_viewer)
                    .FirstOrDefault(p => p.ScrollOwner == _viewer) : null;
                _panelResolved = true;
            }
            var lines = SystemParameters.WheelScrollLines;
            if (Mode == WheelMode.Short)
            {
                if (lines == 0) { Stop(); args.Handled = true; return; }
                var fine = args.Delta % Mouse.MouseWheelDeltaForOneLine != 0 || args.StylusDevice is not null ||
                    GetCurrentInputMessageSource(out var source) && source.DeviceType is 4 or 8 or 16;
                if (fine) _lastFineInput = Stopwatch.GetTimestamp();
                var fineBurst = _lastFineInput != 0 && Stopwatch.GetElapsedTime(_lastFineInput).TotalMilliseconds < ShortWheelMotion.DurationMs;
                var notch = lines < 0 ? _viewer.ViewportHeight : Math.Min(_viewer.ViewportHeight, lines * 24d);
                ShortWheel(args, -args.Delta / (double)Mouse.MouseWheelDeltaForOneLine * notch, fineBurst || !SystemParameters.ClientAreaAnimation);
                return;
            }
            if (!SystemParameters.ClientAreaAnimation) return;
            var step = lines < 0 ? _viewer.ViewportHeight * .9 : lines * 24d;
            if (step <= 0) return;
            var distance = -args.Delta / 120d * step;
            var offset = _viewer.VerticalOffset;
            if (Running && _panel is not null) _motion.Rebase(_panel.VerticalOffset, _viewer.ScrollableHeight);
            if (distance < 0 && offset <= .1 || distance > 0 && offset >= _viewer.ScrollableHeight - .1) { Stop(); return; }
            _motion.Push(distance, offset, _viewer.ScrollableHeight, _viewer.ViewportHeight);
            args.Handled = true;
            if (!Running && _motion.IsActive)
            {
                Running = true; _requestedOffset = offset; _lastFrame = Stopwatch.GetTimestamp();
                CompositionTarget.Rendering += Frame;
            }
        }

        private void ShortWheel(MouseWheelEventArgs args, double distance, bool direct)
        {
            var offset = _panel?.RequestedOffset ?? _queuedOffset ?? _viewer!.VerticalOffset;
            var max = _viewer!.ScrollableHeight;
            if (distance < 0 && offset <= 0 || distance > 0 && offset >= max) { Stop(); return; }
            if (!direct) _segmentStart = Stopwatch.GetTimestamp();
            var step = _short.Push(distance, offset, max, direct);
            args.Handled = true;
            ApplyShortStep(step);
            if (_short.Remaining == 0) EndFrames();
            else if (!Running)
            {
                Running = true;
                CompositionTarget.Rendering += Frame;
            }
        }

        private void ApplyShortStep(double step)
        {
            // 累计不足一个设备像素的余量，避免低速尾段或高分辨率滚轮逐帧舍入丢失。
            var scale = VisualTreeHelper.GetDpi(_surface).DpiScaleY;
            var exact = step + _pixelRemainder;
            var rounded = Math.Round(exact * scale, MidpointRounding.ToEven) / scale;
            _pixelRemainder = exact - rounded;
            if (rounded == 0) return;
            if (_panel is not null) _panel.ScrollBy(rounded, wheelStep: true);
            else
            {
                _queuedOffset = Math.Clamp((_queuedOffset ?? _viewer!.VerticalOffset) + rounded, 0, _viewer!.ScrollableHeight);
                _viewer.ScrollToVerticalOffset(_queuedOffset.Value);
            }
        }

        private bool OwnsWheel(DependencyObject? source)
        {
            // 只接管本阅读区域。可编辑文本框、下拉框、可独立滚动的嵌套区域优先。
            for (var node = source; node is not null && node != _surface; node = Parent(node))
            {
                if (node is TextBoxBase { IsReadOnly: false } or PasswordBox or ComboBox) return false;
                if (node is ScrollViewer nested && nested != _viewer && nested.ScrollableHeight > 0 && nested.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled) return false;
            }
            return true;
        }

        private void Frame(object? sender, EventArgs args)
        {
            if (_viewer is null || !_surface.IsVisible || !AllowsAnimation(_enabled, SystemParameters.ClientAreaAnimation)) { Stop(); return; }
            if (Mode == WheelMode.Short)
            {
                var time = ((RenderingEventArgs)args).RenderingTime;
                if (_renderTime == time) return;
                _renderTime = time;
                var offset = _panel?.RequestedOffset ?? _queuedOffset ?? _viewer.VerticalOffset;
                if (_short.Remaining < 0 && offset <= 0 || _short.Remaining > 0 && offset >= _viewer.ScrollableHeight) { Stop(); return; }
                ApplyShortStep(_short.Advance(Stopwatch.GetElapsedTime(_segmentStart).TotalMilliseconds));
                if (_short.Remaining == 0) EndFrames();
                return;
            }
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
            _lastFrame = now;
            // 长时间挂起或切出后不补播一大段位移。
            if (elapsed > .25) { Stop(); return; }
            if (_panel is not null) _motion.Rebase(_panel.VerticalOffset, _viewer.ScrollableHeight);
            var previous = _motion.Position;
            _requestedOffset = _motion.Advance(elapsed, _viewer.ScrollableHeight);
            if (_panel is null) _viewer.ScrollToVerticalOffset(_requestedOffset);
            else _panel.ScrollBy(_requestedOffset - previous);
            if (!_motion.IsActive) Stop();
        }

        private void Scrolled(object sender, ScrollChangedEventArgs args)
        {
            if (_panel is not null || !ReferenceEquals(args.OriginalSource, _viewer)) return;
            if (Mode == WheelMode.Short)
            {
                if (args.VerticalChange == 0 && args.ExtentHeightChange == 0 && args.ViewportHeightChange == 0) return;
                if (Running && args.ExtentHeightChange == 0 && args.ViewportHeightChange == 0 &&
                    (_queuedOffset is not { } requested || Math.Abs(requested - args.VerticalOffset) > 1)) Stop();
                _queuedOffset = null;
                return;
            }
            if (!Running) return;
            if (args.ExtentHeightChange != 0 || args.ViewportHeightChange != 0)
                _motion.Rebase(_viewer!.VerticalOffset, _viewer.ScrollableHeight);
            else if (Math.Abs(_viewer!.VerticalOffset - _requestedOffset) > 2)
                Stop(); // 跳页、焦点定位或外部滚动不能被下一帧惯性拉回。
        }

        public void Stop()
        {
            EndFrames();
            _short.Reset(); _pixelRemainder = 0; _queuedOffset = null; _lastFineInput = 0;
            _panel?.ForgetWheelRow();
            _motion.Reset(_viewer?.VerticalOffset ?? 0);
        }

        private void EndFrames()
        {
            if (Running) CompositionTarget.Rendering -= Frame;
            Running = false;
            _renderTime = null;
        }
        private void ItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action is NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Remove) Stop();
        }
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (!(bool)args.NewValue) Stop(); }
        private void Deactivated(object? sender, EventArgs args) => Stop();
        private void MouseDown(object sender, MouseButtonEventArgs args) => Stop();
        private void KeyDown(object sender, KeyEventArgs args) => Stop();
        private void SystemChanged(object? sender, PropertyChangedEventArgs args) { if (!SystemParameters.ClientAreaAnimation) Stop(); }

        private static ScrollViewer? FindViewer(DependencyObject node)
        {
            if (node is ScrollViewer viewer) return viewer;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (FindViewer(VisualTreeHelper.GetChild(node, i)) is { } found) return found;
            return null;
        }
        private static DependencyObject? Parent(DependencyObject node) => node switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(node),
            FrameworkContentElement content => content.Parent,
            ContentElement content => ContentOperations.GetParent(content),
            _ => LogicalTreeHelper.GetParent(node)
        };
    }
}

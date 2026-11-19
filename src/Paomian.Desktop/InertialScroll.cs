using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Paomian.Desktop;

/// <summary>对选定阅读区域的滚轮输入施加短促惯性；不改变原生滚动条、键盘和编辑框行为。</summary>
public static class InertialScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(InertialScroll), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(ScrollState), typeof(InertialScroll));
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static void Stop(DependencyObject target) => (target.GetValue(StateProperty) as ScrollState)?.Stop();
    internal static bool IsRunning(DependencyObject target) => target.GetValue(StateProperty) is ScrollState { Running: true };
    internal static bool AllowsAnimation(bool enabled, bool systemAnimation) => enabled && systemAnimation;

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
        private ScrollViewer? _viewer;
        private Window? _window;
        private INotifyCollectionChanged? _items;
        private bool _enabled, _attached;
        private long _lastFrame;
        private double _requestedOffset;
        public bool Running { get; private set; }

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
            _items = null; _window = null; _viewer = null;
        }

        private void Wheel(object sender, MouseWheelEventArgs args)
        {
            if (args.Handled || !AllowsAnimation(_enabled, SystemParameters.ClientAreaAnimation)) return;
            // Ctrl+滚轮缩放、编辑输入和选区拖动继续交给各自控件。
            if (Keyboard.Modifiers != ModifierKeys.None || Mouse.LeftButton == MouseButtonState.Pressed) { Stop(); return; }
            _viewer ??= FindViewer(_surface);
            if (_viewer is null || _viewer.ScrollableHeight <= 0 || args.Delta == 0 || !OwnsWheel(args.OriginalSource as DependencyObject)) return;
            if (_surface is ListBox list && _viewer.CanContentScroll && VirtualizingPanel.GetScrollUnit(list) != ScrollUnit.Pixel) return;
            var lines = SystemParameters.WheelScrollLines;
            var step = lines < 0 ? _viewer.ViewportHeight * .9 : lines * 24d;
            if (step <= 0) return;
            var distance = -args.Delta / 120d * step;
            var offset = _viewer.VerticalOffset;
            if (distance < 0 && offset <= .1 || distance > 0 && offset >= _viewer.ScrollableHeight - .1) { Stop(); return; }
            _motion.Push(distance, offset, _viewer.ScrollableHeight, _viewer.ViewportHeight);
            args.Handled = true;
            if (!Running && _motion.IsActive)
            {
                Running = true; _requestedOffset = offset; _lastFrame = Stopwatch.GetTimestamp();
                CompositionTarget.Rendering += Frame;
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
            var now = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(_lastFrame, now).TotalSeconds;
            _lastFrame = now;
            // 长时间挂起或切出后不补播一大段位移。
            if (elapsed > .25) { Stop(); return; }
            _requestedOffset = _motion.Advance(elapsed, _viewer.ScrollableHeight);
            _viewer.ScrollToVerticalOffset(_requestedOffset);
            if (!_motion.IsActive) Stop();
        }

        private void Scrolled(object sender, ScrollChangedEventArgs args)
        {
            if (!Running || !ReferenceEquals(args.OriginalSource, _viewer)) return;
            if (args.ExtentHeightChange != 0 || args.ViewportHeightChange != 0)
                _motion.Rebase(_viewer!.VerticalOffset, _viewer.ScrollableHeight);
            else if (Math.Abs(_viewer!.VerticalOffset - _requestedOffset) > 2)
                Stop(); // 跳页、焦点定位或外部滚动不能被下一帧惯性拉回。
        }

        public void Stop()
        {
            if (Running) CompositionTarget.Rendering -= Frame;
            Running = false;
            _motion.Reset(_viewer?.VerticalOffset ?? 0);
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

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tide.Desktop;

/// <summary>在阅读区两端续载；跳页后等用户滚动，不自动从目标页倒着读回首页。</summary>
public static class IncrementalScroll
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(IncrementalScroll), new PropertyMetadata(null, CommandChanged));
    public static readonly DependencyProperty PreviousCommandProperty = DependencyProperty.RegisterAttached(
        "PreviousCommand", typeof(ICommand), typeof(IncrementalScroll), new PropertyMetadata(null, CommandChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(ScrollState), typeof(IncrementalScroll));
    public static ICommand? GetCommand(DependencyObject target) => (ICommand?)target.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject target, ICommand? value) => target.SetValue(CommandProperty, value);
    public static ICommand? GetPreviousCommand(DependencyObject target) => (ICommand?)target.GetValue(PreviousCommandProperty);
    public static void SetPreviousCommand(DependencyObject target, ICommand? value) => target.SetValue(PreviousCommandProperty, value);
    public static void Reset(ListBox list, bool waitForInput) => (list.GetValue(StateProperty) as ScrollState)?.Reset(waitForInput);
    public static void PrependCompleted(ListBox list) => Reset(list, true);
    internal static void RequestScroll(ListBox list, int direction) => (list.GetValue(StateProperty) as ScrollState)?.Intent(direction);

    private static void CommandChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ListBox list) return;
        if (list.GetValue(StateProperty) is not ScrollState state)
        {
            state = new ScrollState(list); list.SetValue(StateProperty, state);
        }
        state.SetCommands(GetCommand(list), GetPreviousCommand(list));
    }

    private sealed class ScrollState
    {
        private readonly ListBox _list;
        private readonly DispatcherTimer _timer;
        private ICommand? _command, _previous;
        private ScrollViewer? _viewer;
        private bool _attached;
        private bool _waitingForInput;
        private int _direction;

        public ScrollState(ListBox list)
        {
            _list = list;
            _timer = new DispatcherTimer(DispatcherPriority.Background, list.Dispatcher) { Interval = TimeSpan.FromMilliseconds(90) };
            _timer.Tick += (_, _) => CheckEdges();
            list.Loaded += (_, _) => Attach();
            list.Unloaded += (_, _) => Detach();
        }

        public void SetCommands(ICommand? command, ICommand? previous)
        {
            Detach(); _command = command; _previous = previous;
            if (_list.IsLoaded) Attach();
        }

        private void Attach()
        {
            if (_attached || _command is null && _previous is null) return;
            _attached = true;
            _list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            _list.SizeChanged += Resized;
            if (_command is not null) _command.CanExecuteChanged += CommandReady;
            if (_previous is not null) _previous.CanExecuteChanged += CommandReady;
            _list.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel), true);
            _list.PreviewKeyDown += KeyDown;
            _list.PreviewMouseDown += MouseDown;
            Schedule();
        }

        private void Detach()
        {
            _timer.Stop(); _viewer = null;
            if (!_attached) return;
            _attached = false;
            _list.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            _list.SizeChanged -= Resized;
            if (_command is not null) _command.CanExecuteChanged -= CommandReady;
            if (_previous is not null) _previous.CanExecuteChanged -= CommandReady;
            _list.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel));
            _list.PreviewKeyDown -= KeyDown;
            _list.PreviewMouseDown -= MouseDown;
        }

        public void Reset(bool waitForInput) { _timer.Stop(); _direction = 0; _waitingForInput = waitForInput; }
        internal void Intent(int direction) { _waitingForInput = false; _direction = direction; Schedule(); }
        private void Wheel(object sender, MouseWheelEventArgs args)
        {
            if (Keyboard.Modifiers == ModifierKeys.None && args.Delta != 0) Intent(args.Delta > 0 ? -1 : 1);
        }
        private void KeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key is Key.Up or Key.PageUp or Key.Home) Intent(-1);
            if (args.Key is Key.Down or Key.PageDown or Key.End) Intent(1);
        }
        private void MouseDown(object sender, MouseButtonEventArgs args)
        {
            // 拖动滚动条也是阅读意图；布局/跳页产生的 ScrollChanged 本身不算。
            for (var node = args.OriginalSource as DependencyObject; node is Visual && node != _list; node = VisualTreeHelper.GetParent(node))
                if (node is ScrollBar) { _waitingForInput = false; break; }
        }

        private void Resized(object sender, SizeChangedEventArgs args) => Schedule();
        private void CommandReady(object? sender, EventArgs args) => Schedule();
        private void Scrolled(object sender, ScrollChangedEventArgs args)
        {
            _viewer ??= FindViewer(_list);
            // 忽略正文控件等子树内其他 ScrollViewer 的冒泡事件。
            if (ReferenceEquals(args.OriginalSource, _viewer))
            {
                if (!_waitingForInput && args.VerticalChange != 0 && args.ExtentHeightChange == 0 && args.ViewportHeightChange == 0)
                    _direction = Math.Sign(args.VerticalChange);
                Schedule();
            }
        }
        private void Schedule()
        {
            if (!_attached) return;
            // 惯性滚动会逐帧产生事件；节流而非一直延迟，移动期间也能及时触发续载。
            if (!_timer.IsEnabled) _timer.Start();
        }
        private void CheckEdges()
        {
            _timer.Stop();
            if (!_list.IsVisible || _waitingForInput || _list.Items.Count == 0 && _previous is null) return;
            _viewer ??= FindViewer(_list);
            if (_viewer is null || _viewer.ViewportHeight <= 0) return;
            if (_direction < 0)
            {
                if (_viewer.VerticalOffset <= 48 && _previous?.CanExecute(null) == true) _previous.Execute(null);
                return;
            }
            if (_command?.CanExecute(null) != true || _viewer.ScrollableHeight - _viewer.VerticalOffset > 48) return;
            // 将 End/ScrollToEnd 的末端请求固定成当前像素位置，新增内容后不跟着跳到新底部。
            _viewer.ScrollToVerticalOffset(_viewer.VerticalOffset);
            _command.Execute(null);
        }

        private static ScrollViewer? FindViewer(DependencyObject root)
        {
            if (root is ScrollViewer viewer) return viewer;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (FindViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
            return null;
        }
    }
}

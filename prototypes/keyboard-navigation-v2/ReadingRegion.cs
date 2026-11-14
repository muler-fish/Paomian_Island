using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tide.Desktop;

namespace Paomian.KeyboardPrototype;

internal sealed record ReadingTarget(Button Button, string Name, Action Execute, bool InScroll);
internal sealed record VisibleTarget(ReadingTarget Target, Rect Bounds, Rect Viewport);
internal sealed record HintLabel(VisibleTarget Item, string Text, Rect Bounds);

internal sealed class ReadingRegion
{
    private readonly List<ReadingTarget> _targets = [];
    private readonly RegionPane _pane;
    private readonly Canvas _overlay = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly StackPanel _bottom = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private DispatcherTimer? _feedbackTimer;

    internal Grid Root => _pane.Root;
    internal TextBlock Heading => _pane.Heading;
    internal bool ShowsFocus => _pane.ShowsFocus;
    internal ScrollViewer View { get; } = new() { Focusable = true, IsTabStop = true, FocusVisualStyle = null, Background = Brushes.Transparent };
    internal KeyboardScroll Scroll { get; }
    internal IReadOnlyList<Rect> HintBounds => _overlay.Children.OfType<Border>()
        .Select(badge => new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), badge.Width, badge.Height)).ToArray();

    internal ReadingRegion(string name, string background, bool footer = false)
    {
        _pane = new RegionPane(name, background);
        Scroll = new KeyboardScroll(View);
        AutomationProperties.SetName(View, name);
        InputMethod.SetIsInputMethodEnabled(View, false);
        KeyboardNavigation.SetDirectionalNavigation(Root, KeyboardNavigationMode.None);
        KeyboardNavigation.SetDirectionalNavigation(View, KeyboardNavigationMode.None);
        Grid.SetRow(View, 1);
        Root.Children.Add(View);
        if (footer)
        {
            Root.RowDefinitions.Add(new() { Height = UiMetrics.MainTitleRow });
            var bar = new Border { Child = _bottom, BorderThickness = new Thickness(0, 1, 0, 0) };
            bar.SetResourceReference(Border.BorderBrushProperty, "Line");
            Grid.SetRow(bar, 2);
            Root.Children.Add(bar);
        }

        Grid.SetRowSpan(_overlay, Root.RowDefinitions.Count);
        Panel.SetZIndex(_overlay, 2);
        Root.Children.Add(_overlay);
    }

    internal Button ActionButton(string name, Action action, string? style = null, bool inScroll = true)
    {
        var button = new Button { Content = name };
        if (style is not null) button.SetResourceReference(FrameworkElement.StyleProperty, style);
        AutomationProperties.SetName(button, name);
        void Execute()
        {
            if (!button.IsEnabled || !button.IsVisible) return;
            Scroll.Stop();
            action();
        }
        button.Click += (_, _) => Execute();
        _targets.Add(new ReadingTarget(button, name, Execute, inScroll));
        if (inScroll)
        {
            // 部分露出的链接可获焦点，滚动位置仍由用户决定。
            button.RequestBringIntoView += (_, e) =>
            {
                if (e.TargetObject == button && (button.IsKeyboardFocused || (Mouse.LeftButton == MouseButtonState.Pressed && button.IsMouseOver)) &&
                    !VisibleBounds(button, inScroll: true).IsEmpty)
                    e.Handled = true;
            };
        }
        return button;
    }

    internal Button ToolbarButton(string name, Action execute, bool bottom = false, bool primary = false)
    {
        var button = ActionButton(name, execute, primary ? "Primary" : null, inScroll: false);
        button.Margin = new(6, 0, 0, 0);
        (bottom ? _bottom : _pane.Tools).Children.Add(button);
        return button;
    }

    internal void ClearScrollTargets() => _targets.RemoveAll(target => target.InScroll);

    internal VisibleTarget[] VisibleTargets()
    {
        var visible = new List<VisibleTarget>();
        foreach (var target in _targets.OrderBy(target => !target.InScroll))
        {
            var bounds = VisibleBounds(target.Button, target.InScroll);
            if (!bounds.IsEmpty && bounds.Width >= 12 && bounds.Height >= 12)
                visible.Add(new VisibleTarget(target, bounds, TargetViewport(target.Button, target.InScroll)));
        }
        return [.. visible];
    }

    internal void PrepareTabStops()
    {
        var visible = VisibleTargets().Select(item => item.Target).ToHashSet();
        foreach (var target in _targets)
            target.Button.IsTabStop = visible.Contains(target);
    }

    internal void ClearOverlay()
    {
        _overlay.Children.Clear();
        Status("");
    }

    internal void Feedback(string message)
    {
        Status(message);
        if (_feedbackTimer is null)
        {
            _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _feedbackTimer.Tick += (_, _) => Status("");
        }
        _feedbackTimer.Start();
    }

    internal HintLabel[] PlaceHints(VisibleTarget[] targets, string[] labels)
    {
        var placed = new List<HintLabel>();
        var used = new List<Rect>();
        const double height = 19;
        for (var i = 0; i < targets.Length; i++)
        {
            var item = targets[i];
            var target = item.Bounds;
            var width = 12 + 8.5 * labels[i].Length;
            var fullRow = target.Width > Root.ActualWidth * .65;
            var preferred = fullRow
                ? new Point(target.Right - width - 12, target.Top + 5)
                : new Point(target.Left + 2, target.Top - height + (item.Target.InScroll ? 4 : 6));
            var bounds = PlaceNear(preferred, width, height, item.Viewport, used)
                ?? PlaceNear(preferred, width, height, new Rect(Root.RenderSize), used);
            if (bounds is not { } box) continue;
            placed.Add(new HintLabel(item, labels[i], box));
            used.Add(box);
        }
        return [.. placed];
    }

    internal void DrawHints(HintLabel[] labels, string prefix)
    {
        _overlay.Children.Clear();
        var badges = new List<Border>();
        foreach (var hint in labels)
        {
            if (!hint.Text.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var box = hint.Bounds;
            var target = hint.Item.Bounds;
            var nearest = new Point(Math.Clamp(box.Left + box.Width / 2, target.Left, target.Right), Math.Clamp(box.Top + box.Height / 2, target.Top, target.Bottom));
            if (nearest.Y < box.Top - 3 || nearest.Y > box.Bottom + 3 || nearest.X < box.Left - 3 || nearest.X > box.Right + 3)
            {
                var line = new Line { X1 = nearest.X, Y1 = nearest.Y, X2 = box.Left + box.Width / 2, Y2 = box.Top + box.Height / 2, StrokeThickness = 1 };
                line.SetResourceReference(Shape.StrokeProperty, "Muted");
                _overlay.Children.Add(line);
            }

            var text = new TextBlock
            {
                FontFamily = new FontFamily("Consolas"), FontSize = 13, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, prefix.Length == 0 ? "Surface" : "OnAccent");
            if (prefix.Length > 0) text.Inlines.Add(new Run(prefix) { TextDecorations = TextDecorations.Underline });
            text.Inlines.Add(new Run(hint.Text[prefix.Length..]));
            var badge = new Border { Width = box.Width, Height = box.Height, Child = text, CornerRadius = new CornerRadius(3) };
            badge.SetResourceReference(Border.BackgroundProperty, prefix.Length == 0 ? "Text" : "Accent");
            Canvas.SetLeft(badge, box.Left);
            Canvas.SetTop(badge, box.Top);
            badges.Add(badge);
        }
        foreach (var badge in badges) _overlay.Children.Add(badge);
        Status(prefix.Length == 0 ? $"{labels.Length} 个目标 · Esc 取消" : $"{prefix} · 剩余 {badges.Count} 个 · Backspace 更正");
    }

    private void Status(string message)
    {
        _feedbackTimer?.Stop();
        _pane.Status.Text = message;
        _pane.Status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Rect? PlaceNear(Point anchor, double width, double height, Rect area, List<Rect> used)
    {
        if (area.Width < width + 1 || area.Height < height + 1) return null;
        Rect At(double x, double y) => new(
            Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width)),
            Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height)), width, height);

        bool Fits(Rect rect)
        {
            // 为布局取整和实际缩放保留间隙。
            rect.Inflate(1, 1);
            return !used.Any(item => item.IntersectsWith(rect));
        }
        var first = At(anchor.X, anchor.Y);
        if (Fits(first)) return first;
        int[] offsets = [0, 1, -1, 2, -2];
        foreach (var y in offsets)
            foreach (var x in offsets)
            {
                var nearby = At(first.X + x * (width + 3), first.Y + y * (height + 3));
                if (Fits(nearby)) return nearby;
            }
        for (var y = area.Top; y + height <= area.Bottom; y += height + 2)
            for (var x = area.Left; x + width <= area.Right; x += width + 2)
            {
                var cell = new Rect(x, y, width, height);
                if (Fits(cell)) return cell;
            }
        return null;
    }

    private Rect VisibleBounds(Button button, bool inScroll)
    {
        if (!button.IsVisible || !button.IsEnabled || !button.IsDescendantOf(Root)) return Rect.Empty;
        return Rect.Intersect(BoundsOf(button), TargetViewport(button, inScroll));
    }

    private Rect TargetViewport(Button button, bool inScroll)
    {
        var frame = new Rect(Root.RenderSize);
        var site = inScroll ? (FrameworkElement?)KeyboardScroll.Presenter(View) : _pane.Tools.IsAncestorOf(button) ? _pane.Tools : _bottom;
        if (site is null) return Rect.Empty;
        var zone = BoundsOf(site);
        if (!inScroll) zone.Inflate(0, 9);
        return Rect.Intersect(frame, zone);
    }

    private Rect BoundsOf(FrameworkElement element) => element.TransformToVisual(Root).TransformBounds(new Rect(element.RenderSize));
}

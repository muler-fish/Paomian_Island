using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Paomian.KeyboardPrototype;

internal sealed record ReadingTarget(Button Button, string Name, Action Execute, bool InScroll);
internal sealed record VisibleTarget(ReadingTarget Target, Rect Bounds);

internal sealed class ReadingRegion
{
    private readonly List<ReadingTarget> _targets = [];
    private readonly Border _focusMarker = new() { Width = 2, Height = 18, Opacity = 0, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _overlay = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly TextBlock _status = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private DispatcherTimer? _feedbackTimer;

    internal Grid Root { get; } = new() { ClipToBounds = true };
    internal ScrollViewer View { get; } = new() { Focusable = true, FocusVisualStyle = null, Background = Brushes.Transparent };
    internal TextBlock Heading { get; } = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    internal StackPanel Top { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    internal StackPanel Bottom { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    internal KeyboardScroll Scroll { get; }
    internal IReadOnlyList<Rect> HintBounds => _overlay.Children.OfType<Border>()
        .Select(badge => new Rect(Canvas.GetLeft(badge), Canvas.GetTop(badge), badge.Width, badge.Height)).ToArray();

    internal ReadingRegion(string name, string background, bool footer = false)
    {
        Scroll = new KeyboardScroll(View);
        Root.SetResourceReference(Panel.BackgroundProperty, background);
        AutomationProperties.SetName(View, name);
        InputMethod.SetIsInputMethodEnabled(View, false);
        FocusManager.SetIsFocusScope(Root, true);

        Heading.Text = name;
        var headings = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(10, 0, 6, 0) };
        headings.Children.Add(Heading);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _status.Visibility = Visibility.Collapsed;
        _status.SetValue(AutomationProperties.LiveSettingProperty, AutomationLiveSetting.Polite);
        headings.Children.Add(_status);

        _focusMarker.SetResourceReference(Border.BackgroundProperty, "Accent");
        var header = new DockPanel { Margin = new(0, 0, 12, 0) };
        header.Children.Add(_focusMarker);
        DockPanel.SetDock(Top, Dock.Right);
        header.Children.Add(Top);
        header.Children.Add(headings);

        Root.RowDefinitions.Add(new() { Height = Tide.Desktop.UiMetrics.MainTitleRow });
        Root.RowDefinitions.Add(new());
        Root.RowDefinitions.Add(new() { Height = footer ? Tide.Desktop.UiMetrics.MainTitleRow : new GridLength(0) });
        Root.Children.Add(Divider(header, new Thickness(0, 0, 0, 1)));
        Grid.SetRow(View, 1);
        Root.Children.Add(View);
        if (footer)
        {
            Bottom.HorizontalAlignment = HorizontalAlignment.Center;
            var bar = Divider(Bottom, new Thickness(0, 1, 0, 0));
            Grid.SetRow(bar, 2);
            Root.Children.Add(bar);
        }

        Grid.SetRowSpan(_overlay, 3);
        Root.Children.Add(_overlay);
        Root.IsKeyboardFocusWithinChanged += (_, _) => _focusMarker.Opacity = Root.IsKeyboardFocusWithin ? 1 : 0;
        View.PreviewMouseLeftButtonDown += (_, e) =>
        {
            for (var item = e.OriginalSource as Visual; item is not null && item != View; item = VisualTreeHelper.GetParent(item) as Visual)
                if (item is ButtonBase or ScrollBar) return;
            View.Focus();
        };
    }

    internal Button ActionButton(string name, Action execute, string? style = null, bool inScroll = true)
    {
        var button = new Button { Content = name };
        if (style is not null) button.SetResourceReference(FrameworkElement.StyleProperty, style);
        AutomationProperties.SetName(button, name);
        button.Click += (_, _) => execute();
        _targets.Add(new ReadingTarget(button, name, execute, inScroll));
        return button;
    }

    internal Button ToolbarButton(string name, Action execute, bool bottom = false, bool primary = false)
    {
        var button = ActionButton(name, execute, primary ? "Primary" : null, inScroll: false);
        button.Margin = new(6, 0, 0, 0);
        (bottom ? Bottom : Top).Children.Add(button);
        return button;
    }

    internal void ClearScrollTargets() => _targets.RemoveAll(target => target.InScroll);

    internal VisibleTarget[] VisibleTargets()
    {
        var frame = new Rect(Root.RenderSize);
        var viewport = KeyboardScroll.Presenter(View);
        var contentArea = viewport is null ? Rect.Empty : BoundsOf(viewport);
        var visible = new List<VisibleTarget>();
        foreach (var target in _targets.OrderBy(target => !target.InScroll))
        {
            var button = target.Button;
            if (!button.IsVisible || !button.IsEnabled || !button.IsDescendantOf(Root)) continue;
            var bounds = Rect.Intersect(BoundsOf(button), target.InScroll ? contentArea : frame);
            if (bounds.Width >= 12 && bounds.Height >= 12) visible.Add(new VisibleTarget(target, bounds));
        }
        return [.. visible];
    }

    internal void PrepareTabStops()
    {
        var visible = VisibleTargets().Select(item => item.Target).ToHashSet();
        foreach (var target in _targets)
            if (target.InScroll) target.Button.IsTabStop = visible.Contains(target);
    }

    internal void ClearOverlay()
    {
        _overlay.Children.Clear();
        Status("");
    }

    internal void Status(string message)
    {
        _feedbackTimer?.Stop();
        _status.Text = message;
        _status.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
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

    internal void ShowSelection(VisibleTarget target, int index, int count)
    {
        _overlay.Children.Clear();
        var bounds = target.Bounds;
        var box = new Rectangle { Width = bounds.Width - 2, Height = bounds.Height - 2, StrokeThickness = 2, StrokeDashArray = [2, 1] };
        box.SetResourceReference(Shape.StrokeProperty, "Muted");
        Place(box, bounds.X + 1, bounds.Y + 1);
        Status($"{index + 1}/{count} · {target.Target.Name}");
    }

    internal void ShowHints(VisibleTarget[] targets, string[] labels, string prefix)
    {
        _overlay.Children.Clear();
        var used = new List<Rect>();
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i].Bounds;
            var width = 12 + 7 * labels[i].Length;
            const double height = 16;
            var row = target.Width > Root.ActualWidth * .65;
            var anchor = row ? new Point(target.Right - width - 8, target.Top + 3) : new Point(target.Left + 3, target.Top - 11);
            var position = HintPosition(anchor, width, height, used);
            used.Add(position);
            if (!labels[i].StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (Math.Abs(position.X - anchor.X) + Math.Abs(position.Y - anchor.Y) > 6)
            {
                var line = new Line { X1 = anchor.X + width / 2, Y1 = Math.Max(target.Top, anchor.Y), X2 = position.X + width / 2, Y2 = position.Y + height / 2, StrokeThickness = 1 };
                line.SetResourceReference(Shape.StrokeProperty, "Muted");
                _overlay.Children.Add(line);
            }

            var text = new TextBlock
            {
                Text = labels[i], FontSize = 11, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Surface");
            var badge = new Border { Width = width, Height = height, Child = text, CornerRadius = new CornerRadius(3) };
            badge.SetResourceReference(Border.BackgroundProperty, "Text");
            Place(badge, position.X, position.Y);
        }

        Status(prefix.Length == 0 ? "输入字母 · Esc 取消" : $"已输入 {prefix} · Backspace 更正");
    }

    private Rect HintPosition(Point anchor, double width, double height, List<Rect> used)
    {
        Rect At(double x, double y) => new(
            Math.Clamp(x, 1, Math.Max(1, _overlay.ActualWidth - width - 1)),
            Math.Clamp(y, 1, Math.Max(1, _overlay.ActualHeight - height - 1)), width, height);

        var original = At(anchor.X, anchor.Y);
        int[] xOffsets = [0, 1, -1, 2, -2];
        int[] yOffsets = [0, -1, 1, -2, 2, -3, 3, -4, 4];
        foreach (var dy in yOffsets)
        {
            foreach (var dx in xOffsets)
            {
                var candidate = At(original.X + dx * (width + 3), original.Y + dy * (height + 3));
                var clearance = candidate;
                clearance.Inflate(2, 2);
                if (!used.Any(clearance.IntersectsWith)) return candidate;
            }
        }
        return original;
    }

    private Rect BoundsOf(FrameworkElement element) => element.TransformToVisual(Root).TransformBounds(new Rect(element.RenderSize));

    private void Place(FrameworkElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        _overlay.Children.Add(element);
    }

    private static Border Divider(UIElement content, Thickness line)
    {
        var border = new Border { Child = content, BorderThickness = line };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }
}

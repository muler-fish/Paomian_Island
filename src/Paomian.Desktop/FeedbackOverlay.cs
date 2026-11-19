using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Paomian.Desktop;

/// <summary>不占布局的窗口内气泡：成功从操作位置上浮，错误从区域上方滑入，定时消失。</summary>
public sealed class FeedbackOverlay : Canvas
{
    private readonly Dictionary<FeedbackArea, Entry> _entries = [];
    private readonly Func<bool> _animationsEnabled;
    internal static readonly TimeSpan SuccessEntrance = TimeSpan.FromMilliseconds(220);
    internal static readonly TimeSpan ErrorEntrance = TimeSpan.FromMilliseconds(240);
    internal static readonly TimeSpan SuccessHold = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan ErrorHold = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(120);
    private sealed record Entry(Border Bubble, ScrollViewer Content, FrameworkElement Region, Point? Origin,
        bool IsError, TranslateTransform Shift, DispatcherTimer Timer);
    internal int Count => _entries.Count;

    public FeedbackOverlay() : this(() => SystemParameters.ClientAreaAnimation) { }
    internal FeedbackOverlay(Func<bool> animationsEnabled)
    {
        _animationsEnabled = animationsEnabled;
        // 只改变 RenderTransform / Opacity，分栏和正文布局不参与动画。
        LayoutUpdated += (_, _) => { foreach (var entry in _entries.Values) Position(entry); };
        Unloaded += (_, _) => Clear();
    }

    /// <summary>先取坐标再执行操作，源按钮或列表行移除后仍能从正确位置弹出。</summary>
    internal Point? CaptureOrigin(FrameworkElement? source)
    {
        if (source is not { IsVisible: true } || !IsLoaded) return null;
        return PointFromScreen(source.PointToScreen(new Point(source.ActualWidth / 2, source.ActualHeight / 2)));
    }

    public void Show(InteractionFeedback feedback, FrameworkElement region, FrameworkElement? anchor = null, Point? origin = null)
    {
        Dismiss(feedback.Area);
        var text = new TextBlock { Text = feedback.Message, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
        var content = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        var shift = new TranslateTransform();
        var bubble = new Border { Child = content, Padding = new(12, 9, 12, 9), CornerRadius = new(6), BorderThickness = new(1), RenderTransform = shift };
        bubble.SetResourceReference(BackgroundProperty, feedback.IsError ? "DangerSoft" : "Surface");
        bubble.SetResourceReference(Border.BorderBrushProperty, feedback.IsError ? "Danger" : "Line");
        text.SetResourceReference(TextBlock.ForegroundProperty, feedback.IsError ? "Danger" : "Text");
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(bubble, feedback.Message);
        var hold = feedback.IsError ? ErrorHold : SuccessHold;
        var timer = new DispatcherTimer { Interval = hold };
        var entry = new Entry(bubble, content, region, origin ?? CaptureOrigin(anchor), feedback.IsError, shift, timer);
        _entries.Add(feedback.Area, entry); Children.Add(bubble); Position(entry);
        void RemoveCurrent()
        {
            // 旧动画完成时，新气泡可能已替换它，不能误删新气泡。
            if (_entries.TryGetValue(feedback.Area, out var current) && ReferenceEquals(current, entry)) Dismiss(feedback.Area);
        }
        if (_animationsEnabled())
        {
            var entrance = feedback.IsError ? ErrorEntrance : SuccessEntrance;
            var x = GetLeft(bubble); var y = GetTop(bubble); var size = bubble.DesiredSize;
            var start = entry.Origin ?? new Point(x + size.Width / 2, y + size.Height / 2 + 24);
            var startX = feedback.IsError ? 0 : start.X - size.Width / 2 - x;
            var startY = feedback.IsError ? -size.Height - 16 : start.Y - size.Height / 2 - y;
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            // WPF 动画时钟下一帧才开始推进，先放在起点，避免首帧闪到终点。
            shift.X = startX; shift.Y = startY; bubble.Opacity = 0;
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(startX, 0, entrance) { EasingFunction = easing });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(startY, 0, entrance) { EasingFunction = easing });
            var opacity = new DoubleAnimationUsingKeyFrames();
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(entrance)));
            opacity.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(entrance + hold)));
            opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(entrance + hold + ExitDuration)));
            opacity.Completed += (_, _) => RemoveCurrent();
            bubble.BeginAnimation(OpacityProperty, opacity);
        }
        else
        {
            // Windows 关闭动画时直接呈现，仍按相同停留时长收起。
            timer.Tick += (_, _) => RemoveCurrent(); timer.Start();
        }
        Dispatcher.BeginInvoke(() => UIElementAutomationPeer.CreatePeerForElement(text)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged), DispatcherPriority.Loaded);
    }

    private void Position(Entry entry)
    {
        if (ActualWidth < 1 || ActualHeight < 1 || !entry.Region.IsVisible) return;
        var regionOrigin = entry.Region.TranslatePoint(new Point(), this);
        entry.Content.MaxHeight = Math.Max(28, Math.Min(ActualHeight, entry.Region.ActualHeight) - 48);
        entry.Bubble.MaxWidth = Math.Max(100, Math.Min(420, Math.Min(ActualWidth, entry.Region.ActualWidth) - 16));
        entry.Bubble.Measure(new Size(entry.Bubble.MaxWidth, double.PositiveInfinity));
        var size = entry.Bubble.DesiredSize;
        var x = regionOrigin.X + (entry.Region.ActualWidth - size.Width) / 2;
        var y = regionOrigin.Y + 12;
        if (!entry.IsError && entry.Origin is { } origin)
        {
            x = origin.X - size.Width / 2;
            y = origin.Y - size.Height - 28;
        }
        SetLeft(entry.Bubble, Math.Clamp(x, 8, Math.Max(8, ActualWidth - size.Width - 8)));
        SetTop(entry.Bubble, Math.Clamp(y, 8, Math.Max(8, ActualHeight - size.Height - 8)));
    }

    public void Dismiss(FeedbackArea area)
    {
        if (!_entries.Remove(area, out var entry)) return;
        entry.Timer.Stop();
        entry.Bubble.BeginAnimation(OpacityProperty, null);
        entry.Shift.BeginAnimation(TranslateTransform.XProperty, null);
        entry.Shift.BeginAnimation(TranslateTransform.YProperty, null);
        Children.Remove(entry.Bubble);
    }
    public void Clear() { foreach (var area in _entries.Keys.ToArray()) Dismiss(area); }
}

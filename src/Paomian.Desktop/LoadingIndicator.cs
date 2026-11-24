using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Paomian.Desktop;

/// <summary>同一 WPF 时钟驱动头尾，只重绘固定大小的指示器。</summary>
public sealed class LoadingIndicator : Control
{
    private static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        "Phase", typeof(double), typeof(LoadingIndicator), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty MotionEnabledProperty = DependencyProperty.Register(
        "MotionEnabled", typeof(bool), typeof(LoadingIndicator), new PropertyMetadata(false, (d, _) => ((LoadingIndicator)d).UpdateClock()));
    private static readonly DoubleAnimation Cycle = CreateCycle();
    private bool _running;
    internal bool Circular { get; init; }

    public LoadingIndicator()
    {
        Focusable = false;
        IsHitTestVisible = false;
        SetResourceReference(BackgroundProperty, "Line");
        SetResourceReference(MotionEnabledProperty, SystemParameters.ClientAreaAnimationKey);
        Loaded += (_, _) => UpdateClock();
        Unloaded += (_, _) => StopClock();
        IsVisibleChanged += (_, _) => UpdateClock();
    }

    private static DoubleAnimation CreateCycle()
    {
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1500)) { RepeatBehavior = RepeatBehavior.Forever };
        animation.Freeze();
        return animation;
    }

    private void UpdateClock()
    {
        if (!IsLoaded || !IsVisible || !(bool)GetValue(MotionEnabledProperty)) { StopClock(); return; }
        if (_running) return;
        _running = true;
        BeginAnimation(PhaseProperty, Cycle);
    }

    private void StopClock()
    {
        if (!_running) return;
        _running = false;
        BeginAnimation(PhaseProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var phase = (double)GetValue(PhaseProperty);
        if (Circular)
        {
            var diameter = Math.Min(size.Width, size.Height);
            var stroke = Math.Max(1, diameter * .095);
            var radius = (diameter - stroke) / 2;
            if (radius <= 0) return;
            var center = new Point(size.Width / 2, size.Height / 2);
            dc.DrawEllipse(null, new Pen(Background, stroke), center, radius, radius);
            var sweep = 30 + 105 * (1 - Math.Cos(phase * 2 * Math.PI));
            var middle = phase * 360 - 90;
            Point OnCircle(double angle)
            {
                var radians = angle * Math.PI / 180;
                return center + new Vector(Math.Cos(radians) * radius, Math.Sin(radians) * radius);
            }
            var arc = new StreamGeometry();
            using (var path = arc.Open())
            {
                path.BeginFigure(OnCircle(middle - sweep / 2), false, false);
                path.ArcTo(OnCircle(middle + sweep / 2), new Size(radius, radius), 0,
                    sweep > 180, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, new Pen(Foreground, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, arc);
        }
        else
        {
            dc.DrawRectangle(Background, null, new Rect(size));
            // 每段从零长度进入，在另一端缩至零。第二段错开半个周期。
            void Segment(double time)
            {
                var head = Smooth(time / .72);
                var tail = Smooth((time - .28) / .72);
                dc.DrawRectangle(Foreground, null, new Rect(tail * size.Width, 0, (head - tail) * size.Width, size.Height));
            }
            Segment(phase);
            Segment((phase + .5) % 1);
        }
    }

    private static double Smooth(double time)
    {
        time = Math.Clamp(time, 0, 1);
        return time * time * (3 - 2 * time);
    }
}

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
    private static readonly DependencyProperty SecondPhaseProperty = DependencyProperty.Register(
        "SecondPhase", typeof(double), typeof(LoadingIndicator), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DoubleAnimation CircularCycle = CreateCycle(TimeSpan.FromMilliseconds(1400));
    private static readonly DoubleAnimation LinearCycle = CreateCycle(TimeSpan.FromMilliseconds(2100));
    private static readonly DoubleAnimation LinearSecondCycle = CreateCycle(TimeSpan.FromMilliseconds(2100), TimeSpan.FromMilliseconds(1150));
    private bool _running;
    private bool MotionEnabled => TryFindResource(SystemParameters.ClientAreaAnimationKey) is bool enabled
        ? enabled
        : SystemParameters.ClientAreaAnimation;
    internal bool Circular { get; init; }

    public LoadingIndicator()
    {
        Focusable = false;
        IsHitTestVisible = false;
        SetResourceReference(BackgroundProperty, "Line");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateClock();
    }

    private static DoubleAnimation CreateCycle(TimeSpan duration, TimeSpan? beginTime = null)
    {
        var animation = new DoubleAnimation(0, 1, duration)
        {
            BeginTime = beginTime,
            RepeatBehavior = RepeatBehavior.Forever
        };
        animation.Freeze();
        return animation;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateClock();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        StopClock();
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SystemParameters.ClientAreaAnimation)) return;
        UpdateClock();
        InvalidateVisual();
    }

    private void UpdateClock()
    {
        if (!IsLoaded || !IsVisible || !MotionEnabled) { StopClock(); return; }
        if (_running) return;
        _running = true;
        BeginAnimation(PhaseProperty, Circular ? CircularCycle : LinearCycle);
        BeginAnimation(SecondPhaseProperty, Circular ? null : LinearSecondCycle);
    }

    private void StopClock()
    {
        if (!_running) return;
        _running = false;
        BeginAnimation(PhaseProperty, null);
        BeginAnimation(SecondPhaseProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var phase = (double)GetValue(PhaseProperty);
        var motionEnabled = MotionEnabled;
        if (Circular)
        {
            DrawCircular(dc, size, phase, motionEnabled);
        }
        else
        {
            DrawLinear(dc, size, phase, (double)GetValue(SecondPhaseProperty), motionEnabled);
        }
    }

    private void DrawCircular(DrawingContext dc, Size size, double phase, bool motionEnabled)
    {
        const double viewBoxSize = 44;
        const double strokeWidth = 3.6;
        const double radius = (viewBoxSize - strokeWidth) / 2;
        const double circumference = 2 * Math.PI * radius;
        var diameter = Math.Min(size.Width, size.Height);
        if (diameter <= 0) return;

        var scale = diameter / viewBoxSize;
        dc.PushTransform(new ScaleTransform(scale, scale));
        var center = new Point(viewBoxSize / 2, viewBoxSize / 2);
        dc.DrawEllipse(null, new Pen(Background, strokeWidth), center, radius, radius);

        var frame = MuiAnimationMath.CircularFrame(phase, !motionEnabled);
        dc.PushTransform(new RotateTransform(frame.RotationDegrees, center.X, center.Y));
        var startRadians = -frame.DashOffset / circumference * 2 * Math.PI;
        var endRadians = startRadians + frame.DashLength / circumference * 2 * Math.PI;
        Point OnCircle(double radians) => new(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(OnCircle(startRadians), false, false);
            context.ArcTo(OnCircle(endRadians), new Size(radius, radius), 0,
                frame.DashLength > circumference / 2, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Foreground, strokeWidth), geometry);
        dc.Pop();
        dc.Pop();
    }

    private void DrawLinear(DrawingContext dc, Size size, double phase, double secondPhase, bool motionEnabled)
    {
        var bounds = new Rect(size);
        dc.PushClip(new RectangleGeometry(bounds));
        dc.DrawRectangle(Background, null, bounds);
        if (!motionEnabled)
        {
            dc.DrawRectangle(Foreground, null,
                new Rect(bounds.Width * .30, 0, bounds.Width * .40, bounds.Height));
            dc.Pop();
            return;
        }

        DrawSegment(dc, bounds.Width, MuiAnimationMath.FirstLinearSegment(phase), bounds.Height);
        DrawSegment(dc, bounds.Width, MuiAnimationMath.SecondLinearSegment(secondPhase), bounds.Height);
        dc.Pop();
    }

    private void DrawSegment(DrawingContext dc, double width, MuiLinearFrame frame, double height)
    {
        var segmentWidth = width * (1 - frame.Left - frame.Right);
        if (segmentWidth > 0)
            dc.DrawRectangle(Foreground, null, new Rect(frame.Left * width, 0, segmentWidth, height));
    }

}

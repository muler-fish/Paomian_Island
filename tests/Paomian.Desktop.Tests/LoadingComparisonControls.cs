using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Paomian.Desktop.Tests;

internal abstract class TimedComparisonElement : FrameworkElement
{
    protected static readonly DependencyProperty PhaseProperty = DependencyProperty.Register(
        nameof(Phase), typeof(double), typeof(TimedComparisonElement),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    protected static readonly DependencyProperty SecondPhaseProperty = DependencyProperty.Register(
        nameof(SecondPhase), typeof(double), typeof(TimedComparisonElement),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActivityBrushProperty = DependencyProperty.Register(
        nameof(ActivityBrush), typeof(Brush), typeof(TimedComparisonElement),
        new FrameworkPropertyMetadata(Brushes.SeaGreen, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(TimedComparisonElement),
        new FrameworkPropertyMetadata(Brushes.LightGray, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly bool _respectSystemMotion;
    private Storyboard? _storyboard;

    protected TimedComparisonElement(bool respectSystemMotion = true)
    {
        _respectSystemMotion = respectSystemMotion;
        Focusable = false;
        IsHitTestVisible = false;
        SetResourceReference(ActivityBrushProperty, "Accent");
        SetResourceReference(TrackBrushProperty, "Line");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateAnimation();
    }

    public Brush ActivityBrush
    {
        get => (Brush)GetValue(ActivityBrushProperty);
        set => SetValue(ActivityBrushProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    protected double Phase => (double)GetValue(PhaseProperty);
    protected double SecondPhase => (double)GetValue(SecondPhaseProperty);
    protected abstract Storyboard CreateStoryboard();

    protected void AddAnimation(Storyboard storyboard, DependencyProperty property, Timeline animation)
    {
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_respectSystemMotion)
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateAnimation();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_respectSystemMotion)
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        StopAnimation();
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            UpdateAnimation();
    }

    private void UpdateAnimation()
    {
        if (!IsLoaded || !IsVisible || _respectSystemMotion && !SystemParameters.ClientAreaAnimation)
        {
            StopAnimation();
            return;
        }

        if (_storyboard is not null)
            return;

        _storyboard = CreateStoryboard();
        _storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }

    private void StopAnimation()
    {
        if (_storyboard is null)
            return;

        _storyboard.Remove(this);
        _storyboard = null;
        SetValue(PhaseProperty, 0d);
        SetValue(SecondPhaseProperty, 0d);
    }

    protected void DrawTrack(DrawingContext drawingContext, Rect bounds, double thickness)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        var pen = new Pen(TrackBrush, thickness);
        drawingContext.DrawEllipse(null, pen, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
            Math.Max(0, (bounds.Width - thickness) / 2), Math.Max(0, (bounds.Height - thickness) / 2));
    }

    protected void DrawClippedTrack(DrawingContext drawingContext, Rect bounds)
    {
        drawingContext.PushClip(new RectangleGeometry(bounds));
        drawingContext.DrawRectangle(TrackBrush, null, bounds);
    }

    protected void AddLoop(Storyboard storyboard, DependencyProperty property, TimeSpan duration, TimeSpan? beginTime = null)
    {
        var animation = new DoubleAnimation(0, 1, duration)
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        if (beginTime is TimeSpan delay)
            animation.BeginTime = delay;
        Storyboard.SetTarget(animation, this);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }
}
internal sealed class PreviousLoadingComparisonIndicator : TimedComparisonElement
{
    internal bool IsCircular { get; init; }

    protected override Storyboard CreateStoryboard()
    {
        var storyboard = new Storyboard();
        AddLoop(storyboard, PhaseProperty, TimeSpan.FromMilliseconds(1500));
        return storyboard;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        if (IsCircular)
        {
            var diameter = Math.Min(bounds.Width, bounds.Height);
            var stroke = Math.Max(1, diameter * .095);
            var radius = (diameter - stroke) / 2;
            var center = new Point(bounds.Width / 2, bounds.Height / 2);
            drawingContext.DrawEllipse(null, new Pen(TrackBrush, stroke), center, radius, radius);
            var sweep = 30 + 105 * (1 - Math.Cos(Phase * 2 * Math.PI));
            var middle = Phase * 360 - 90;
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
            drawingContext.DrawGeometry(null, new Pen(ActivityBrush, stroke)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            }, arc);
            return;
        }

        DrawClippedTrack(drawingContext, bounds);
        Segment(Phase);
        Segment((Phase + .5) % 1);
        drawingContext.Pop();

        void Segment(double time)
        {
            var head = Smooth(time / .72);
            var tail = Smooth((time - .28) / .72);
            drawingContext.DrawRectangle(ActivityBrush, null,
                new Rect(tail * bounds.Width, 0, (head - tail) * bounds.Width, bounds.Height));
        }
    }

    private static double Smooth(double time)
    {
        time = Math.Clamp(time, 0, 1);
        return time * time * (3 - 2 * time);
    }
}

/*
 * Ported for comparison from MUI v9.3.1:
 * packages/mui-material/src/CircularProgress/CircularProgress.js
 * packages/mui-material/src/LinearProgress/LinearProgress.js
 * Copyright (c) 2014 Call-Em-All. MIT License, see licenses/MUI-MIT.txt.
 */
internal sealed class MuiCircularComparisonIndicator : TimedComparisonElement
{
    private const double ViewBoxSize = 44;
    private const double StrokeWidth = 3.6;
    private const double Radius = (ViewBoxSize - StrokeWidth) / 2;
    private const double Circumference = 2 * Math.PI * Radius;

    protected override Storyboard CreateStoryboard()
    {
        var storyboard = new Storyboard();
        AddLoop(storyboard, PhaseProperty, TimeSpan.FromMilliseconds(1400));
        return storyboard;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var diameter = Math.Min(ActualWidth, ActualHeight);
        var scale = diameter / ViewBoxSize;
        drawingContext.PushTransform(new ScaleTransform(scale, scale));

        var center = new Point(ViewBoxSize / 2, ViewBoxSize / 2);
        DrawTrack(drawingContext, new Rect(0, 0, ViewBoxSize, ViewBoxSize), StrokeWidth);

        var reducedMotion = !SystemParameters.ClientAreaAnimation;
        var frame = MuiAnimationMath.CircularFrame(Phase, reducedMotion);
        drawingContext.PushTransform(new RotateTransform(frame.RotationDegrees, center.X, center.Y));
        var startRadians = -frame.DashOffset / Circumference * 2 * Math.PI;
        var endRadians = startRadians + frame.DashLength / Circumference * 2 * Math.PI;
        var start = new Point(center.X + Radius * Math.Cos(startRadians), center.Y + Radius * Math.Sin(startRadians));
        var end = new Point(center.X + Radius * Math.Cos(endRadians), center.Y + Radius * Math.Sin(endRadians));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.ArcTo(end, new Size(Radius, Radius), 0, frame.DashLength > Circumference / 2,
                SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(ActivityBrush, StrokeWidth), geometry);
        drawingContext.Pop();
        drawingContext.Pop();
    }
}

internal sealed class MuiLinearComparisonIndicator : TimedComparisonElement
{
    private static readonly TimeSpan CycleDuration = TimeSpan.FromMilliseconds(2100);

    protected override Storyboard CreateStoryboard()
    {
        var storyboard = new Storyboard();
        AddLoop(storyboard, PhaseProperty, CycleDuration);
        AddLoop(storyboard, SecondPhaseProperty, CycleDuration, TimeSpan.FromMilliseconds(1150));
        return storyboard;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        DrawClippedTrack(drawingContext, bounds);
        var reducedMotion = !SystemParameters.ClientAreaAnimation;
        if (reducedMotion)
        {
            drawingContext.DrawRectangle(ActivityBrush, null,
                new Rect(bounds.Width * 0.30, 0, bounds.Width * 0.40, bounds.Height));
            drawingContext.Pop();
            return;
        }

        DrawSegment(drawingContext, bounds.Width, MuiAnimationMath.FirstLinearSegment(Phase), bounds.Height);
        DrawSegment(drawingContext, bounds.Width, MuiAnimationMath.SecondLinearSegment(SecondPhase), bounds.Height);
        drawingContext.Pop();
    }

    private void DrawSegment(DrawingContext drawingContext, double width, MuiLinearFrame frame, double height)
    {
        var segmentWidth = width * (1 - frame.Left - frame.Right);
        if (segmentWidth > 0)
            drawingContext.DrawRectangle(ActivityBrush, null, new Rect(frame.Left * width, 0, segmentWidth, height));
    }
}

/*
 * Ported for comparison from MaterialDesignInXAMLToolkit v5.3.2:
 * src/MaterialDesignThemes.Wpf/Themes/MaterialDesignTheme.ProgressBar.xaml
 * and Converters/CircularProgressBar/ArcEndPointConverter.cs.
 * Copyright (c) James Willock, Mulholland Software and Contributors.
 * MIT License, see licenses/MaterialDesignInXAML-MIT.txt.
 */
internal sealed class MaterialDesignCircularComparisonIndicator : TimedComparisonElement
{
    private static readonly DependencyProperty SweepProgressProperty = DependencyProperty.Register(
        "SweepProgress", typeof(double), typeof(MaterialDesignCircularComparisonIndicator),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly DependencyProperty RotationProperty = DependencyProperty.Register(
        "Rotation", typeof(double), typeof(MaterialDesignCircularComparisonIndicator),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    protected override Storyboard CreateStoryboard()
    {
        var storyboard = new Storyboard();
        var sweep = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        sweep.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        sweep.KeyFrames.Add(new SplineDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(1))));
        sweep.KeyFrames.Add(new SplineDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4))));
        AddAnimation(storyboard, SweepProgressProperty, sweep);
        AddLoop(storyboard, RotationProperty, TimeSpan.FromMilliseconds(1250), null);
        var rotation = storyboard.Children[^1] as DoubleAnimation;
        if (rotation is not null)
        {
            rotation.From = 0;
            rotation.To = 359;
        }
        return storyboard;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var diameter = Math.Min(ActualWidth, ActualHeight);
        if (diameter <= 0)
            return;

        const double stroke = 3;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Max(0, (diameter - 4) / 2);
        var bounds = new Rect(center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
        DrawTrack(drawingContext, bounds, stroke);
        drawingContext.PushTransform(new RotateTransform((double)GetValue(RotationProperty), center.X, center.Y));

        var percent = Math.Clamp((double)GetValue(SweepProgressProperty), 0, 1);
        if (percent > 0.0001 && radius > 0)
        {
            Point At(double fraction)
            {
                var radians = fraction * 2 * Math.PI;
                return new Point(center.X + Math.Sin(radians) * radius, center.Y - Math.Cos(radians) * radius);
            }

            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(At(0), false, false);
                context.ArcTo(At(percent / 2), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
                context.ArcTo(At(percent), new Size(radius, radius), 0, false, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            drawingContext.DrawGeometry(null, new Pen(ActivityBrush, stroke), geometry);
        }

        drawingContext.Pop();
    }
}

internal sealed class MaterialDesignLinearComparisonIndicator : TimedComparisonElement
{
    protected override Storyboard CreateStoryboard()
    {
        var storyboard = new Storyboard();
        AddLoop(storyboard, PhaseProperty, TimeSpan.FromSeconds(2));
        return storyboard;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;

        DrawClippedTrack(drawingContext, bounds);
        var phase = Math.Clamp(Phase, 0, 1);
        var transformOriginX = -0.5 + 2 * phase;
        var segmentWidth = bounds.Width * 0.25;
        var segmentLeft = transformOriginX * bounds.Width - transformOriginX * segmentWidth;
        drawingContext.DrawRectangle(ActivityBrush, null, new Rect(segmentLeft, 0, segmentWidth, bounds.Height));
        drawingContext.Pop();
    }
}

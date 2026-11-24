using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using ProgressRing = Wpf.Ui.Controls.ProgressRing;

namespace Paomian.Desktop;

/// <summary>保留图片加载占位，仅在仍在等待且与视口相交时创建动画。</summary>
internal sealed class ImageLoading : Grid
{
    private readonly double _diameter;
    private ScrollViewer? _viewer;
    private FrameworkElement? _viewport;
    private int _generation;
    private bool _ready;

    internal ImageLoading(double width, double height, double diameter = 22)
    {
        Width = width; Height = height;
        _diameter = Math.Min(diameter, Math.Min(width, height));
        IsHitTestVisible = false;
        AutomationProperties.SetName(this, "正在加载图片");
        Loaded += Start;
        Unloaded += (_, _) =>
        {
            ++_generation; _ready = false;
            if (_viewer != null) _viewer.ScrollChanged -= Scrolled;
            SystemParameters.StaticPropertyChanged -= AnimationChanged;
            _viewer = null; _viewport = null; Children.Clear();
        };
        IsVisibleChanged += (_, _) => Refresh();
    }

    private async void Start(object sender, RoutedEventArgs e)
    {
        var generation = ++_generation;
        await Task.Delay(160);
        if (generation != _generation || !IsLoaded) return;
        for (var node = VisualTreeHelper.GetParent(this); node != null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ScrollContentPresenter presenter) _viewport = presenter;
            if (node is ScrollViewer viewer)
            {
                _viewer = viewer; _viewport ??= viewer;
                viewer.ScrollChanged += Scrolled;
                break;
            }
        }
        SystemParameters.StaticPropertyChanged += AnimationChanged;
        _ready = true; Refresh();
    }
    private void Scrolled(object sender, ScrollChangedEventArgs e) => Refresh();
    private void AnimationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) Refresh();
    }
    private void Refresh()
    {
        if (!_ready) return;
        if (!IsVisible || (_viewport != null && (!IsDescendantOf(_viewport) ||
            !TransformToAncestor(_viewport).TransformBounds(new Rect(RenderSize)).IntersectsWith(new Rect(_viewport.RenderSize)))))
        {
            if (Children.Count != 0) Children.Clear();
            return;
        }
        var animate = SystemParameters.ClientAreaAnimation;
        if (Children.Count > 0 && (Children[0] is ProgressRing) == animate) return;
        Children.Clear();
        if (animate)
        {
            var ring = new ProgressRing { Width = _diameter, Height = _diameter, IsIndeterminate = true,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                CoverRingVisibility = Visibility.Collapsed };
            ring.SetResourceReference(ProgressRing.ForegroundProperty, "Accent");
            Children.Add(ring);
        }
        else
        {
            var label = new TextBlock { Text = "加载中", FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Children.Add(label);
        }
    }
}

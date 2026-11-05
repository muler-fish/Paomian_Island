using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using XamlAnimatedGif;

namespace Tide.Desktop;

/// <summary>为一个 Image 管理 GIF 的流、播放和可见性；下载仍由 ImageService 负责。</summary>
internal sealed class GifPlayback : IDisposable
{
    private readonly Image _image;
    private readonly MemoryStream _stream;
    private readonly Action _failed;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScrollViewer? _scroll;
    private bool _disposed;

    internal GifPlayback(Image image, byte[] bytes, Action failed)
    {
        _image = image; _stream = new(bytes, writable: false); _failed = failed;
    }

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        AnimationBehavior.AddLoadedHandler(_image, AnimationLoaded);
        AnimationBehavior.AddErrorHandler(_image, AnimationError);
        _image.Loaded += ImageLoaded;
        _image.Unloaded += ImageUnloaded;
        _image.IsVisibleChanged += VisibilityChanged;
        _image.SizeChanged += SizeChanged;
        // 逐帧合成，不缓存整段动画的所有解码帧；沿用文件声明的帧间隔和循环次数。
        AnimationBehavior.SetCacheFramesInMemory(_image, false);
        AnimationBehavior.SetSourceStream(_image, _stream);
        if (_image.IsLoaded) WatchViewport();
        try
        {
            await _ready.Task.WaitAsync(cancellationToken);
            UpdatePlayback();
        }
        catch { Dispose(); throw; }
    }

    private void AnimationLoaded(object sender, RoutedEventArgs e) => _ready.TrySetResult();
    private void AnimationError(object sender, AnimationErrorEventArgs e)
    {
        e.Handled = true;
        if (_disposed) return;
        if (!_ready.TrySetException(new InvalidDataException("GIF 解码失败。", e.Exception)))
        {
            Dispose(); _failed();
        }
    }
    private void ImageLoaded(object sender, RoutedEventArgs e) { WatchViewport(); UpdatePlayback(); }
    private void ImageUnloaded(object sender, RoutedEventArgs e) => Dispose();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdatePlayback();
    private void SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePlayback();
    private void ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdatePlayback();

    private void WatchViewport()
    {
        if (_scroll is not null) return;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(_image); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroll)
            { _scroll = scroll; scroll.ScrollChanged += ScrollChanged; break; }
    }

    private void UpdatePlayback()
    {
        if (_disposed || AnimationBehavior.GetAnimator(_image) is not { } animator) return;
        var visible = _image.IsVisible && _image.IsLoaded && _image.ActualWidth > 0 && _image.ActualHeight > 0;
        // IsVisible 不包含 ScrollViewer 的裁切范围，必须额外检查视口。
        if (visible && _scroll is not null)
        {
            var bounds = _image.TransformToAncestor(_scroll).TransformBounds(new Rect(_image.RenderSize));
            visible = bounds.IntersectsWith(new Rect(_scroll.RenderSize));
        }
        if (!visible) animator.Pause();
        else if (animator.IsPaused && !animator.IsComplete) animator.Play();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _ready.TrySetCanceled();
        if (_scroll is not null) _scroll.ScrollChanged -= ScrollChanged;
        _scroll = null;
        _image.Loaded -= ImageLoaded; _image.Unloaded -= ImageUnloaded;
        _image.IsVisibleChanged -= VisibilityChanged; _image.SizeChanged -= SizeChanged;
        AnimationBehavior.RemoveLoadedHandler(_image, AnimationLoaded);
        AnimationBehavior.RemoveErrorHandler(_image, AnimationError);
        _image.ClearValue(AnimationBehavior.SourceStreamProperty);
        _stream.Dispose();
    }
}

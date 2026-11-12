using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Tide.Core;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Button = System.Windows.Controls.Button;

namespace Tide.Desktop;

/// <summary>独立看图窗口。到达末图自动续页，图库和阅读区分别维护位置，原图仍按需下载。</summary>
public sealed class ImageWindow : ChildWindow
{
    private readonly List<Post> _posts;
    private readonly ImagePageCursor? _pages;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Border _pageNotice = new() { CornerRadius = new(6), Padding = new(12, 6, 12, 6), Margin = new(40, 0, 40, 14),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
    private readonly TextBlock _pageText = new() { Text = "已到最后一张", MaxWidth = 300, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _pageProgress = new() { Height = 3, Margin = new(0, 0, 0, 6), Value = 50, Visibility = Visibility.Collapsed };
    private readonly Button _pageRetry;
    private readonly DispatcherTimer _endNoticeTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _atEnd;
    private bool _paging, _advanceAfterPage, _pageFailed, _pagePaused;
    private long _lastAutoPost;
    private readonly Func<string, CancellationToken, Task<byte[]>> _download;
    private Image _image = new() { Stretch = Stretch.Fill };
    private GifPlayback? _playback;
    private readonly FeedbackOverlay _imageFeedback = new();
    private readonly Action<BitmapSource> _copyImage;
    private readonly Action<byte[], long> _copyGif;
    private readonly Canvas _canvas = new() { ClipToBounds = true, Focusable = true };
    private readonly StackPanel _feedback = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _zoomText = new() { MinWidth = 60, FontSize = 16, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _previous, _next, _save, _retry;
    private readonly List<Button> _zoomButtons = [];
    // 原始字节只在此窗口临时保存：最多三张且总计不超过 32 MiB，关闭窗口即释放。
    private readonly Dictionary<string, byte[]> _cache = [];
    private readonly LinkedList<string> _recent = [];
    private CancellationTokenSource? _request;
    private byte[]? _bytes;
    private int _index, _generation, _rotation;
    private bool _closed, _fit = true, _dragging;
    private Point _lastPointer;
    private double _scale = 1, _x, _y;

    public ImageWindow(Post post) : this([post], post.Id) { }
    public ImageWindow(IEnumerable<Post> posts, long selectedId) : this(posts, selectedId, ImageService.BytesAsync) { }
    internal ImageWindow(IEnumerable<Post> posts, long selectedId, Func<string, CancellationToken, Task<byte[]>> download, ImagePageCursor? pages = null, Action<BitmapSource>? copyImage = null, Action<byte[], long>? copyGif = null) : base("图片", dialogChrome: false)
    {
        _posts = posts.DistinctBy(p => p.Id).ToList(); _pages = pages;
        if (_posts.Count == 0) throw new ArgumentException("图库不能为空。", nameof(posts));
        _index = Math.Max(0, Array.FindIndex(_posts.ToArray(), p => p.Id == selectedId));
        _download = download;
        _copyImage = copyImage ?? Clipboard.SetImage;
        _copyGif = copyGif ?? ((bytes, id) => GifClipboard.Copy(bytes, id));
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Tide;component/ImageViewerResources.xaml") });
        Width = 1000; Height = 780; MinWidth = 500; MinHeight = 400;
        var grid = new Grid();
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var viewport = new Grid { ClipToBounds = true };
        grid.Children.Add(viewport);
        _canvas.SetResourceReference(Panel.BackgroundProperty, "Canvas");
        _canvas.Children.Add(_image); viewport.Children.Add(_canvas);
        ConfigureImageMenu(_image);
        viewport.Children.Add(_feedback);
        _retry = ActionButton("重试", "重新加载原图", () =>
        {
            var url = _posts[_index].ImageUrl();
            if (url is not null) { _cache.Remove(url); _recent.Remove(url); ImageService.Forget(url); }
            _ = LoadCurrentAsync();
        });
        _retry.HorizontalAlignment = HorizontalAlignment.Center;
        _previous = IconButton("‹", SymbolRegular.ChevronLeft24, "上一张（←）", () => Move(-1));
        _next = IconButton("›", SymbolRegular.ChevronRight24, "下一张（→）", () => Move(1));
        foreach (var button in new[] { _previous, _next })
        { button.SetResourceReference(StyleProperty, "ImageNavigationButton"); button.Margin = new(20, 0, 20, 0); viewport.Children.Add(button); }
        _previous.HorizontalAlignment = HorizontalAlignment.Left; _next.HorizontalAlignment = HorizontalAlignment.Right;
        var pageBody = new StackPanel();
        _pageProgress.IsIndeterminate = SystemParameters.ClientAreaAnimation;
        _pageProgress.SetResourceReference(Control.ForegroundProperty, "Accent"); pageBody.Children.Add(_pageProgress);
        var pageRow = new StackPanel { Orientation = Orientation.Horizontal };
        pageRow.Children.Add(_pageText);
        _pageRetry = ActionButton("重试", "重试加载下一页图片", () => _ = LoadMoreImagesAsync(false));
        _pageRetry.Tag = "续页重试"; _pageRetry.Width = 76; _pageRetry.Margin = new(8, 0, 0, 0);
        _pageRetry.Visibility = Visibility.Collapsed; pageRow.Children.Add(_pageRetry);
        pageBody.Children.Add(pageRow); _pageNotice.Child = pageBody;
        _pageNotice.SetResourceReference(Border.BackgroundProperty, "Sidebar"); viewport.Children.Add(_pageNotice);
        AutomationProperties.SetLiveSetting(_pageText, AutomationLiveSetting.Polite);
        var tools = new Grid { Margin = new(20, 8, 20, 8), MaxWidth = 900 };
        tools.ColumnDefinitions.Add(new() { Width = new(2.5, GridUnitType.Star), MinWidth = 148 });
        for (var i = 0; i < 3; i++) tools.ColumnDefinitions.Add(new());
        var zoom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var minus = IconButton("−", SymbolRegular.ZoomOut24, "缩小（−）", () => ZoomAt(_scale / 1.25, Center)); zoom.Children.Add(minus);
        zoom.Children.Add(_zoomText);
        var plus = IconButton("+", SymbolRegular.ZoomIn24, "放大（+）", () => ZoomAt(_scale * 1.25, Center)); zoom.Children.Add(plus); tools.Children.Add(zoom);
        var actual = ActionButton("1:1", "1:1 / 适应窗口（1 / 0）", () => { if (_fit) ZoomAt(1, Center); else Fit(); });
        var ratio = new Border { BorderThickness = new(1), CornerRadius = new(2), Padding = new(2, 0, 2, 0), Child = new TextBlock { Text = "1:1", FontSize = 12 } };
        ratio.SetResourceReference(Border.BorderBrushProperty, "Text"); actual.Content = ratio;
        var rotate = IconButton("旋转", SymbolRegular.ArrowRotateCounterclockwise24, "向左旋转（R）", Rotate);
        _zoomButtons.AddRange([minus, plus, actual, rotate]);
        _save = IconButton("保存原图", SymbolRegular.ArrowDownload24, "保存原图", Save);
        var commands = new[] { actual, rotate, _save };
        for (var i = 0; i < commands.Length; i++) { Grid.SetColumn(commands[i], i + 1); tools.Children.Add(commands[i]); }
        var footer = new Border { Child = tools }; footer.SetResourceReference(Border.BackgroundProperty, "Sidebar");
        Grid.SetRow(footer, 1); grid.Children.Add(footer);
        Grid.SetRowSpan(_imageFeedback, 2); Panel.SetZIndex(_imageFeedback, 100); grid.Children.Add(_imageFeedback);
        SetClientContent(grid);
        _endNoticeTimer.Tick += (_, _) => { _endNoticeTimer.Stop(); UpdateNavigation(); };
        _canvas.SizeChanged += (_, _) => { if (_fit) Fit(); else Position(); };
        _canvas.PreviewMouseWheel += (_, e) => { ZoomAt(_scale * (e.Delta > 0 ? 1.25 : .8), e.GetPosition(_canvas)); e.Handled = true; };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            _canvas.Focus();
            if (_image.Source is null) return;
            if (e.ClickCount == 2) { if (_fit) ZoomAt(1, e.GetPosition(_canvas)); else Fit(); e.Handled = true; return; }
            _lastPointer = e.GetPosition(_canvas); _dragging = _canvas.CaptureMouse(); e.Handled = true;
        };
        _canvas.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var point = e.GetPosition(_canvas); PanBy(point.X - _lastPointer.X, point.Y - _lastPointer.Y); _lastPointer = point;
        };
        _canvas.MouseLeftButtonUp += (_, _) => _canvas.ReleaseMouseCapture();
        _canvas.LostMouseCapture += (_, _) => _dragging = false;
        PreviewKeyDown += ViewerKeyDown;
        Loaded += async (_, _) => { await LoadCurrentAsync(); if (!_closed && IsActive) _canvas.Focus(); };
        Closed += (_, _) => { _closed = true; _endNoticeTimer.Stop(); ++_generation; _lifetime.Cancel(); _lifetime.Dispose(); _request?.Cancel(); _request?.Dispose(); _playback?.Dispose(); _playback = null; _cache.Clear(); _recent.Clear(); _bytes = null; _image.Source = null; };
    }

    private void ConfigureImageMenu(Image target)
    {
        var menu = new ContextMenu(); var copy = new MenuItem { Header = "复制" }; menu.Items.Add(copy);
        target.ContextMenu = menu;
        BitmapSource? copyTarget = null; byte[]? gifTarget = null; long copyPostId = 0; Point? copyOrigin = null;
        menu.Opened += (_, _) =>
        {
            // 捕获打开菜单时的原文件，GIF 不经过位图转换，缩放或旋转也不改变复制的动画。
            gifTarget = _bytes is { } bytes && XIslandApi.DetectImage(bytes) == "image/gif" ? bytes : null;
            copyPostId = _posts[_index].Id;
            copyTarget = gifTarget is null ? (target.Source as BitmapSource)?.CloneCurrentValue() : null; copyTarget?.Freeze();
            copy.IsEnabled = gifTarget is not null || copyTarget is not null; copyOrigin = Mouse.GetPosition(_imageFeedback);
        };
        target.ContextMenuOpening += (_, e) => { if (target.Source is null) e.Handled = true; };
        copy.Click += (_, _) => { if (gifTarget is not null || copyTarget is not null) CopyDisplayedImage(copyTarget, gifTarget, copyPostId, copyOrigin); };
        menu.Closed += (_, _) => { copyTarget = null; gifTarget = null; copyOrigin = null; };
    }

    private static Button ActionButton(string text, string hint, Action action)
    {
        var b = new Button { Content = text, Tag = text, ToolTip = hint };
        b.SetResourceReference(StyleProperty, "ImageToolButton");
        AutomationProperties.SetName(b, hint); b.Click += (_, _) => action(); return b;
    }
    private static Button IconButton(string id, SymbolRegular symbol, string hint, Action action)
    { var b = ActionButton(id, hint, action); b.Content = new SymbolIcon(symbol) { FontSize = 22 }; return b; }
    private Point Center => new(_canvas.ActualWidth / 2, _canvas.ActualHeight / 2);

    private async Task LoadCurrentAsync()
    {
        _request?.Cancel(); _request?.Dispose(); _request = new(); var token = _request.Token; var generation = ++_generation;
        _canvas.ReleaseMouseCapture(); _bytes = null; _rotation = 0; _save.IsEnabled = false;
        _playback?.Dispose(); _playback = null;
        _image.ContextMenu.IsOpen = false; _canvas.Children.Remove(_image);
        // 每次切图拥有独立播放实例，旧解码任务不会把迟到帧或错误写入新图片。
        _image = new() { Stretch = Stretch.Fill }; ConfigureImageMenu(_image); _canvas.Children.Add(_image);
        foreach (var button in _zoomButtons) button.IsEnabled = false;
        _zoomText.Text = ""; UpdateNavigation();
        var post = _posts[_index];
        // 每次抵达一个新的末图只自动尝试一次，失败或连续无图时由用户重试。
        if (_index == _posts.Count - 1 && _pages?.HasMore == true && !_paging && !_pageFailed && !_pagePaused && _lastAutoPost != post.Id)
        { _lastAutoPost = post.Id; _ = LoadMoreImagesAsync(false); }
        _feedback.Children.Clear(); _feedback.Children.Add(new ImageLoading(144, 40));
        _message.Text = "正在加载原图"; _feedback.Children.Add(_message); _feedback.Visibility = Visibility.Visible;
        try
        {
            var url = post.ImageUrl() ?? throw new InvalidDataException("这条发言没有可查看的图片。");
            var bytes = _cache.TryGetValue(url, out var cached) ? cached : await _download(url, token);
            if (generation != _generation || token.IsCancellationRequested || _closed) return;
            Remember(url, bytes); _bytes = bytes; _save.IsEnabled = true;
            // 图片解码不占用界面线程；切图和关闭均可中断等待，迟到结果不覆盖新图。
            var decoded = await Task.Run(() => ImageService.DecodeImage(bytes), token);
            if (generation != _generation || token.IsCancellationRequested || _closed) return;
            var bitmap = decoded.Poster;
            _image.Source = bitmap; _image.Width = bitmap.PixelWidth; _image.Height = bitmap.PixelHeight;
            if (decoded.Gif is not null)
            {
                var playback = new GifPlayback(_image, decoded.Gif, () => { if (generation == _generation && !_closed) ShowImageError(); });
                _playback = playback; await playback.StartAsync(token);
                if (generation != _generation || token.IsCancellationRequested || _closed) return;
                _image.Width = BitConverter.ToUInt16(bytes, 6); _image.Height = BitConverter.ToUInt16(bytes, 8);
            }
            _feedback.Visibility = Visibility.Collapsed;
            _zoomText.ToolTip = $"{bitmap.PixelWidth} × {bitmap.PixelHeight}";
            UpdateNavigation();
            foreach (var button in _zoomButtons) button.IsEnabled = true;
            Fit();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (generation != _generation || _closed) return;
            ShowImageError();
        }
    }

    private void ShowImageError()
    {
        _feedback.Children.Clear(); _message.Text = _bytes is null ? "图片加载失败" : "无法预览，可保存原图";
        _feedback.Children.Add(_message); _feedback.Children.Add(_retry); _feedback.Visibility = Visibility.Visible;
        foreach (var button in _zoomButtons) button.IsEnabled = false;
    }

    private void Remember(string url, byte[] bytes)
    {
        _cache[url] = bytes; _recent.Remove(url); _recent.AddLast(url);
        while (_cache.Count > 3 || _cache.Values.Sum(b => (long)b.Length) > 32 * 1024 * 1024)
        { var oldest = _recent.First!.Value; _recent.RemoveFirst(); _cache.Remove(oldest); }
    }
    private void Move(int delta)
    {
        var target = _index + delta;
        if (_closed || target < 0) return;
        if (target >= _posts.Count)
        { if (delta > 0 && _pages?.HasMore == true) _ = LoadMoreImagesAsync(true); return; }
        _advanceAfterPage = false;
        _index = target; _ = LoadCurrentAsync();
    }
    private void UpdateNavigation()
    {
        _previous.IsEnabled = _index > 0;
        _next.IsEnabled = _index < _posts.Count - 1 || _pages?.HasMore == true;
        var tail = _index == _posts.Count - 1;
        var hint = tail && _pages?.HasMore == true ? (_pageFailed ? "重试加载下一页（→）" : "加载下一页图片（→）") : "下一张（→）";
        _next.ToolTip = hint; AutomationProperties.SetName(_next, hint);
        Title = $"{_posts[_index].Number}  ·  {_index + 1} / {_posts.Count}" + (_pages?.HasMore == true ? "+" : "")
            + (_posts[_index].Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ? "  ·  GIF" : "");
        // End-of-gallery is a transient hint, not a persistent paging action. Start once per arrival;
        // repeated navigation refreshes must not re-show it after expiry or hide loading/retry controls.
        var atEnd = tail && _pages is { HasMore: false } && !_paging && !_pageFailed && !_pagePaused && _image.Source is not null;
        if (atEnd && !_atEnd) _endNoticeTimer.Start();
        else if (!atEnd) _endNoticeTimer.Stop();
        _atEnd = atEnd;
        _pageNotice.Visibility = tail && (_paging || _pageFailed || _pagePaused || _endNoticeTimer.IsEnabled)
            ? Visibility.Visible : Visibility.Collapsed;
    }
    private async Task LoadMoreImagesAsync(bool advance)
    {
        if (_closed || _pages?.HasMore != true) return;
        _advanceAfterPage |= advance;
        if (_paging) return; // 连点或按住右方向键只保留一次切图意图。
        _paging = true; _pageFailed = _pagePaused = false;
        _pageProgress.Visibility = Visibility.Visible; _pageRetry.Visibility = Visibility.Collapsed;
        _pageText.Text = "正在加载下一页图片"; UpdateNavigation();
        var token = _lifetime.Token; var added = 0;
        try
        {
            // 自动跳过无图页，单轮最多五页，避免图片稀少的大串被后台读穿。
            for (var attempt = 0; attempt < 5 && _pages.HasMore && added == 0; attempt++)
            {
                var batch = await _pages.NextAsync(token); token.ThrowIfCancellationRequested();
                if (_closed) return;
                var known = _posts.Select(p => p.Id).ToHashSet();
                var images = batch.Images.Where(p => known.Add(p.Id)).ToArray(); _posts.AddRange(images); added += images.Length;
                _pageText.Text = $"正在查找图片 · 第 {batch.Page} 页";
            }
            _pagePaused = added == 0 && _pages.HasMore;
            _pageText.Text = _pagePaused ? "这些页暂无图片" : "已到最后一张";
            _pageRetry.Content = "继续查找"; _pageRetry.Visibility = _pagePaused ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_closed) return;
            _pageFailed = true; _pageText.Text = ex is IslandException ? ex.Message : "下一页加载失败";
            _pageRetry.Content = "重试"; _pageRetry.Visibility = Visibility.Visible;
        }
        finally
        {
            _paging = false;
            if (!_closed)
            {
                _pageProgress.Visibility = Visibility.Collapsed;
                var move = _advanceAfterPage && added > 0; _advanceAfterPage = false;
                UpdateNavigation();
                if (move) Move(1);
            }
        }
    }
    private void Fit()
    {
        if (_image.Source is null) return;
        _fit = true;
        _scale = Math.Min(1, Math.Min(Math.Max(1, _canvas.ActualWidth - 24) / ImageWidth, Math.Max(1, _canvas.ActualHeight - 24) / ImageHeight));
        _x = (_canvas.ActualWidth - ImageWidth * _scale) / 2; _y = (_canvas.ActualHeight - ImageHeight * _scale) / 2;
        Position();
    }
    internal void ZoomAt(double scale, Point anchor)
    {
        if (_image.Source is null) return;
        var next = Math.Clamp(scale, .01, 8);
        _x = anchor.X - (anchor.X - _x) * next / _scale; _y = anchor.Y - (anchor.Y - _y) * next / _scale;
        _scale = next; _fit = false; Position();
    }
    internal void PanBy(double dx, double dy) { if (_image.Source is null) return; _x += dx; _y += dy; Position(); }
    private void Position()
    {
        if (_image.Source is null) return;
        // 小图始终居中，大图限制拖动边界，避免被拖出可视区后找不到。
        var width = ImageWidth * _scale; var height = ImageHeight * _scale;
        _x = width <= _canvas.ActualWidth ? (_canvas.ActualWidth - width) / 2 : Math.Clamp(_x, _canvas.ActualWidth - width, 0);
        _y = height <= _canvas.ActualHeight ? (_canvas.ActualHeight - height) / 2 : Math.Clamp(_y, _canvas.ActualHeight - height, 0);
        var matrix = _rotation switch
        {
            1 => new Matrix(0, _scale, -_scale, 0, _x + _image.Height * _scale, _y),
            2 => new Matrix(-_scale, 0, 0, -_scale, _x + width, _y + height),
            3 => new Matrix(0, -_scale, _scale, 0, _x, _y + _image.Width * _scale),
            _ => new Matrix(_scale, 0, 0, _scale, _x, _y)
        };
        _image.RenderTransform = new MatrixTransform(matrix);
        _zoomText.Text = $"{_scale * 100:0}%";
        _canvas.Cursor = width > _canvas.ActualWidth || height > _canvas.ActualHeight ? Cursors.SizeAll : Cursors.Arrow;
    }
    private double ImageWidth => _rotation % 2 == 0 ? _image.Width : _image.Height;
    private double ImageHeight => _rotation % 2 == 0 ? _image.Height : _image.Width;
    private void Rotate()
    {
        if (_image.Source is null) return;
        _rotation = (_rotation + 3) % 4;
        if (_fit) Fit();
        else { _x = (_canvas.ActualWidth - ImageWidth * _scale) / 2; _y = (_canvas.ActualHeight - ImageHeight * _scale) / 2; Position(); }
    }
    private void ViewerKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: Move(-1); break;
            case Key.Right: Move(1); break;
            case Key.Add: case Key.OemPlus: ZoomAt(_scale * 1.25, Center); break;
            case Key.Subtract: case Key.OemMinus: ZoomAt(_scale / 1.25, Center); break;
            case Key.D0: case Key.NumPad0: Fit(); break;
            case Key.D1: case Key.NumPad1: ZoomAt(1, Center); break;
            case Key.R: Rotate(); break;
            default: return;
        }
        e.Handled = true;
    }
    private void Save()
    {
        if (_bytes is null) return;
        var post = _posts[_index]; var bytes = _bytes;
        try
        {
            var picker = new SaveFileDialog { FileName = $"{post.Id}{post.Extension}", Filter = "图片文件|*" + post.Extension };
            if (picker.ShowDialog(this) == true) { File.WriteAllBytes(picker.FileName, bytes); Title = $"{post.Number}  ·  已保存"; }
        }
        catch (Exception) { Title = "保存失败，请重试"; }
    }
    private void CopyDisplayedImage(BitmapSource? bitmap, byte[]? gif, long postId, Point? origin)
    {
        try
        {
            if (gif is not null) _copyGif(gif, postId);
            else _copyImage(bitmap!);
            _imageFeedback.Show(new(gif is not null ? "GIF 已复制" : "图片已复制", FeedbackArea.General), _canvas, origin: origin);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        { _imageFeedback.Show(new("复制图片失败，剪贴板可能正被占用，请重试。", FeedbackArea.General, true), _canvas); }
    }
}


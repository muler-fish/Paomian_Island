using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;

namespace Tide.Desktop;

internal sealed record LoadedImage(BitmapSource Poster, byte[]? Gif = null);
// 解码器不支持时，看图窗口仍可提供已下载原文件的保存入口。
internal sealed record ImageFile(byte[] Bytes, LoadedImage? Image);

/// <summary>下载与解码图片；独立 HttpClient 不附带饼干，下载并发限制为 4。</summary>
public static class ImageService
{
    private static readonly HttpClient Http = NetworkClients.Create();
    internal static DiskCache? DiskCache { get; set; }
    internal static void ClearMemoryCache() => Cache.Clear();
    internal static void Forget(string url)
    {
        Cache.Remove(url);
        try { DiskCache?.Remove(ImageKey(url)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private static string ImageKey(string url) => "images/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".bin";
    private static readonly SemaphoreSlim Slots = new(4);
    private const int PreviewDecodeWidth = 700;
    internal static TimeSpan ScrollSettleDelay { get; set; } = TimeSpan.FromMilliseconds(120);
    // 已解码的缩略图缓存在内存，当前调用方在 WPF 界面线程上访问此字典。
    private static readonly Dictionary<string, LoadedImage> Cache = [];
    private static long CacheBytes => Cache.Values.Sum(i => (long)i.Poster.PixelWidth * i.Poster.PixelHeight * 4 + (i.Gif?.Length ?? 0));

    internal static Task<ImageFile> LoadOriginalAsync(string url, CancellationToken ct) => LoadAsync(Http, DiskCache, url, 0, ct);

    internal static async Task<ImageFile> LoadAsync(HttpClient http, DiskCache? cache, string url, int width, CancellationToken ct)
    {
        // 只请求已知图片站；调用方不能借此下载任意域名或本地协议的资源。
        var uri = new Uri(url);
        if (uri.Scheme != "https" || uri.Host != "image.nmb.best") throw new ArgumentException("不支持的图片地址。");
        var key = ImageKey(uri.AbsoluteUri);
        if (cache is not null && await Task.Run(() => cache.Read(key), ct) is { } saved)
        {
            var previous = await DecodeAsync(saved, width, ct);
            if (previous.Image is not null) return previous;
            // 旧版本可能保存了坏响应。允许重取并替换同一缓存，无需用户清空缓存。
        }
        var bytes = await DownloadAsync(http, uri, ct);
        var loaded = await DecodeAsync(bytes, width, ct);
        if (loaded.Image is not null && cache is not null) await Task.Run(() => cache.Write(key, bytes), ct);
        return loaded;
    }

    internal static async Task<ImageFile> DecodeAsync(byte[] bytes, int width, CancellationToken ct)
    {
        try
        {
            var image = await Task.Run(() => DecodeImage(bytes, width), ct);
            ct.ThrowIfCancellationRequested();
            return new(bytes, image);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException or InvalidDataException or IOException)
        {
            return new(bytes, null);
        }
    }

    private static async Task<byte[]> DownloadAsync(HttpClient http, Uri uri, CancellationToken ct)
    {
        await Slots.WaitAsync(ct);
        try
        {
            // ResponseHeadersRead 的 HttpClient 超时只到响应头。给整个下载设一个期限。
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(http.Timeout);
            var token = deadline.Token;
            try { return await ReadResponseAsync(http, uri, token); }
            catch (Exception ex) when (IsInterrupted(ex))
            {
                // 断流只重取一次，两次请求共用原下载时限。
                await Task.Delay(150, token);
                return await ReadResponseAsync(http, uri, token);
            }
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) { throw new TimeoutException("图片加载超时", ex); }
        finally { Slots.Release(); }
    }

    private static async Task<byte[]> ReadResponseAsync(HttpClient http, Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri)
        {
            Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        var mime = response.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mime, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mime, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mime, "application/json", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("收到的响应不是图片");
        const int limit = 20 * 1024 * 1024;
        // 同时检查响应声明和实际读取量，避免缺少 Content-Length 时绕过大小限制。
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("图片超过 20 MB，请在网页中查看。");
        using var input = await response.Content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + count > limit) throw new InvalidDataException("图片过大。");
            output.Write(buffer, 0, count);
        }
        token.ThrowIfCancellationRequested();
        if (output.Length == 0) throw new InvalidDataException("收到空的图片响应");
        return output.ToArray();
    }

    private static bool IsInterrupted(Exception error) => error is
        HttpRequestException { HttpRequestError: HttpRequestError.ResponseEnded or HttpRequestError.HttpProtocolError } or
        HttpIOException { HttpRequestError: HttpRequestError.ResponseEnded or HttpRequestError.HttpProtocolError };

    internal static async Task<LoadedImage> LoadPreviewAsync(string url, CancellationToken ct)
    {
        if (Cache.TryGetValue(url, out var cached)) return cached;
        // 内存里没有时稍等片刻再请求：快速滚过的条目在这段时间内被回收、取消，不会发出下载。
        await Task.Delay(ScrollSettleDelay, ct);
        // 列表缩略图限制解码宽度并最多保留 80 项，减少长期阅读时的内存占用。
        var loaded = await LoadAsync(Http, DiskCache, url, PreviewDecodeWidth, ct);
        var image = loaded.Image ?? throw new InvalidDataException("无法解码图片");
        ct.ThrowIfCancellationRequested();
        Cache[url] = image;
        while (Cache.Count > 80 || CacheBytes > 64L * 1024 * 1024) Cache.Remove(Cache.Keys.First());
        return image;
    }

    internal static async Task<BitmapSource?> ReadCachedThumbnailAsync(Post post, CancellationToken ct)
    {
        if (post.ImageUrl(thumbnail: true) is not { } url) return null;
        if (Cache.TryGetValue(url, out var cached)) return cached.Poster;
        var cache = DiskCache;
        if (cache is null || await Task.Run(() => cache.Read(ImageKey(url)), ct) is not { } bytes) return null;
        return (await DecodeAsync(bytes, PreviewDecodeWidth, ct)).Image?.Poster;
    }

    internal static string ErrorMessage(Exception? error) => error switch
    {
        TimeoutException => "图片加载超时",
        HttpRequestException { StatusCode: HttpStatusCode status } => $"图片加载失败（HTTP {(int)status}）",
        { } interrupted when IsInterrupted(interrupted) => "图片传输中断",
        InvalidDataException => error.Message.TrimEnd('。'),
        _ => "图片加载失败"
    };
    internal static LoadedImage DecodeImage(byte[] bytes, int width = 0)
    {
        var gif = XIslandApi.DetectImage(bytes) == "image/gif";
        // GIF 合成需完整画布；限制像素数量，避免压缩文件很小而解码占用失控。
        if (gif && (bytes.Length < 10 || (long)BitConverter.ToUInt16(bytes, 6) * BitConverter.ToUInt16(bytes, 8) > 16_777_216))
            throw new InvalidDataException("GIF 画布过大，无法播放。");
        return new(Decode(bytes, width), gif ? bytes : null);
    }
    public static BitmapSource Decode(byte[] bytes, int width = 0)
    {
        // OnLoad 确保离开此方法后可关闭流；Freeze 后图像不可变，可被多个控件安全复用。
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (width > 0) image.DecodePixelWidth = width;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }
}

/// <summary>串列表和正文共用的缩略图控件，处理无图模式、重试，以及虚拟化列表复用时的请求取消。</summary>
public sealed class PostImage : ContentControl
{
    public static readonly DependencyProperty PostProperty = DependencyProperty.Register(nameof(Post), typeof(Post), typeof(PostImage), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty ShowImagesProperty = DependencyProperty.Register(nameof(ShowImages), typeof(bool), typeof(PostImage), new PropertyMetadata(true, Changed));
    // 串列表选择「不显示」时为 true：整块收起，不请求也不留入口。
    public static readonly DependencyProperty SuppressedProperty = DependencyProperty.Register(nameof(Suppressed), typeof(bool), typeof(PostImage), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty PreviewHeightProperty = DependencyProperty.Register(nameof(PreviewHeight), typeof(double), typeof(PostImage), new PropertyMetadata(double.NaN, Changed));
    public static readonly DependencyProperty PreviewMaxWidthProperty = DependencyProperty.Register(nameof(PreviewMaxWidth), typeof(double), typeof(PostImage), new PropertyMetadata(440d, Changed));
    public Post? Post { get => (Post?)GetValue(PostProperty); set => SetValue(PostProperty, value); }
    public bool ShowImages { get => (bool)GetValue(ShowImagesProperty); set => SetValue(ShowImagesProperty, value); }
    public bool Suppressed { get => (bool)GetValue(SuppressedProperty); set => SetValue(SuppressedProperty, value); }
    // 串列表使用固定预留高度；正文保持按比例自然高度。尺寸都是 WPF 逻辑像素。
    public double PreviewHeight { get => (double)GetValue(PreviewHeightProperty); set => SetValue(PreviewHeightProperty, value); }
    public double PreviewMaxWidth { get => (double)GetValue(PreviewMaxWidthProperty); set => SetValue(PreviewMaxWidthProperty, value); }
    public static event Action<PostImage, Post>? ImageClicked;
    private readonly Func<string, CancellationToken, Task<LoadedImage>> _load;
    private GifPlayback? _playback;
    private CancellationTokenSource? _cts;
    private int _version;
    // 点击加载模式下用户已点开的图片地址；换绑到别的帖子后自然失效。
    private string? _requested;
    public PostImage() : this(ImageService.LoadPreviewAsync) { }
    internal PostImage(Func<string, CancellationToken, Task<BitmapSource>> load) : this(async (url, ct) => new LoadedImage(await load(url, ct))) { }
    internal PostImage(Func<string, CancellationToken, Task<LoadedImage>> load)
    {
        _load = load;
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => { _cts?.Cancel(); ++_version; _playback?.Dispose(); _playback = null; Content = null; };
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PostImage)d).Update();
    private async void Update()
    {
        // 同一个控件可能换绑到另一帖子；取消旧请求并更新版本，避免慢图显示到新帖子上。
        _cts?.Cancel(); _cts?.Dispose(); _cts = new(); var version = ++_version; var ct = _cts.Token;
        _playback?.Dispose(); _playback = null;
        // 站点缩略图可能只有首帧，GIF 预览通过同一缓存读取原文件。
        var post = Post; var url = post?.ImageUrl(!post.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase));
        Visibility = url is null || Suppressed ? Visibility.Collapsed : Visibility.Visible;
        if (url is null || post is null || Suppressed) { Content = null; return; }
        var show = ShowImages || _requested == url;
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left,
            Height = show ? PreviewHeight : double.NaN, Padding = new(4),
            Content = show ? new ImageLoading(double.IsNaN(PreviewHeight) ? 144 : 58, double.IsNaN(PreviewHeight) ? 92 : Math.Max(24, PreviewHeight - 12)) : "显示这张图片",
            ToolTip = show ? "点击查看原图" : "点击在此处加载这张图片"
        };
        System.Windows.Automation.AutomationProperties.SetName(button, $"查看 {post.Number} 的图片");
        Content = button;
        // 点击加载模式：第一次点击在原处加载缩略图，之后再点击打开看图窗口加载原图。
        void Fail(string message)
        {
            if (version != _version || Content != button) return;
            var retry = new Button { Content = "重试", ToolTip = $"{message}，点击重试", VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(retry, $"重新加载 {post.Number} 的图片");
            retry.Click += (_, _) => { ImageService.Forget(url); Update(); };
            var original = new Button { Content = "查看原图", Margin = new(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(original, $"查看 {post.Number} 的原图");
            original.Click += (_, _) => ImageClicked?.Invoke(this, post);
            Content = new StackPanel { Orientation = Orientation.Horizontal, Height = PreviewHeight, Children = { retry, original } };
        }
        button.Click += (_, _) =>
        {
            if (!show) { _requested = url; Update(); }
            else ImageClicked?.Invoke(this, post);
        };
        if (!show) return;
        if (!IsLoaded) return;
        try
        {
            var image = await _load(url, ct);
            if (version != _version || ct.IsCancellationRequested) return;
            var visual = new Image
            {
                Source = image.Poster, Stretch = Stretch.Uniform,
                MaxHeight = double.IsNaN(PreviewHeight) ? 260 : Math.Max(1, PreviewHeight - 10),
                MaxWidth = PreviewMaxWidth, HorizontalAlignment = HorizontalAlignment.Left
            };
            button.Content = visual;
            if (image.Gif is not null)
            {
                var playback = new GifPlayback(visual, image.Gif, () => Fail("无法预览图片")); _playback = playback;
                await playback.StartAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Fail(ImageService.ErrorMessage(ex));
        }
    }
}

/// <summary>轻量加载占位；关闭系统动画时显示静态进度，缓存命中时直接被图片替换。</summary>
internal sealed class ImageLoading : Grid
{
    internal ImageLoading(double width, double height)
    {
        Width = width; Height = height;
        var bar = new ProgressBar { Width = Math.Min(72, width - 12), Height = 3,
            IsIndeterminate = SystemParameters.ClientAreaAnimation, Value = 50, VerticalAlignment = VerticalAlignment.Center };
        bar.SetResourceReference(Control.ForegroundProperty, "Accent");
        Children.Add(bar);
        System.Windows.Automation.AutomationProperties.SetName(this, "正在加载图片");
    }
}

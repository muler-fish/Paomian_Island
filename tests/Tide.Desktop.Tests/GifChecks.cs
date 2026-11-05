using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;
using XamlAnimatedGif;

namespace Tide.Desktop.Tests;

internal static class GifChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var bytes = Fixture(loop: true);
        File.WriteAllBytes(Path.Combine(output, "disposal-fixture.gif"), bytes);
        var gif = ImageService.DecodeImage(bytes, 700);
        var post = new Post { Id = 801, ImagePath = "fixture/animated", Extension = ".gif" };
        var requested = new List<string>();
        Task<LoadedImage> Load(string url, CancellationToken ct) { requested.Add(url); return Task.FromResult(gif); }
        var preview = new PostImage(Load) { Post = post, PreviewHeight = 180, PreviewMaxWidth = 320 };
        var body = new PostImage(Load) { Post = post };
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = "串列表 GIF 预览" }); panel.Children.Add(preview);
        panel.Children.Add(new TextBlock { Text = "串内 GIF 图片" }); panel.Children.Add(body);
        panel.Children.Add(new Border { Height = 900 });
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var host = new Window { Owner = owner, ShowInTaskbar = false, Width = 560, Height = 520, Content = scroll };
        host.Show();
        try
        {
            await Wait(() => FindImage(preview)?.Source is WriteableBitmap && FindImage(body)?.Source is WriteableBitmap);
            Require(requested.Count == 2 && requested.All(u => u == post.ImageUrl()), "GIF 预览请求了静态缩略图地址");
            var image = FindImage(preview)!;
            await CheckFrames(image);
            await CheckFrames(FindImage(body)!);
            capture(host, Path.Combine(output, "gif-previews-light.png"));
            App.ApplyTheme("dark"); capture(host, Path.Combine(output, "gif-previews-dark.png"));
            var animator = AnimationBehavior.GetAnimator(image);
            scroll.ScrollToBottom(); await Wait(() => animator.IsPaused);
            // Pause 允许已经提交的当前帧完成，之后不应继续解码或推进。
            await Task.Delay(180);
            var paused = animator.CurrentFrameIndex; await Task.Delay(350);
            Require(animator.CurrentFrameIndex == paused, "离开视口后仍在播放");
            scroll.ScrollToTop(); await Wait(() => !animator.IsPaused);
            preview.ShowImages = false; await Task.Delay(80);
            Require(AnimationBehavior.GetAnimator(image) is null, "无图模式没有释放动画");
            var calls = requested.Count; await Task.Delay(80); Require(requested.Count == calls, "无图模式仍下载 GIF");
            preview.ShowImages = true; await Wait(() => FindImage(preview)?.Source is WriteableBitmap);
            var old = FindImage(preview)!;
            preview.Post = null; await Task.Delay(80);
            Require(AnimationBehavior.GetAnimator(old) is null && preview.Content is null, "换绑空帖子残留旧 GIF");
            var playingBody = FindImage(body)!;
            host.Close(); await Task.Delay(80);
            Require(AnimationBehavior.GetAnimator(playingBody) is null, "卸载缩略图未释放动画");
        }
        finally { host.Close(); }

        var staticImage = ImageService.Decode(bytes);
        var pngEncoder = new PngBitmapEncoder(); pngEncoder.Frames.Add(BitmapFrame.Create(staticImage));
        using var pngStream = new MemoryStream(); pngEncoder.Save(pngStream); var png = pngStream.ToArray();
        var next = post with { Id = 802, ImagePath = "fixture/static", Extension = ".png" };
        BitmapSource? copied = null;
        byte[]? copiedGif = null; long copiedId = 0;
        var slow = new TaskCompletionSource<byte[]>();
        var slowPost = post with { Id = 803, ImagePath = "fixture/slow" };
        var badPost = post with { Id = 804, ImagePath = "fixture/bad" };
        var viewer = new ImageWindow([post, next, slowPost, badPost], post.Id,
            (url, _) => url == slowPost.ImageUrl() ? slow.Task : Task.FromResult(url == next.ImageUrl() ? png : url == badPost.ImageUrl() ? new byte[] { 1, 2 } : bytes),
            copyImage: bitmap => copied = bitmap, copyGif: (data, id) => { copiedGif = data; copiedId = id; }) { Owner = owner, ShowInTaskbar = false };
        viewer.Show();
        try
        {
            await Wait(() => ViewerImage(viewer).Source is WriteableBitmap);
            var image = ViewerImage(viewer); await CheckFrames(image);
            Require(!viewer.Title.Contains("首帧"), "GIF 标题仍宣称只显示首帧");
            Click(viewer, "+"); Click(viewer, "旋转");
            await CheckFrames(image);
            var menu = image.ContextMenu;
            menu.IsOpen = true; await Task.Delay(40);
            ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
            Require(copied is null && copiedId == post.Id && copiedGif is not null && copiedGif.SequenceEqual(bytes), "GIF 复制没有保留完整原文件");
            capture(viewer, Path.Combine(output, "gif-viewer-dark.png"));
            Click(viewer, "›"); await Wait(() => ViewerImage(viewer).Source is BitmapImage);
            Require(AnimationBehavior.GetAnimator(image) is null, "切到静态图没有释放旧 GIF");
            Click(viewer, "›"); await Task.Delay(40); Click(viewer, "‹");
            await Wait(() => ViewerImage(viewer).Source is BitmapImage);
            slow.SetResult(bytes); await Task.Delay(100);
            Require(viewer.Title.Contains("No.802") && ViewerImage(viewer).Source is BitmapImage, "迟到的 GIF 覆盖静态图");
            Click(viewer, "›"); await Wait(() => ViewerImage(viewer).Source is WriteableBitmap);
            Click(viewer, "›"); await Wait(() => Children<TextBlock>(viewer).Any(t => t.IsVisible && t.Text == "无法预览，可保存原图"));
            Require(Children<Button>(viewer).Single(b => Equals(b.Tag, "保存原图")).IsEnabled, "坏 GIF 不能保存原文件");
            Click(viewer, "‹"); await Wait(() => ViewerImage(viewer).Source is WriteableBitmap);
            image = ViewerImage(viewer); viewer.Close(); await Task.Delay(100);
            Require(AnimationBehavior.GetAnimator(image) is null, "关闭看图窗口没有释放动画");
        }
        finally { viewer.Close(); }

        var finite = new Image(); var finiteHost = new Window { Owner = owner, ShowInTaskbar = false, Width = 240, Height = 160, Content = finite };
        finiteHost.Show();
        using var playback = new GifPlayback(finite, Fixture(loop: false), () => throw new InvalidOperationException("有限循环 GIF 播放失败"));
        try
        {
            await playback.StartAsync(default);
            await Wait(() => AnimationBehavior.GetAnimator(finite).IsComplete);
            var last = Pixels((BitmapSource)finite.Source); await Task.Delay(200);
            Require(last.SequenceEqual(Pixels((BitmapSource)finite.Source)), "未声明循环的 GIF 被强制重复");
        }
        finally { finiteHost.Close(); App.ApplyTheme("light"); }
        Console.WriteLine("PASS: GIF list/body/viewer animation; transparent/partial frames and disposal 2/3; loop metadata; viewport pause/resume; static images; full GIF copy; no-image mode; cancellation, reuse and cleanup.");
    }

    private static async Task CheckFrames(Image image)
    {
        var patterns = new HashSet<string>();
        var expectedPatterns = new[] { "FFFF0000,FFFF0000,FFFF0000", "FFFF0000,FF00FF00,FFFF0000", "FFFF0000,FFFF0000,FF0000FF", "FF00FF00,FFFF0000,00000000" };
        // 观察像素而非提前触发的帧索引事件；允许低速机器跨多轮采到全部帧。
        for (var i = 0; i < 200 && !expectedPatterns.All(patterns.Contains); i++)
        {
            patterns.Add(string.Join(",", Pixels((BitmapSource)image.Source).Select(p => p.ToString("X8"))));
            await Task.Delay(20);
        }
        foreach (var expected in expectedPatterns)
            Require(patterns.Contains(expected), "GIF 帧合成/透明/恢复不正确，缺少 " + expected + "，实际 " + string.Join(";", patterns));
    }
    private static uint[] Pixels(BitmapSource bitmap) => new[] { 0, 25, 60 }.Select(x => { var data = new byte[4]; bitmap.CopyPixels(new Int32Rect(x, 0, 1, 1), data, 4, 0); return BitConverter.ToUInt32(data); }).ToArray();
    internal static byte[] Fixture(bool loop)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)80); writer.Write((ushort)40);
        writer.Write(new byte[] { 0x81, 0, 0, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255 });
        if (loop) writer.Write(new byte[] { 0x21, 0xFF, 11 }.Concat(Encoding.ASCII.GetBytes("NETSCAPE2.0")).Concat(new byte[] { 3, 1, 0, 0, 0 }).ToArray());
        void Frame(int left, int width, int disposal, Func<int, int> pixel)
        {
            writer.Write(new byte[] { 0x21, 0xF9, 4, (byte)((disposal << 2) | 1), 12, 0, 0, 0, 0x2C });
            writer.Write((ushort)left); writer.Write((ushort)0); writer.Write((ushort)width); writer.Write((ushort)40); writer.Write((byte)0); writer.Write((byte)2);
            var data = new List<byte>(); int bits = 0, count = 0;
            void Code(int value) { bits |= value << count; count += 3; while (count >= 8) { data.Add((byte)bits); bits >>= 8; count -= 8; } }
            // 每像素重置字典，生成无需外部图像工具的确定性 GIF 测试数据。
            for (var p = 0; p < width * 40; p++) { Code(4); Code(pixel(p)); }
            Code(5); if (count > 0) data.Add((byte)bits);
            for (var start = 0; start < data.Count; start += 255) { var block = data.Skip(start).Take(255).ToArray(); writer.Write((byte)block.Length); writer.Write(block); }
            writer.Write((byte)0);
        }
        Frame(0, 80, 1, _ => 1); Frame(0, 40, 3, p => p == 0 ? 0 : 2);
        Frame(40, 40, 2, _ => 3); Frame(0, 20, 1, _ => 2); writer.Write((byte)0x3B);
        return stream.ToArray();
    }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject => UiRefinementChecks.Descendants<T>(parent);
    private static Image? FindImage(PostImage post) => Children<Image>(post).FirstOrDefault();
    private static Image ViewerImage(Window window) => Children<Image>(window).First(i => i.Parent is Canvas);
    private static void Click(Window window, string tag) => Children<Button>(window).Single(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(30); Require(condition(), "等待 GIF 状态超时"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

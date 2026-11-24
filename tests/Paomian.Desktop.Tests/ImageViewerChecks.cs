using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

/// <summary>固定图片与可控制的慢请求，验证真实窗口的切图竞态、重试、缩放拖动和生命周期。</summary>
internal static class ImageViewerChecks
{
    internal static async Task CheckNoticeAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        App.ApplyTheme("dark");
        var bytes = Picture();
        var first = new Post { Id = 901, ImagePath = "fixture/901", Extension = ".png" };
        var last = first with { Id = 902, ImagePath = "fixture/902" };
        var pages = new ImagePageCursor(900, 3, 3, [901, 902], null, (_, _) => throw new InvalidOperationException("末页不应请求"));
        var viewer = new ImageWindow([first, last], last.Id, (_, _) => Task.FromResult(bytes), pages) { Owner = owner, ShowInTaskbar = false };
        viewer.Show();
        try
        {
            await Wait(() => Image(viewer).Source is not null && Text(viewer, "已到最后一张"));
            capture(viewer, Path.Combine(output, "end-notice-visible.png"));
            await Task.Delay(2600);
            capture(viewer, Path.Combine(output, "end-notice-after-wait.png"));
            Require(!Text(viewer, "已到最后一张"), "已到最后一张提示停留超过 2.6 秒仍未消失");
            Require(!Buttons(viewer).Single(b => Equals(b.Tag, "›")).IsEnabled && Image(viewer).Source is not null, "提示消失改变了末页或当前图片状态");
            PressKey(viewer, Key.Right); PressKey(viewer, Key.R); await Pause();
            Require(!Text(viewer, "已到最后一张"), "边界按键或旋转重新唤起已收起的提示");
            PressKey(viewer, Key.Left); await Wait(() => Image(viewer).Source is not null);
            PressKey(viewer, Key.Right); await Wait(() => Text(viewer, "已到最后一张"));
            PressKey(viewer, Key.Left); await Pause(); Require(!Text(viewer, "已到最后一张"), "离开末图没有立即收起提示");
            PressKey(viewer, Key.Right); await Wait(() => Text(viewer, "已到最后一张"));
            await Task.Delay(2600); Require(!Text(viewer, "已到最后一张"), "再次抵达末图后提示未自动收起");
        }
        finally { viewer.Close(); }
        await CheckPaging(owner, bytes, output, capture, checkNoticeLifetime: true);
        Console.WriteLine("PASS: terminal image notice expires without changing image/end state; related pagination checks passed.");
    }

    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var bytes = Picture();
        var first = new TaskCompletionSource<byte[]>(); var closing = new TaskCompletionSource<byte[]>();
        var calls = new Dictionary<string, int>(); bool closeCanceled = false;
        var posts = Enumerable.Range(301, 5).Select(id => new Post { Id = id, ImagePath = $"fixture/{id}", Extension = ".png" }).ToArray();
        var thumbnailKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(posts[2].ImageUrl(thumbnail: true)!)));
        ImageService.DiskCache!.Write($"images/{thumbnailKey}.bin", bytes);
        Task<byte[]> Download(string url, CancellationToken token)
        {
            calls[url] = calls.GetValueOrDefault(url) + 1; var n = calls[url];
            if (url == posts[0].ImageUrl() && n == 1) return first.Task; // 故意忽略取消，模拟已经完成的慢请求。
            if (url == posts[2].ImageUrl() && n == 1) return Task.FromException<byte[]>(new HttpRequestException("fixture failure", null, HttpStatusCode.BadGateway));
            if (url == posts[3].ImageUrl() && n == 1) return Task.FromResult(new byte[] { 1, 2, 3 });
            if (url == posts[4].ImageUrl()) { token.Register(() => closeCanceled = true); return closing.Task; }
            return Task.FromResult(bytes);
        }
        var viewer = new ImageWindow(posts, 301, Download) { Owner = owner, ShowInTaskbar = false };
        viewer.Show(); await Pause();
        await Wait(() => Children<ImageLoading>(viewer).Any(p => p.IsVisible && p.Children.Count == 1));
        Require(Children<ImageLoading>(viewer).Any(p => p.IsVisible), "加载时没有可见进度反馈");
        capture(viewer, Path.Combine(output, "image-loading.png"));
        await Record(viewer, output, "original");
        Click(viewer, "›"); await Wait(() => Image(viewer).Source is not null);
        first.SetResult(bytes); await Pause();
        Require(viewer.Title.Contains("No.302") && Info(viewer).Contains("2 / 5"), "慢请求覆盖新图片");
        PressKey(viewer, Key.Left); await Wait(() => Image(viewer).Source is not null);
        Require(calls[posts[0].ImageUrl()!] == 2, "取消的原图污染缓存");
        PressKey(viewer, Key.Right); await Wait(() => Image(viewer).Source is not null);
        Require(calls[posts[1].ImageUrl()!] == 1, "返回已看图片时没有复用临时缓存");
        Require(Buttons(viewer).Single(b => Equals(b.Tag, "保存原图")).IsEnabled, "成功图片无法保存");
        Click(viewer, "1:1"); await Pause();
        var image = Image(viewer); var before = image.RenderTransform.Value;
        Require(Math.Abs(before.M11 - 1) < .001, "原始像素比例错误");
        viewer.PanBy(-120, -80); var after = image.RenderTransform.Value;
        Require(after.OffsetX < before.OffsetX && after.OffsetY < before.OffsetY, "放大后无法拖动");
        viewer.PanBy(-999999, 999999); after = image.RenderTransform.Value;
        var canvas = Children<Canvas>(viewer).Single(c => c.Children.Contains(image));
        Require(after.OffsetX >= canvas.ActualWidth - image.Width - 1 && after.OffsetY <= 0, "拖动超出边界");
        PressKey(viewer, Key.D0); await Pause();
        Require(image.RenderTransform.Value.M11 < 1 && image.Width * image.RenderTransform.Value.M11 <= canvas.ActualWidth, "适应窗口未完整显示图片");
        capture(viewer, Path.Combine(output, "image-fit-light.png"));
        App.ApplyTheme("dark"); viewer.Width = 510; viewer.Height = 510; await Pause();
        Program.RequireWindowFits(viewer);
        Require(Buttons(viewer).All(b => b.TransformToAncestor(viewer).Transform(new Point(b.ActualWidth, b.ActualHeight)).X <= viewer.ActualWidth), "窄窗口操作栏溢出");
        capture(viewer, Path.Combine(output, "image-fit-dark-narrow.png"));
        Click(viewer, "+"); var enlarged = image.RenderTransform.Value.M11; Click(viewer, "−");
        Require(image.RenderTransform.Value.M11 < enlarged, "缩放按钮无效");
        Click(viewer, "1:1"); await Pause();
        Require(image.Width * image.RenderTransform.Value.M11 < canvas.ActualWidth, "比例按钮无法返回适应窗口");
        Click(viewer, "旋转"); await Pause();
        var rotated = image.RenderTransform.Value;
        Require(rotated.M11 == 0 && rotated.M12 < 0 && rotated.M21 > 0, "向左旋转方向错误");
        Require(image.Width * Math.Abs(rotated.M12) <= canvas.ActualHeight, "旋转后适应窗口裁掉长边");
        capture(viewer, Path.Combine(output, "image-rotated.png"));
        PressKey(viewer, Key.R); PressKey(viewer, Key.R); PressKey(viewer, Key.R);
        Require(image.RenderTransform.Value.M11 > 0 && image.RenderTransform.Value.M12 == 0, "四次旋转未恢复");
        Require(!Buttons(viewer).Any(b => Equals(b.Tag, "更多")), "仍保留更多按钮");
        PressKey(viewer, Key.Right); await Wait(() => Text(viewer, "图片加载失败（HTTP 502），显示缩略图"));
        Require(Children<Image>(viewer).Any(i => i.IsVisible && i.Parent is StackPanel && i.Source is BitmapSource { PixelWidth: 700 }),
            "原图下载失败时没有显示已缓存的缩略图");
        Require(Image(viewer).Source is null && !Buttons(viewer).Single(b => Equals(b.Tag, "保存原图")).IsEnabled, "失败时还能保存旧图或缩略图");
        capture(viewer, Path.Combine(output, "image-failure.png"));
        Click(viewer, "重试"); await Wait(() => Image(viewer).Source is not null);
        PressKey(viewer, Key.Right); await Wait(() => Text(viewer, "无法预览，可保存原图"));
        Require(Buttons(viewer).Single(b => Equals(b.Tag, "保存原图")).IsEnabled, "解码失败后无法保存原文件");
        Click(viewer, "重试"); await Wait(() => Image(viewer).Source is not null);
        Require(calls[posts[3].ImageUrl()!] == 2, "重试仍使用坏图缓存");
        PressKey(viewer, Key.Right); await Pause();
        Require(!Buttons(viewer).Single(b => Equals(b.Tag, "›")).IsEnabled, "末图仍能下一张");
        PressKey(viewer, Key.Escape); closing.SetResult(bytes); await Pause();
        Require(!viewer.IsVisible && closeCanceled, "关闭窗口没有取消请求");
        App.ApplyTheme("light");
        await CheckThumbnails(bytes, output, capture);
        var quotedPost = new Post { Id = 813, ImagePath = "fixture/reference", Extension = ".png" };
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(quotedPost.ImageUrl(true)!)));
        owner.Model.Store.Cache.Write("images/" + key + ".bin", bytes);
        var settle = ImageService.ScrollSettleDelay;
        var quote = new TextWindow("引用预览", "", referencePreview: true) { Owner = owner };
        try
        {
            ImageService.ScrollSettleDelay = TimeSpan.FromMilliseconds(450);
            quote.SetPost(quotedPost); quote.Show();
            await Wait(() => Children<ImageLoading>(quote).Any(item => item.Children.Count == 1));
            await Wait(() => Children<PostImage>(quote).Any(item => Children<Image>(item).Any()));
        }
        finally { quote.Close(); ImageService.ScrollSettleDelay = settle; }

        if (Environment.GetCommandLineArgs().Contains("--record-motion"))
            await RecordReading(owner, output, bytes);
        await CheckPaging(owner, bytes, output, capture);
        await CheckModelCursor(output);
        File.WriteAllText(Path.Combine(output, "image-results.json"), JsonSerializer.Serialize(new { Loading = true, StaleResponse = true, Cache = true, RetryDownloadAndDecode = true, ZoomAndPan = true, NarrowThemes = true, CloseCancellation = true, ThumbnailRecycling = true, NoImageMode = true, TailPagination = true, ReaderAndIdentityIsolation = true }));
        Console.WriteLine("PASS: image loading, previous/next, temporary cache, stale requests, download/decode retry, zoom/pan bounds, fit, rotation, themes, narrow toolbar without More, close cancellation, thumbnail recycling, no-image mode.");
    }
    private static async Task RecordReading(MainWindow owner, string output, byte[] bytes)
    {
        var vm = owner.Model;
        var delay = ImageService.ScrollSettleDelay;
        var post = new Post { Id = 913, Fid = 4, Content = "沿河步道", ImagePath = "fixture/record", Extension = ".png" };
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(post.ImageUrl(true)!)));
        vm.Store.Cache.Write("images/" + key + ".bin", bytes);
        try
        {
            ImageService.ScrollSettleDelay = TimeSpan.FromSeconds(45);
            KeyboardReadingFixture.SeedImages(vm.Store.Cache);
            vm.State.ListImages = vm.State.ReaderImages = ImageModes.Auto;
            vm.AppearanceChanged();
            await vm.SelectForumAsync(new Forum(4, "日常", "综合"));
            await vm.OpenThreadAsync(KeyboardReadingFixture.Thread);
            vm.Posts.Clear(); vm.ReadingPosts.Clear();
            for (var i = 0; i < 4; i++)
            {
                var item = post with { Id = post.Id + i };
                vm.Posts.Add(new PostCard(item, "日常"));
                vm.ReadingPosts.Add(new ReadPost(item, false));
            }
            foreach (var pane in new[] { "ThreadList", "ReaderList" })
            {
                var list = (ListBox)owner.FindName(pane);
                list.ScrollIntoView(list.Items[0]);
                await Wait(() => Children<LoadingIndicator>(list).Any(x => x.IsVisible));
                Require(Children<LoadingIndicator>(list).Any(x => x.Circular && x.IsVisible),
                    $"{pane} 缺少圆环，项目 {list.Items.Count}，可见 {list.IsVisible}，有串 {vm.HasThread}");
            }
            var intervals = new List<double>();
            var rings = Children<LoadingIndicator>(owner).Count(x => x.IsVisible && x.Circular);
            TimeSpan? last = null;
            void Frame(object? sender, EventArgs args)
            {
                var time = ((RenderingEventArgs)args).RenderingTime;
                if (last.HasValue && time > last) intervals.Add((time - last.Value).TotalMilliseconds);
                last = time;
            }
            CompositionTarget.Rendering += Frame;
            try { await Task.Delay(2500); }
            finally { CompositionTarget.Rendering -= Frame; }
            intervals.Sort();
            Require(intervals.Count > 1 && rings >= 2, "缺少并行动画帧");
            File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(new {
                Rings = rings, Samples = intervals.Count, MedianMs = intervals[intervals.Count / 2],
                P95Ms = intervals[(int)((intervals.Count - 1) * .95)], MaxMs = intervals[^1] }));
            await Record(owner, output, "reading");
        }
        finally { ImageService.ScrollSettleDelay = delay; }
    }

    private static async Task Record(Window window, string output, string name)
    {
        if (!Environment.GetCommandLineArgs().Contains("--record-motion")) return;
        var folder = Directory.CreateDirectory(Path.Combine(output, name)).FullName;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var timestamps = new List<double>();
        while (clock.Elapsed.TotalSeconds < 5)
        {
            timestamps.Add(clock.Elapsed.TotalSeconds);
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(folder, $"{timestamps.Count:D4}.png"))) png.Save(file);
            await Task.Delay(20);
        }
        File.WriteAllText(Path.Combine(folder, "times.json"), JsonSerializer.Serialize(timestamps));
    }

    private static async Task CheckPaging(MainWindow owner, byte[] bytes, string output, Action<Window, string> capture, bool checkNoticeLifetime = false)
    {
        var first = new Post { Id = 901, ImagePath = "fixture/page1", Extension = ".png" };
        var last = first with { Id = 902 }; var added = first with { Id = 903 };
        var original = new Post { Id = 900, ReplyCount = 57 };
        var gate = new TaskCompletionSource<ThreadPage>(); var calls = new List<int>(); var failed = false;
        var pages = new ImagePageCursor(900, 1, 3, [901, 902], null, (page, _) =>
        {
            calls.Add(page);
            if (page == 2) return gate.Task;
            if (!failed) { failed = true; throw new IOException("offline fixture"); }
            return Task.FromResult(new ThreadPage(original, [added, added], page));
        });
        var readerCount = owner.Model.ReadingPosts.Count; var readerLabel = owner.Model.ThreadPageLabel;
        var viewer = new ImageWindow([first, last], first.Id, (_, _) => Task.FromResult(bytes), pages) { Owner = owner, ShowInTaskbar = false };
        viewer.Show(); await Wait(() => Image(viewer).Source is not null);
        Require(calls.Count == 0, "未到末图就续页");
        PressKey(viewer, Key.Right); await Wait(() => calls.Count == 1);
        await Wait(() => Image(viewer).Source is not null);
        await Record(viewer, output, "pagination");
        var retained = Image(viewer).Source;
        var loading = Children<TextBlock>(viewer).Single(t => t.IsVisible && t.Text == "正在加载下一页图片");
        var canvas = (Canvas)Image(viewer).Parent;
        var loadingCenter = loading.TranslatePoint(new Point(loading.ActualWidth / 2, 0), canvas).X;
        Require(Math.Abs(loadingCenter - canvas.ActualWidth / 2) <= 1, "看图续页提示未在视口居中");
        if (checkNoticeLifetime) { await Task.Delay(2600); Require(Text(viewer, "正在加载下一页图片"), "续页加载提示被末图计时提前隐藏"); }
        for (var i = 0; i < 5; i++) PressKey(viewer, Key.Right);
        Require(calls.Count == 1 && Image(viewer).Source == retained, "连点并发请求或清空当前图");
        capture(viewer, Path.Combine(output, "image-page-loading.png"));
        // 上一张允许继续看；迟到续页不得把用户强制带回最后一张。
        PressKey(viewer, Key.Left);
        gate.SetResult(new ThreadPage(original, [new Post { Id = 904 }], 2));
        await Wait(() => calls.Count == 2); await Pause();
        Require(viewer.Title.Contains("No.901"), "续页结果抢走当前图片");
        PressKey(viewer, Key.Right); await Wait(() => Text(viewer, "下一页加载失败") && Image(viewer).Source is not null);
        Require(calls.SequenceEqual(new[] { 2, 3 }), "失败后自动循环重试");
        if (checkNoticeLifetime) { await Task.Delay(2600); Require(Text(viewer, "下一页加载失败") && Buttons(viewer).Single(b => Equals(b.Tag, "续页重试")).IsVisible, "重试入口被计时隐藏"); }
        capture(viewer, Path.Combine(output, "image-page-retry.png"));
        PressKey(viewer, Key.Right);
        await Wait(() => viewer.Title.Contains("No.903") && Image(viewer).Source is not null);
        Require(calls.SequenceEqual(new[] { 2, 3, 3 }) && viewer.Title.Contains("3 / 3"), "失败重试跳页或图片重复");
        Require(Text(viewer, "已到最后一张") && !Buttons(viewer).Single(b => Equals(b.Tag, "›")).IsEnabled, "末页未停止");
        if (checkNoticeLifetime) { await Task.Delay(2600); Require(!Text(viewer, "已到最后一张"), "自动续页到末图后提示未消失"); }
        Require(owner.Model.ReadingPosts.Count == readerCount && owner.Model.ThreadPageLabel == readerLabel, "看图续页改变正文阅读位置");
        viewer.Close();
        // 直接从正文末页打开，不应出现从未启动的加载条或无效重试入口。
        var end = new ImagePageCursor(900, 3, 3, [901], null, (_, _) => throw new InvalidOperationException("末页不应请求"));
        viewer = new ImageWindow([first], first.Id, (_, _) => Task.FromResult(bytes), end) { Owner = owner, ShowInTaskbar = false };
        viewer.Show(); await Wait(() => Image(viewer).Source is not null);
        Require(Text(viewer, "已到最后一张") && !Children<ProgressBar>(viewer).Any(p => p.IsVisible)
            && !Buttons(viewer).Single(b => Equals(b.Tag, "续页重试")).IsVisible, "直接打开末页误显示加载或重试");
        viewer.Close();
        var scanCalls = 0; var scan = new ImagePageCursor(900, 1, 12, [], null, (p, _) =>
        { scanCalls++; return Task.FromResult(new ThreadPage(original with { ReplyCount = 228 }, [new Post { Id = 2000 + p }], p)); });
        viewer = new ImageWindow([first], first.Id, (_, _) => Task.FromResult(bytes), scan) { Owner = owner, ShowInTaskbar = false };
        viewer.Show(); await Wait(() => Text(viewer, "这些页暂无图片"));
        Require(scanCalls == 5, "连续无图页没有限次");
        if (checkNoticeLifetime) { await Task.Delay(2600); Require(Text(viewer, "这些页暂无图片") && Buttons(viewer).Single(b => Equals(b.Tag, "续页重试")).IsVisible, "继续查找入口被计时隐藏"); }
        await Pause(); Require(scanCalls == 5, "无图页被后台自动读穿");
        Click(viewer, "续页重试"); await Wait(() => scanCalls == 10); viewer.Close();
        var canceled = false; var slow = new TaskCompletionSource<ThreadPage>();
        var stop = new ImagePageCursor(900, 1, 2, [], null, (_, ct) => { ct.Register(() => canceled = true); return slow.Task; });
        viewer = new ImageWindow([first], first.Id, (_, _) => Task.FromResult(bytes), stop) { Owner = owner, ShowInTaskbar = false };
        viewer.Show(); await Pause(); viewer.Close(); slow.SetResult(new ThreadPage(original, [added], 2)); await Pause();
        Require(canceled, "关闭看图窗口没有取消续页");
        Console.WriteLine("PASS: tail auto paging, empty-page scanning, serial requests, retry same page, navigation intent, deduplication, end stop, bounded scan, reader isolation and close cancellation.");
    }
    private static async Task CheckThumbnails(byte[] bytes, string output, Action<Window, string> capture)
    {
        var bitmap = ImageService.Decode(bytes, 100); var gate = new TaskCompletionSource<BitmapSource>(); var calls = 0;
        var post = new Post { Id = 801, ImagePath = "fixture/thumb", Extension = ".png" };
        var thumb = new PostImage((_, _) => { calls++; return calls == 1 ? gate.Task : Task.FromResult(bitmap); }) { Post = post, PreviewHeight = 72, PreviewMaxWidth = 72 };
        var host = new ThumbnailWindow(thumb) { ShowInTaskbar = false }; host.Show(); await Pause();
        await Wait(() => Children<ImageLoading>(thumb).Any(p => p.IsVisible && p.Children.Count == 1));
        Require(Children<ImageLoading>(thumb).Any(p => p.IsVisible), "缩略图加载反馈未显示");
        capture(host, Path.Combine(output, "thumbnail-loading.png"));
        thumb.Post = post with { Id = 802, ImagePath = "fixture/next" }; await Pause(); gate.SetResult(bitmap); await Pause();
        Require(thumb.Post.Id == 802 && Children<Image>(thumb).Single().Source == bitmap, "虚拟化换绑错图");
        var failedCalls = 0;
        var retry = new PostImage((_, _) => ++failedCalls == 1 ? Task.FromException<BitmapSource>(new TimeoutException()) : Task.FromResult(bitmap)) { Post = post };
        host.Set(retry); await Pause();
        Require(Buttons(host).Any(b => Equals(b.Content, "重试") && Equals(b.ToolTip, "图片加载超时，点击重试")), "缩略图超时未显示原因和重试入口");
        var opened = false;
        void Open(PostImage source, Post selected) => opened = ReferenceEquals(source, retry) && selected.Id == post.Id;
        PostImage.ImageClicked += Open;
        try { Click(host, "查看原图"); }
        finally { PostImage.ImageClicked -= Open; }
        Require(opened && failedCalls == 1, "缩略图失败后没有直接打开对应原图的入口");
        Click(host, "重试"); await Pause();
        Require(failedCalls == 2 && Children<Image>(retry).Any(), "缩略图失败不能重试");
        var noImageCalls = 0; var manual = new PostImage((_, _) => { noImageCalls++; return Task.FromResult(bitmap); }) { Post = post, ShowImages = false };
        host.Set(manual); await Pause();
        Require(noImageCalls == 0, "无图模式仍下载缩略图");
        Require(Buttons(host).Any(b => Equals(b.Content, "显示这张图片") && b.IsEnabled), "无图模式没有手动看图入口");
        Click(host, "显示这张图片"); await Wait(() => Children<Image>(manual).Any());
        Require(noImageCalls == 1 && !Children<ImageLoading>(manual).Any(), "点击加载或同步命中时的占位状态错误");

        // 未卸载的预取行滚出视口也应停止动画，再滚回来才恢复。
        var loading = new ImageLoading(144, 60);
        var stack = new StackPanel(); stack.Children.Add(loading); stack.Children.Add(new Border { Height = 850 });
        var viewport = new ScrollViewer { Content = stack, Width = 200, Height = 120 };
        host.Set(viewport);
        await Wait(() => loading.Children.Count == 1);
        if (SystemParameters.ClientAreaAnimation)
        {
            var ring = (LoadingIndicator)loading.Children[0];
            loading.Resources["Accent"] = Brushes.MediumPurple;
            Require(ring.Foreground == Brushes.MediumPurple && ring.Width == 22 && ring.Circular, "圆环未跟随强调色或尺寸不符");
        }
        viewport.ScrollToBottom();
        await Wait(() => loading.IsLoaded && loading.Children.Count == 0);
        viewport.ScrollToTop(); await Wait(() => loading.Children.Count == 1);
        var progress = new ProgressBar { Height = 4, Width = 140 };
        progress.Resources[SystemParameters.ClientAreaAnimationKey] = false;
        progress.SetResourceReference(FrameworkElement.StyleProperty, "LoadingProgress");
        host.Set(progress); await Pause();
        Require(Children<LoadingIndicator>(progress).Single() is { IsVisible: true, HasAnimatedProperties: false },
            "关闭动画偏好后仍有线条时钟");
        progress.IsIndeterminate = false; progress.Value = 37; await Pause();
        Require(progress.Value == 37 && !Children<LoadingIndicator>(progress).Any(), "确定进度仍使用循环模板");
        host.Close(); await Wait(() => !loading.IsLoaded && loading.Children.Count == 0);
    }
    private static async Task CheckModelCursor(string output)
    {
        var transport = new GalleryHandler();
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(transport)), new LocalStore(Path.Combine(output, "gallery-profile")));
        await vm.OpenThreadAsync(900, 4);
        vm.OnlyPo = true;
        var cursor = vm.CreateImageCursor()!;
        var label = vm.ThreadPageLabel; var input = vm.PageText; var ids = vm.ReadingPosts.Select(p => p.Post.Id).ToArray();
        var history = JsonSerializer.Serialize(vm.State.History);
        var next = await cursor.NextAsync(default);
        Require(next.Page == 5 && next.Images.Count == 1 && next.Images[0].UserHash == "po", "图库没有从已加载末页继续或丢失 PO 范围");
        Require(vm.ThreadPageLabel == label && vm.PageText == input && vm.ReadingPosts.Select(p => p.Post.Id).SequenceEqual(ids)
            && JsonSerializer.Serialize(vm.State.History) == history, "真实模型看图续页修改阅读状态");
        await vm.OpenThreadAsync(1000, 1);
        next = await cursor.NextAsync(default);
        Require(next.Images.Single().Id == 90006 && vm.Original?.Id == 1000, "切换正文串后图库混串");
        vm.State.ActiveCookieId = "different-fixture-identity"; var requests = transport.Calls;
        var blocked = false;
        try { await cursor.NextAsync(default); } catch (IslandException) { blocked = true; }
        Require(blocked && requests == transport.Calls, "更换饼干后旧图库仍发送请求");
        Console.WriteLine("PASS: real view-model gallery cursor starts at loaded tail, keeps PO filter, isolates reader/history, remains on original thread and checks identity.");
    }
    private sealed class GalleryHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var q = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var id = long.Parse(q["id"]!); var page = int.Parse(q["page"]!);
            var json = JsonSerializer.Serialize(new { id, ReplyCount = 190, user_hash = "po", Replies = new[] {
                new { id = id * 100 + page, user_hash = "po", img = "fixture/photo", ext = ".png" },
                new { id = id * 100 + page + 20, user_hash = "other", img = "fixture/other", ext = ".png" } } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private sealed class ThumbnailWindow : ChildWindow
    {
        public ThumbnailWindow(PostImage thumb) : base("图片加载检查") { Width = 500; Height = 300; Set(thumb); }
        public void Set(FrameworkElement thumb) => SetClientContent(new Border { Child = thumb, Padding = new(24), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top });
    }
    private static byte[] Picture()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.LightSteelBlue, null, new Rect(0, 0, 1800, 1200));
            drawing.DrawRectangle(Brushes.Teal, null, new Rect(80, 80, 1640, 240));
            drawing.DrawEllipse(Brushes.Goldenrod, null, new Point(900, 750), 320, 300);
        }
        var bitmap = new RenderTargetBitmap(1800, 1200, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject => UiRefinementChecks.Descendants<T>(parent);
    private static IEnumerable<Button> Buttons(DependencyObject parent) => Children<Button>(parent);
    private static Image Image(Window window) => Children<Image>(window).First(i => i.Parent is Canvas);
    private static string Info(Window window) => string.Join("|", Children<TextBlock>(window).Select(t => t.Text));
    private static bool Text(Window window, string text) => Children<TextBlock>(window).Any(t => t.IsVisible && t.Text == text);
    private static void Click(Window window, string text) => Buttons(window).Single(b => b.IsVisible && (Equals(b.Tag, text) || Equals(b.Content, text))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void PressKey(Window window, Key key) => window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, key)
    { RoutedEvent = key == Key.Escape ? Keyboard.PreviewKeyDownEvent : Keyboard.KeyDownEvent });
    private static async Task Pause() { await Task.Delay(100); }
    private static async Task Wait(Func<bool> condition) { for (var i = 0; i < 60 && !condition(); i++) await Task.Delay(50); Require(condition(), "等待图片状态超时"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}



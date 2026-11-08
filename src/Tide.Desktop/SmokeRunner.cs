using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>通过启动参数显式启用的运行验证：读取公开数据、检查本机状态并截图，不提交发言。</summary>
public static class SmokeRunner
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm, string output)
    {
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, object>();
        var protectedValue = SecretProtection.Protect("synthetic-cookie-test%2Fvalue");
        checks["dpapiRoundtrip"] = SecretProtection.Unprotect(protectedValue) == "synthetic-cookie-test%2Fvalue";
        // 这部分访问真实只读 API；与使用模拟 HTTP 的 Tide.Tests 分开，便于判断网络故障。
        await vm.InitializeAsync();
        if (vm.Posts.Count == 0) throw new InvalidOperationException("真实时间线没有加载成功：" + vm.Error);
        checks["liveForums"] = vm.State.Forums.Count; checks["liveTimelinePosts"] = vm.Posts.Count;
        await vm.OpenThreadAsync(69574374, 1);
        if (vm.Original?.Id != 69574374 || vm.ReadingPosts.Count < 2) throw new InvalidOperationException("真实参考串加载失败：" + vm.Error);
        checks["liveThread"] = vm.Original.Id; checks["firstPagePosts"] = vm.ReadingPosts.Count;
        await vm.OpenThreadAsync(69574374, 2, true);
        checks["secondPageAccumulated"] = vm.ReadingPosts.Count > 20;
        await vm.OpenThreadAsync(69574374, 1);
        // 只读 smoke 不修改云订阅，以历史列表验证同一主楼的图片。
        vm.ShowLibrary("history"); checks["historyList"] = vm.Posts.Any(p => p.Post.Id == 69574374);
        // 历史列表和正文同时显示同一有图主楼，检查图片解码。
        checks["originalHasImage"] = vm.Original!.HasImage;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            window.UpdateLayout();
            if (LoadedOriginalImages(window, 69574374) >= 2) break;
            await Task.Delay(250);
        }
        checks["originalImageInListAndReader"] = LoadedOriginalImages(window, 69574374) >= 2;
        await Capture(window, Path.Combine(output, "original-images.png"));
        vm.State.ListImages = vm.State.ReaderImages = ImageModes.Click; vm.AppearanceChanged();
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        checks["noImageMode"] = Descendants<PostImage>(window).Where(p => p.IsVisible && p.Post?.Id == 69574374)
            .All(p => p.Content is Button { Content: string text } && text == "显示这张图片");
        vm.State.ListImages = vm.State.ReaderImages = ImageModes.Auto; vm.AppearanceChanged();
        await vm.SelectForumAsync(vm.State.Forums.First(f => !f.IsTimeline && f.Id == 30));
        checks["liveBoardPosts"] = vm.Posts.Count;
        vm.OnlyPo = true; checks["onlyPo"] = vm.ReadingPosts.All(p => p.IsTip || p.Post.UserHash == vm.Original!.UserHash); vm.OnlyPo = false;
        await Task.Delay(1500);
        await Capture(window, Path.Combine(output, "desktop-light.png"));
        vm.State.Theme = "dark"; vm.AppearanceChanged(); App.ApplyTheme("dark"); await Capture(window, Path.Combine(output, "desktop-dark.png"));
        window.Width = 1060; window.Height = 720; await Capture(window, Path.Combine(output, "desktop-compact.png"));
        vm.State.Theme = "light"; vm.AppearanceChanged(); App.ApplyTheme("light"); window.Width = 1280; window.Height = 720;
        var editor = new ComposerWindow(vm, 69574374) { Owner = window, ShowInTaskbar = false }; editor.Show();
        checks["replyFluentTitleBar"] = HasFluentCaption(editor);
        await Capture(editor, Path.Combine(output, "composer.png")); editor.Close();
        var newThread = new ComposerWindow(vm, 0) { Owner = window, ShowInTaskbar = false }; newThread.Show();
        checks["newThreadFluentTitleBar"] = HasFluentCaption(newThread);
        await Capture(newThread, Path.Combine(output, "new-thread.png")); newThread.Close();
        var cookies = new CookieWindow(vm) { Owner = window, ShowInTaskbar = false }; cookies.Show();
        checks["cookiesFluentTitleBar"] = HasFluentCaption(cookies);
        await Capture(cookies, Path.Combine(output, "cookies.png")); cookies.Close();
        var settings = new SettingsWindow(vm) { Owner = window, ShowInTaskbar = false }; settings.Show();
        checks["settingsFluentTitleBar"] = HasFluentCaption(settings);
        await Capture(settings, Path.Combine(output, "settings.png")); settings.Close();
        var quoteBody = new PostBody { Html = ">>No.69574374" };
        var paragraph = quoteBody.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().Single();
        var link = paragraph.Inlines.OfType<System.Windows.Documents.Hyperlink>().Single();
        link.RaiseEvent(new RoutedEventArgs(System.Windows.Documents.Hyperlink.ClickEvent));
        await Task.Delay(1200);
        var reference = window.OwnedWindows.OfType<TextWindow>().FirstOrDefault(w => w.Title.StartsWith("引用"));
        checks["referenceDialogOpened"] = reference is not null;
        if (reference is not null) { checks["referenceFluentTitleBar"] = HasFluentCaption(reference); await Capture(reference, Path.Combine(output, "reference.png")); reference.Close(); }
        checks["referenceDialogClosed"] = reference is not null && !reference.IsVisible;
        checks["historyPersisted"] = vm.Store.Load().History.Any(f => f.Post.Id == 69574374);
        checks["cachedPage"] = vm.Store.ReadPage(69574374, 1, "")?.Original.Id == 69574374;
        // 先用真实读取产生缓存，再注入总是断网的传输层，检查是否能恢复已缓存的正文。
        using (var offline = new MainViewModel(new XIslandApi(new HttpClient(new SmokeHandler((_, _) => throw new HttpRequestException("offline")))), vm.Store))
        {
            await offline.OpenThreadAsync(69574374, 1);
            checks["offlineCacheFallback"] = offline.Original?.Id == 69574374 && offline.Status.Contains("离线");
        }
        // 故意让旧请求忽略取消且更晚返回，验证版本号检查能阻止旧串覆盖新串。
        using (var race = new MainViewModel(new XIslandApi(new HttpClient(new SmokeHandler(async (request, _) =>
        {
            var isOld = request.RequestUri!.Query.Contains("id=101");
            await Task.Delay(isOld ? 180 : 10); // Deliberately ignores cancellation to simulate a late response.
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":" + (isOld ? "101" : "102") + ",\"content\":\"race test\",\"Replies\":[]}") };
        }))), new LocalStore(Path.Combine(output, "race-profile"))))
        {
            var first = race.OpenThreadAsync(101, 1); var second = race.OpenThreadAsync(102, 1);
            await Task.WhenAll(first, second); checks["staleResponseIsolation"] = race.Original?.Id == 102;
        }
        // 本流程没有调用 SendPostAsync；该字段记录验证流程约定，不是网络抓包统计。
        checks["postRequestsSent"] = 0;
        File.WriteAllText(Path.Combine(output, "smoke-results.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        if (checks.Values.OfType<bool>().Any(x => !x)) throw new InvalidOperationException("部分烟雾检查未通过，请查看 smoke-results.json。");
    }

    private static bool HasFluentCaption(Window window)
    {
        window.UpdateLayout();
        return window is Wpf.Ui.Controls.FluentWindow { ExtendsContentIntoTitleBar: true } &&
            Descendants<Wpf.Ui.Controls.TitleBar>(window).Any(t => t.IsVisible && t.ActualHeight >= 32);
    }

    private sealed class SmokeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => action(request, cancellationToken);
    }
    private static int LoadedOriginalImages(Window window, long id) => Descendants<PostImage>(window)
        .Count(p => p.IsVisible && p.Post?.Id == id && p.Content is Button { Content: Image { Source: BitmapSource } });

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private static async Task Capture(Window window, string path)
    {
        // 等待布局和数据绑定刷新后渲染窗口内容，不包含操作系统标题栏或其他桌面窗口。
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(200);
        window.UpdateLayout();
        var element = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}

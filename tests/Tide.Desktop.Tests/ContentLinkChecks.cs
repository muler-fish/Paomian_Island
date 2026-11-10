using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ContentLinkChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new LinkHandler(); var external = new List<string>();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(Path.Combine(output, "links-profile")));
        var home = new Forum(4, "综合版1", "综合"); vm.State.Forums.Add(home); vm.State.ShowImages = false;
        var window = new MainWindow(vm, external.Add) { Owner = owner, ShowInTaskbar = false };
        window.Show();
        var source = new TextWindow("链接检查", "") { Owner = window, ShowInTaskbar = false };
        try
        {
            await vm.SelectForumAsync(home); await vm.OpenThreadAsync(1000, 1); await Task.Delay(180);
            var reader = (ListBox)window.FindName("ReaderList");
            var scroll = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            var tip = UiRefinementChecks.Descendants<PostBody>(window).First(b => b.Html.Contains("tips-link-fixture"));
            Link(tip).RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            await Until(() => Popup<ForumLinkPromptWindow>() is { } p && Button(p, "跳转").IsEnabled);
            var prompt = Popup<ForumLinkPromptWindow>()!;
            Require(Text(prompt, "即将跳转到「婆罗门一」") && vm.ActiveForumKey == "f4" && !vm.ListBusy && handler.ListPages.Count == 1, "提示前已切版或占用主列表");
            capture(prompt, Path.Combine(output, "forum-prompt-light.png"));
            App.ApplyTheme("dark"); await Task.Delay(60); capture(prompt, Path.Combine(output, "forum-prompt-dark.png")); App.ApplyTheme("light");
            PressEscape(prompt); Require(vm.ActiveForumKey == "f4" && !prompt.IsVisible, "Esc 取消后仍跳转");
            Link(tip).RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            await Until(() => Popup<ForumLinkPromptWindow>() is { } p && Button(p, "跳转").IsEnabled);
            Click(Popup<ForumLinkPromptWindow>()!, "跳转"); await Until(() => !vm.ListBusy && vm.ActiveForum?.Id == 7);
            Require(vm.Original?.Id == 1000 && owner.Model.ActiveForum is null && external.Count == 0, "板块确认混淆所属窗口或正文");

            source.Show();
            source.SetHtml("<a href=/Forum/timeline/id/1/page/2>综合线</a>"); ClickLink(source);
            await Until(() => Popup<ForumLinkPromptWindow>() is { } p && Button(p, "跳转").IsEnabled);
            Require(vm.ActiveForumKey == "f7", "时间线未确认即跳转"); Click(Popup<ForumLinkPromptWindow>()!, "跳转");
            await Until(() => !vm.ListBusy && vm.ActiveForumKey == "t1");
            Require(handler.ListPages.Last() == ("timeline", 1, 2), "确认后丢失页码");
            source.SetHtml("<a href='/f/技术宅'>技术宅</a>"); ClickLink(source);
            await Until(() => Popup<ForumLinkPromptWindow>() is { } p && Button(p, "跳转").IsEnabled);
            Require(Text(Popup<ForumLinkPromptWindow>()!, "即将跳转到「技术宅(代码)」"), "提示使用错误板块名称");
            Click(Popup<ForumLinkPromptWindow>()!, "取消"); Require(vm.ActiveForumKey == "t1", "取消板块仍切换");

            scroll.ScrollToVerticalOffset(200); await Task.Delay(180);
            var offset = scroll.VerticalOffset; var page = vm.PageText; var historyCount = vm.State.History.Count;
            var ids = vm.ReadingPosts.Select(p => p.Post.Id).ToArray();
            source.SetHtml("<a href='/Home/Forum/thread/id/2000/page/2.html?r=200025'>正文链接</a>"); ClickLink(source);
            await Until(() => Popup<ThreadLinkPreviewWindow>() is { } p && Button(p, "打开第 2 页").IsEnabled);
            var preview = Popup<ThreadLinkPreviewWindow>()!;
            Require(vm.Original?.Id == 1000 && vm.PageText == page && vm.State.History.Count == historyCount
                && vm.ReadingPosts.Select(p => p.Post.Id).SequenceEqual(ids) && Math.Abs(scroll.VerticalOffset - offset) < 1, "预览改变正文、页码、历史或滚动位置");
            Require(UiRefinementChecks.Descendants<PostBody>(preview).Single().Html == "预览主楼 2000"
                && UiRefinementChecks.Descendants<PostImage>(preview).Single().Post?.Id == 2000, "串预览未显示串首及图片入口");
            Require(Text(preview, "预览标题 2000") && !vm.State.History.Any(h => h.Post.Id == 2000), "预览没有标题或写入历史");
            capture(preview, Path.Combine(output, "thread-preview-light.png"));
            App.ApplyTheme("dark"); await Task.Delay(60); capture(preview, Path.Combine(output, "thread-preview-dark.png")); App.ApplyTheme("light");
            Click(preview, "关闭"); Require(vm.Original?.Id == 1000, "关闭预览仍跳串");
            var calls = handler.ThreadCalls.Count(c => c == (2000, 1)); ClickLink(source);
            await Until(() => Popup<ThreadLinkPreviewWindow>() is { } p && Button(p, "打开第 2 页").IsEnabled);
            Require(handler.ThreadCalls.Count(c => c == (2000, 1)) == calls, "重复预览未复用临时缓存");
            Click(Popup<ThreadLinkPreviewWindow>()!, "打开第 2 页");
            await Until(() => !vm.ThreadBusy && vm.Original?.Id == 2000);
            Require(vm.ReaderRestorePosition?.PostId == 200025 && vm.PageText == "2", "明确打开后页码或回复定位丢失");

            vm.OnlyPo = true;
            source.SetHtml("https://www.nmbxd1.com/t/3000?page=3"); ClickLink(source);
            await Until(() => Popup<ThreadLinkPreviewWindow>() is { } p && Button(p, "打开第 3 页").IsEnabled);
            Require(vm.Original?.Id == 2000 && vm.OnlyPo, "纯文本链接仍直跳或改 PO");
            PressEscape(Popup<ThreadLinkPreviewWindow>()!);

            // 错误只显示在预览里，修复网络后可原地重试。
            await window.OpenContentLinkAsync("/t/9999"); await Until(() => Popup<ThreadLinkPreviewWindow>() is { } p && Text(p, "该串不存在"));
            preview = Popup<ThreadLinkPreviewWindow>()!;
            Require(!vm.HasError && vm.Original?.Id == 2000 && !Button(preview, "打开串").IsEnabled, "预览失败污染主窗口");
            handler.FailPreview = false; Click(preview, "重试"); await Until(() => Button(preview, "打开串").IsEnabled); preview.Close();

            // 关闭慢预览与慢板块提示会取消请求，迟到结果不能打开页面。
            handler.ThreadGate = new(); await window.OpenContentLinkAsync("/t/5000");
            await Until(() => handler.ThreadCalls.Any(c => c.Item1 == 5000)); Popup<ThreadLinkPreviewWindow>()!.Close();
            handler.ThreadGate.SetResult(); await Task.Delay(80);
            Require(handler.ThreadCanceled && vm.Original?.Id == 2000 && !vm.State.History.Any(h => h.Post.Id == 5000), "关闭预览仍应用迟到结果");
            handler.CatalogGate = new(); var catalogCalls = handler.CatalogCalls;
            await window.OpenContentLinkAsync("/f/后来新增板块"); await Until(() => handler.CatalogCalls > catalogCalls);
            Popup<ForumLinkPromptWindow>()!.Close(); handler.CatalogGate.SetResult(); await Task.Delay(80);
            Require(handler.CatalogCanceled && vm.ActiveForumKey == "t1" && external.Count == 0, "取消提示仍跳转"); handler.CatalogGate = null;

            await window.OpenContentLinkAsync("/f/已删除板块");
            await Until(() => Popup<ForumLinkPromptWindow>() is { } p && Button(p, "浏览器打开").IsEnabled);
            Require(external.Count == 0, "未知板块未经确认就开网页"); Click(Popup<ForumLinkPromptWindow>()!, "浏览器打开");
            Require(external.Count == 1, "未知板块确认未打开网页");
            await window.OpenContentLinkAsync("https://outside.example/test?a=1&b=2"); Require(external.Count == 2, "外链交接改变");
            source.SetHtml("&gt;&gt;No.5678"); ClickLink(source);
            await Until(() => window.OwnedWindows.OfType<TextWindow>().Any(w => w.Title.StartsWith("引用")));
            foreach (var quote in window.OwnedWindows.OfType<TextWindow>().Where(w => w.Title.StartsWith("引用")).ToArray()) quote.Close();
            foreach (var quote in owner.OwnedWindows.OfType<TextWindow>().Where(w => w.Title.StartsWith("引用")).ToArray()) quote.Close();
            File.WriteAllText(Path.Combine(output, "link-results.json"), JsonSerializer.Serialize(new { Preview = true, ConfirmForum = true, ReadingPreserved = true, Retry = true, Cancellation = true, PageAndReply = true, ExternalFallback = true }));
            Console.WriteLine("PASS: thread preview/close/open, title/body/image entry, temporary cache, reading/PO/history preservation, local error/retry, forum prompt/cancel/confirm, legacy pages/replies, nested owners, themes, cancellation and fallback.");
        }
        finally { source.Close(); window.Close(); App.ApplyTheme("light"); }
    }
    private static T? Popup<T>() where T : Window => Application.Current.Windows.OfType<T>().SingleOrDefault(w => w.IsVisible);
    private static Button Button(Window window, string label) => UiRefinementChecks.Descendants<Button>(window).Single(b => Equals(b.Content, label));
    private static bool Text(Window w, string text) => UiRefinementChecks.Descendants<TextBlock>(w).Any(t => t.Text == text);
    private static void Click(Window w, string text) => Button(w, text).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static Hyperlink Link(PostBody body) => body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Hyperlink>().Single();
    private static void ClickLink(TextWindow w) => Link(UiRefinementChecks.Descendants<PostBody>(w).Single()).RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
    private static void PressEscape(Window w) => w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static async Task Until(Func<bool> done) { for (var i = 0; i < 80 && !done(); i++) await Task.Delay(50); Require(done(), "等待链接交互超时"); }
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }
    private sealed class LinkHandler : HttpMessageHandler
    {
        public int CatalogCalls;
        public bool FailPreview = true, ThreadCanceled, CatalogCanceled;
        public List<(string, int, int)> ListPages = [];
        public List<(int, int)> ThreadCalls = [];
        public TaskCompletionSource? CatalogGate, ThreadGate;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var route = request.RequestUri!.AbsolutePath.Split('/').Last();
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var id = int.Parse(query["id"] ?? "1"); var page = int.Parse(query["page"] ?? "1");
            if (route == "getForumList")
            {
                CatalogCalls++; if (CatalogGate is not null) { ct.Register(() => CatalogCanceled = true); await CatalogGate.Task; }
                return Response("""[{"name":"综合","forums":[{"id":4,"name":"综合版1"},{"id":7,"name":"婆罗门一"},{"id":30,"name":"技术宅","showName":"技术宅(代码)"}]}]""");
            }
            if (route == "getTimelineList") return Response("""[{"id":1,"name":"综合线","max_page":20}]""");
            if (route is "showf" or "timeline") { ListPages.Add((route, id, page)); return Response("[]"); }
            ThreadCalls.Add((id, page));
            if (id == 5000 && ThreadGate is not null) { ct.Register(() => ThreadCanceled = true); await ThreadGate.Task; }
            if (id == 9999 && FailPreview) return Response("""{"success":false,"error":"该串不存在"}""");
            var replies = Enumerable.Range(1, 19).Select(n => (object)new { id = id * 100 + page * 10 + n, user_hash = "other", content = $"回复 {n}<br>正文<br>正文<br>正文<br>正文" }).ToList();
            replies.Insert(0, new { id = 9999999, user_hash = "Tips", now = "2099-01-01 00:00:01", content = "tips-link-fixture：<a href='/f/婆罗门一'>任意蓝字</a>" });
            return Response(JsonSerializer.Serialize(new { id, fid = 4, user_hash = "po", now = "2026-10-29 12:00:00", ReplyCount = 190,
                title = $"预览标题 {id}", content = $"预览主楼 {id}", img = id == 2000 ? "fixture/photo" : "", ext = ".png", Replies = replies }));
        }
        private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>举报与弹窗标题的局部验证；所有 GET/POST 都只经过内存处理器。</summary>
internal static class ReportChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new FixtureHandler();
        var store = new LocalStore(Path.Combine(output, "report-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        vm.State.ShowImages = false;
        vm.State.Forums.Add(new(4, "综合版1", "综合")); // 故意不放值班室，离线目录也必须路由到 18。
        vm.State.Drafts.Add(new("new:4", 4, 0, "", "普通发串草稿", "", false));
        vm.State.Drafts.Add(new("reply:101", 4, 101, "", "普通回应草稿", "", false));
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        window.Show();
        try
        {
            foreach (var (dialog, expected) in new (Window, string)[]
            {
                (new CookieWindow(vm), "饼干管理"), (new SubscriptionWindow(vm), "订阅号管理"),
                (new SettingsWindow(vm), "设置"), (new ComposerWindow(vm, 0), "发串"),
                (new ComposerWindow(vm, 101), "参与回应"), (new TextWindow("发送预览", ""), "发送预览"),
                (new DraftWindow(vm, _ => { }), "草稿箱")
            })
            {
                Require(dialog.Title == expected, "弹窗标题未统一：" + dialog.Title);
                dialog.Owner = window; dialog.ShowInTaskbar = false; dialog.Show(); await Pause();
                Require(UiRefinementChecks.Descendants<TextBlock>(dialog).Any(t => t.Text == expected), "可见弹窗标题与窗口标题不一致");
                if (expected == "发串") Require(UiRefinementChecks.Descendants<TextBlock>(dialog).Any(t => t.Text == "发串"), "发串页标题未改名");
                dialog.Close();
            }

            await vm.OpenThreadAsync(101); await Pause();
            var reader = (ListBox)window.FindName("ReaderList");
            var originalPosition = vm.PageText;
            foreach (var target in new long[] { 101, 102 })
            {
                var body = UiRefinementChecks.Descendants<PostBody>(reader).First(b => b.DataContext is ReadPost p && p.Post.Id == target);
                var menu = body.ContextMenu; menu.PlacementTarget = body; menu.IsOpen = true; await Pause();
                Require(menu.Items.OfType<MenuItem>().Select(i => i.Header.ToString()).SequenceEqual(new[] { "复制串号", "复制正文", "回应", "举报" }), "串内菜单缺少举报或破坏已有操作");
                Task? check = null;
                _ = window.Dispatcher.BeginInvoke(() => check = EditOpenedReportAsync(window, target, output, capture), DispatcherPriority.Background);
                menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "举报")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if (check is not null) await check;
                Require(vm.State.Drafts.Any(d => d.Key == $"report:{target}" && d.ReportPostId == target && d.ForumId == 18 && d.ThreadId == 0), "主楼/回应的举报目标或独立草稿错误");
            }
            var tip = UiRefinementChecks.Descendants<PostBody>(reader).First(b => b.DataContext is ReadPost { IsTip: true });
            tip.ContextMenu.PlacementTarget = tip; tip.ContextMenu.IsOpen = true; await Pause();
            Require(tip.ContextMenu.Items.OfType<MenuItem>().All(i => !Equals(i.Header, "举报")), "官方 Tips 不应有举报入口"); tip.ContextMenu.IsOpen = false;
            Require(handler.Posts == 0 && vm.Original?.Id == 101 && vm.PageText == originalPosition, "打开举报时发送了请求或改变了阅读目标");

            // 模拟重启后从草稿箱恢复：理由和目标分开持久化，引用只能拼接一次。
            var saved = store.Load().Drafts.Single(d => d.Key == "report:102");
            var report = new ComposerWindow(vm, saved.ThreadId, existing: saved) { Owner = window, ShowInTaskbar = false };
            report.Show(); await Pause();
            try
            {
                var reason = UiRefinementChecks.Descendants<EmojiEditor>(report).Single();
                Require(report.Title == "举报" && reason.Text == "测试理由 102", "举报草稿恢复丢失目标或理由");
                Require(reason.MaxLength + ">>No.102\n举报理由：".Length == 10000, "自动引用未计入发送字数限制");
                reason.Text = "  \n "; Click(report, "预览并发送");
                Require(handler.Posts == 0 && Text(report).Contains("请填写举报理由"), "空白举报理由没有拦截");
                reason.Text = "测试理由 102"; Click(report, "预览并发送");
                Require(handler.Posts == 0 && Text(report).Contains("请先导入"), "没有饼干仍可发送举报");
            }
            finally { report.Close(); }

            var cookie = new CookieIdentity("fixture", "测试饼干", SecretProtection.Protect(new string('a', 32)));
            vm.State.Cookies.Add(cookie); vm.State.ActiveCookieId = cookie.Id;
            report = new ComposerWindow(vm, 0, existing: store.Load().Drafts.Single(d => d.Key == "report:102")) { Owner = window, ShowInTaskbar = false };
            report.Show(); await Pause();
            try
            {
                await PreviewAsync(report, confirm: false);
                Require(handler.Posts == 0 && !report.Delivered, "取消预览仍发送了举报");
                handler.Fail = true;
                await PreviewAsync(report, confirm: true); await Until(() => handler.Posts == 1 && Text(report).Contains("模拟发送失败"));
                Require(!report.Delivered && store.Load().Drafts.Any(d => d.Key == "report:102"), "发送失败丢失举报草稿");
                await Task.Delay(250); Require(handler.Posts == 1, "失败后自动重复发送");
                handler.Fail = false;
                await PreviewAsync(report, confirm: true); await Until(() => report.Delivered);
                Require(handler.Posts == 2 && handler.Path == "/Home/Forum/doPostThread.html", "举报没有通过发新串接口提交");
                Require(handler.Fields["fid"] == "18" && !handler.Fields.ContainsKey("resto"), "举报被错误发送到原串或当前板块");
                Require(handler.Fields["content"] == ">>No.102\n举报理由：测试理由 102", "举报楼号或理由提交不正确");
                Require(!store.Load().Drafts.Any(d => d.Key == "report:102") && store.Load().Drafts.Any(d => d.Key == "report:101"), "成功后没有仅清理对应举报草稿");
            }
            finally { if (report.IsVisible) report.Close(); }
            Require(store.Load().Drafts.Any(d => d.Key == "new:4" && d.Content == "普通发串草稿") && store.Load().Drafts.Any(d => d.Key == "reply:101" && d.Content == "普通回应草稿"), "举报覆盖了普通草稿");
            File.WriteAllText(Path.Combine(output, "report-results.txt"), "PASS: titles; post/reply menu targets; tips excluded; fixed fid=18; independent persistent drafts; reason/cookie validation; preview cancel; failure retention/no retry; confirmed success cleanup. All requests simulated.");
            Console.WriteLine(File.ReadAllText(Path.Combine(output, "report-results.txt")));
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }

    private static async Task EditOpenedReportAsync(MainWindow window, long target, string output, Action<Window, string> capture)
    {
        await Until(() => window.OwnedWindows.OfType<ComposerWindow>().Any());
        var report = window.OwnedWindows.OfType<ComposerWindow>().Single();
        try
        {
            Require(report.Title == "举报" && Text(report).Contains($"No.{target} · 值班室"), "举报页目标不正确");
            Require(!UiRefinementChecks.Descendants<ComboBox>(report).Any(c => System.Windows.Automation.AutomationProperties.GetName(c) == "发串版块" && c.IsVisible), "举报目的地不应允许切换");
            var reason = UiRefinementChecks.Descendants<EmojiEditor>(report).Single(); reason.Text = $"测试理由 {target}";
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); await Pause();
                if (target == 102) capture(report, Path.Combine(output, $"report-{theme}.png"));
            }
            report.Width = report.MinWidth; await Pause();
            if (target == 102) capture(report, Path.Combine(output, "report-narrow.png"));
        }
        finally { report.Close(); App.ApplyTheme("light"); }
    }

    private static async Task PreviewAsync(ComposerWindow report, bool confirm)
    {
        Task? check = null;
        async Task CheckAsync()
        {
            await Until(() => report.OwnedWindows.OfType<TextWindow>().Any());
            var preview = report.OwnedWindows.OfType<TextWindow>().Single();
            try
            {
                var body = UiRefinementChecks.Descendants<PostBody>(preview).Single();
                var text = EmojiText.Read(body.Document.ContentStart, body.Document.ContentEnd);
                Require(text.Contains("值班室 · 举报 No.102") && text.Contains(">>No.102") && text.Contains("测试理由 102"), "预览遗漏实际目标或理由");
                if (confirm) Click(preview, "确认发送"); else preview.Close();
            }
            finally { if (preview.IsVisible) preview.Close(); }
        }
        _ = report.Dispatcher.BeginInvoke(() => check = CheckAsync(), DispatcherPriority.Background);
        Click(report, "预览并发送");
        if (check is not null) await check;
    }
    private static void Click(Window window, string text) => UiRefinementChecks.Descendants<Button>(window).Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Text(Window window) => string.Join("\n", UiRefinementChecks.Descendants<TextBlock>(window).Select(t => t.Text));
    private static Task Pause() => Task.Delay(90);
    private static async Task Until(Func<bool> predicate) { for (var i = 0; i < 60 && !predicate(); i++) await Task.Delay(50); Require(predicate(), "等待 UI 操作超时"); }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int Posts;
        public bool Fail;
        public string Path = "";
        public Dictionary<string, string> Fields = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts++; Path = request.RequestUri!.AbsolutePath; Fields = [];
                foreach (var part in (MultipartFormDataContent)request.Content!) Fields[part.Headers.ContentDisposition!.Name!.Trim('"')] = await part.ReadAsStringAsync(ct);
                await Task.Delay(70, ct);
                return Response(Fail ? "<p class=\"error\">模拟发送失败</p>" : "<p class=\"success\">发串成功</p>");
            }
            if (request.RequestUri!.AbsolutePath == "/Forum") return Response("<html></html>");
            return Response("""{"id":101,"fid":4,"user_hash":"PO","ReplyCount":2,"content":"主楼","Replies":[{"id":102,"user_hash":"Reply","content":"回应"},{"id":9999999,"user_hash":"Tips","now":"2099-01-01 00:00:01","content":"官方提示"}]}""");
        }
        private static HttpResponseMessage Response(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content) };
    }
}

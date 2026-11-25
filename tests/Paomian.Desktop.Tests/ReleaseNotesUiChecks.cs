using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ReleaseNotesUiChecks
{
    private const string Body = "## 更新内容\n\n### 发言\n\n附图可以随草稿保存。\n\n- 支持**粘贴**截图。\n- 查看[发布说明](https://example.invalid/notes)。\n";
    private static readonly string LongBody = Body + string.Join('\n', Enumerable.Range(1, 50).Select(i => $"- 模拟更新条目 {i}：在当前窗口查看内容。"));

    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var transport = new Releases();
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), new LocalStore(Path.Combine(output, Guid.NewGuid().ToString("N"))),
            new ReleaseUpdater(new HttpClient(transport)), "1.0.4");
        var updates = vm.Updates;
        SettingsWindow Open()
        {
            var window = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false }; window.Show();
            All<ListBox>(window).Single(b => AutomationProperties.GetName(b) == "设置分类").SelectedItem = "关于";
            window.UpdateLayout(); return window;
        }
        var settings = Open();
        SettingsReleaseNotes Panel() => All<SettingsReleaseNotes>(settings).Single();
        bool Has(string text) => All<TextBlock>(Panel()).Any(b => b.Text == text);
        try
        {
            var check = Button(settings, "检查更新"); Click(check); Click(check);
            Require(transport.Checks == 1 && !check.IsEnabled && !Panel().IsVisible, "rapid checks duplicated or notes shown before detection");
            transport.AllowCheck.SetResult();
            await Until(() => Has("正在获取更新内容…"));
            Require(Has("v1.0.6") && Button(settings, "下载更新").IsEnabled && !updates.Busy, "latest body or download blocked by history");
            settings.Close(); settings = Open();
            Require(transport.Lists == 1, "reopen duplicated in-flight request");
            transport.AllowList.SetResult();
            await Until(() => Has("暂时无法获取更新内容"));
            settings.Close(); settings = Open();
            Require(Has("暂时无法获取更新内容") && Has("v1.0.6") && transport.Lists == 1 && Button(settings, "下载更新").IsEnabled, "error not isolated/cached");
            var retry = Links(Panel()).Single(l => AutomationProperties.GetName(l) == "重试");
            retry.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            await Until(() => Has("v1.0.5"));
            var headings = All<TextBlock>(Panel()).Where(b => b.Text.StartsWith('v')).ToArray();
            Require(headings.Select(b => b.Text).SequenceEqual(["v1.0.6", "v1.0.5"]) && Has("此版本未提供更新说明"), "order/empty body");
            Require(All<TextBlock>(Panel()).Any(b => b.Inlines.OfType<Bold>().Any()) && Links(Panel()).Single().ToolTip as string == "https://example.invalid/notes", "Markdown formatting/link");
            Require(All<TextBlock>(Panel()).Any(b => b.Text == "发言" && b.FontWeight == FontWeights.SemiBold) && Has("•"), "Markdown headings/bullets");
            Require(!All<ScrollViewer>(Panel()).Any(), "nested notes scroll");
            var scroll = All<ScrollViewer>(settings).Single(s => AutomationProperties.GetName(s) == "设置内容");
            var nav = All<ListBox>(settings).Single(b => AutomationProperties.GetName(b) == "设置分类");
            foreach (var mode in new[] { "light", "dark" })
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                App.ApplyTheme(mode); UiCopyChecks.SetDpi(settings, scale, scale == 1 ? 880 : 660, scale == 1 ? 690 : 470);
                await Task.Delay(70); settings.UpdateLayout();
                var footer = Button(settings, "完成"); var y = footer.TranslatePoint(new(), settings).Y;
                scroll.ScrollToTop(); settings.UpdateLayout();
                var title = All<TextBlock>(Panel()).First(b => b.Text == "更新内容");
                Require(Links(Panel()).Single().Foreground == settings.FindResource("Accent") && title.Foreground == settings.FindResource("Text"), "theme brushes");
                Require(title.TranslatePoint(new(), settings).Y > Button(settings, "检查更新").TranslatePoint(new(), settings).Y && scroll.ScrollableHeight > 0 &&
                    scroll.ExtentWidth <= scroll.ViewportWidth + 1 && Math.Abs(VisualTreeHelper.GetDpi(settings).DpiScaleX - scale) < .01, "placement/scroll/DPI");
                scroll.ScrollToVerticalOffset(Panel().TranslatePoint(new(), scroll).Y - 60); settings.UpdateLayout();
                capture(settings, Path.Combine(output, $"notes-{mode}-{scale:0.##}.png"));
                scroll.ScrollToEnd(); settings.UpdateLayout();
                var toggle = All<CheckBox>(settings).Single(b => AutomationProperties.GetName(b) == "自动下载，退出时安装更新");
                var toggleY = toggle.TranslatePoint(new(), scroll).Y;
                Require(toggleY >= 0 && toggleY + toggle.ActualHeight <= scroll.ViewportHeight + 1 && Math.Abs(y - footer.TranslatePoint(new(), settings).Y) < 1, "switch not reachable or fixed footer moved");
            }
            nav.SelectedItem = "阅读"; nav.SelectedItem = "关于"; await updates.CheckAsync();
            Require(transport.Lists == 2 && Has("v1.0.5"), "successful history cache lost");
            CheckLinks();

            nav.SelectedItem = "阅读"; transport.TargetBody = Body; transport.MiddleBody = "- 调整了附图的预览布局。";
            await updates.CheckAsync();
            Require(transport.Lists == 2 && updates.Notes?.Phase == NotesPhase.NotLoaded, "hidden About requested history");
            UiCopyChecks.SetDpi(settings, 1.25, 880, 690); nav.SelectedItem = "关于";
            await Until(() => Has("调整了附图的预览布局。"));
            var section = All<TextBlock>(settings).Single(b => b.IsVisible && b.Text == "软件更新");
            scroll.ScrollToVerticalOffset(section.TranslatePoint(new(), scroll).Y - 8); settings.UpdateLayout();
            capture(settings, Path.Combine(output, "about-release-notes.png"));

            transport.Latest = "1.0.7"; transport.AllowList = Releases.Gate(); await updates.CheckAsync();
            await Until(() => Has("v1.0.7") && Has("正在获取更新内容…"));
            Require(!Has("v1.0.6"), "stale history during target change");
            settings.Close(); transport.Latest = "1.0.4"; await updates.CheckAsync();
            transport.AllowList.SetResult(); settings = Open(); await Task.Delay(60);
            Require(!Panel().IsVisible && updates.Notes is null && updates.Status.StartsWith("已是最新版本"), "stale result after close/up-to-date");

            transport.Latest = "1.0.6"; transport.AllowList = Releases.Gate(); vm.State.InstallUpdatesOnExit = true;
            await updates.CheckAsync(automatic: true); vm.State.InstallUpdatesOnExit = false;
            Require(updates.Staged is not null && Button(settings, "重启安装").IsVisible && Has("正在获取更新内容…"), "auto-download/staging waited for notes");
            var profile = new LocalStore(vm.Store.Root);
            var staged = profile.ReadUpgradeNotes().Single();
            Require(!staged.Complete && staged.Entries.Single().Body == transport.TargetBody, "staging omitted the available target notes");
            transport.AllowList.SetResult(); await Until(() => Has("v1.0.5"));
            var pending = new PendingUpdateNotice(new Version(1, 0, 4), new Version(1, 0, 6));
            var saved = StartupNotes.FromLocal(pending, [], profile.ReadUpgradeNotes());
            Require(saved.IsComplete && !saved.NeedsOnline && saved.Entries.Select(n => n.Body).SequenceEqual([transport.TargetBody, transport.MiddleBody]),
                "late history failed to complete the offline upgrade notes");
            Click(Button(settings, "完成")); Require(!settings.IsVisible, "Done did not close");
            var beforeDownload = new Releases { Lists = 1, MiddleBody = "先取得历史说明" };
            beforeDownload.AllowCheck.SetResult();
            beforeDownload.AllowList.SetResult();
            var otherStore = new LocalStore(Path.Combine(output, Guid.NewGuid().ToString("N")));
            using var other = new MainViewModel(UiRefinementChecks.CreateApi(), otherStore, new ReleaseUpdater(new HttpClient(beforeDownload)), "1.0.4");
            await other.Updates.CheckAsync();
            await other.Updates.LoadNotesAsync();
            Require(otherStore.ReadUpgradeNotes().Count == 0, "checking available updates wrote an installation snapshot");
            await other.Updates.DownloadAsync();
            var ready = StartupNotes.FromLocal(pending, [], new LocalStore(otherStore.Root).ReadUpgradeNotes());
            Require(ready.IsComplete && ready.Entries.Select(n => n.Body).SequenceEqual([beforeDownload.TargetBody, beforeDownload.MiddleBody]),
                "verified download lost history already obtained in About");
            other.Updates.Exit();
            Console.WriteLine("PASS release notes WPF: pending, dedup/cache, close/reopen, failure/retry, ordered real controls, Markdown and safe links, 2 themes x 3 DPI, scroll/footer, stale target, verified mock auto-download without installation");
        }
        finally { vm.State.InstallUpdatesOnExit = false; settings.Close(); updates.Exit(); App.ApplyTheme("light"); }
    }

    private static void CheckLinks()
    {
        var opened = new List<string>(); var panel = new StackPanel(); var page = new Uri(ReleaseUpdater.Repository + "/releases/tag/v1.0.6");
        ReleaseMarkdown.Append(panel, new(new Version(1, 0, 6), "[网页](https://example.invalid/page) [本地](file:///not-opened) " +
            "[协议](custom:ignored) ![图片](https://example.invalid/img) <a href='file:///ignored'>原文</a> `代码`", page, null), opened.Add);
        Links(panel).Single().RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        Require(opened.SequenceEqual(["https://example.invalid/page"]) && !All<Image>(panel).Any(), "unsafe link or embedded content");
        panel.Children.Clear(); ReleaseMarkdown.Append(panel, new(new Version(1, 0, 6), new string('长', 18000), page, null), opened.Add);
        Links(panel).Single().RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        Require(opened.Last() == page.AbsoluteUri, "long release has no complete source");
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static IEnumerable<Hyperlink> Links(DependencyObject panel) => All<TextBlock>(panel).SelectMany(b => b.Inlines.OfType<Hyperlink>());
    private static Button Button(Window w, string text) => All<Button>(w).Single(b => Equals(b.Content, text));
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static async Task Until(Func<bool> ready) { for (var i = 0; i < 150 && !ready(); i++) await Task.Delay(20); Require(ready(), "timed out"); }
    private static void Require(bool ok, string why) { if (!ok) throw new InvalidOperationException(why); }

    private sealed class Releases : HttpMessageHandler
    {
        internal string Latest = "1.0.6";
        internal string TargetBody = LongBody, MiddleBody = "";
        internal int Checks, Lists;
        internal static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource AllowCheck = Gate();
        internal TaskCompletionSource AllowList = Gate();
        private readonly byte[] _zip = MakePackage();
        private static byte[] MakePackage()
        {
            var fake = Encoding.UTF8.GetBytes("inert update fixture"); using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                using (var entry = zip.CreateEntry(AppIdentity.ExecutableName).Open()) entry.Write(fake);
                using var writer = new StreamWriter(zip.CreateEntry("SHA256SUMS.txt").Open()); writer.WriteLine(Convert.ToHexString(SHA256.HashData(fake)) + "  " + AppIdentity.ExecutableName);
            }
            return stream.ToArray();
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!;
            if (url.AbsoluteUri == ReleaseUpdater.Endpoint)
            {
                Interlocked.Increment(ref Checks); await AllowCheck.Task.WaitAsync(ct);
                var name = AppIdentity.PackageName(Latest);
                return Ok(new { tag_name = "v" + Latest, draft = false, prerelease = false, body = TargetBody, assets = new[] {
                    new { name, browser_download_url = $"{ReleaseUpdater.Repository}/releases/download/v{Latest}/{name}", digest = "sha256:" + Convert.ToHexString(SHA256.HashData(_zip)), size = _zip.Length } } });
            }
            if (url.Host == "api.github.com" && url.AbsolutePath.EndsWith("/releases"))
            {
                var attempt = Interlocked.Increment(ref Lists); await AllowList.Task; // 允许模拟取消后迟到的响应。
                if (attempt == 1) return new(HttpStatusCode.TooManyRequests);
                return Ok(new[] { new { tag_name = "1.0.5", draft = false, prerelease = false, body = MiddleBody } });
            }
            if (url.AbsoluteUri == $"{ReleaseUpdater.Repository}/releases/download/v1.0.6/{AppIdentity.PackageName("1.0.6")}")
                return new(HttpStatusCode.OK) { Content = new ByteArrayContent(_zip) };
            throw new InvalidOperationException("Unexpected fixture request: " + url);
        }
        private static HttpResponseMessage Ok(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
    }
}

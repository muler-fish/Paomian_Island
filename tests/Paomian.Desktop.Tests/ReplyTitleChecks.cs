using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ReplyTitleChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture, Action<bool, string> check)
    {
        var transport = new Fixture();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(transport)), new LocalStore(Path.Combine(output, "titles-" + Guid.NewGuid().ToString("N"))));
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        window.Show();
        var list = (ListBox)window.FindName("ReaderList");
        IncrementalScroll.SetCommand(list, null); IncrementalScroll.SetPreviousCommand(list, null);
        var reports = new List<object>();
        async Task Inspect(string stage)
        {
            check(vm.ReadingPosts.Count > 0, $"{stage}: 没有帖子");
            foreach (var item in vm.ReadingPosts.ToArray())
            {
                list.ScrollIntoView(item); window.UpdateLayout(); await Task.Delay(40);
                var container = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(item);
                if (container is null) { check(false, $"{stage}: 未实现容器 {item.Post.Id}"); continue; }
                var title = All<EmojiLabel>(container).Single(t => t.Name == "OriginalPostTitle");
                var author = All<EmojiLabel>(container).Single(t => t.Name == "ReaderAuthor");
                var body = All<PostBody>(container).Single();
                var expected = PostCard.Optional(item.Post.Title, "无标题", vm.State.HideSpoilers);
                var name = PostCard.Optional(item.Post.Name, "无名氏", vm.State.HideSpoilers);
                // 根据 fixture 的楼号独立校验，API 或离线缓存丢字段也必须失败。
                var fixtureTitle = item.IsOriginal ? "串首标题" : Fixture.TitleFor((int)((item.Post.Id - 12345679) % 19));
                check(item.Post.Title == (fixtureTitle ?? ""), $"{stage}/{item.Post.Id}: 模型标题与接口样本不同");
                check(title.Text == expected && title.IsVisible == (expected.Length > 0), $"{stage}/{item.Post.Id}: 标题遗漏或占位值可见");
                check(author.Text == name && author.IsVisible == (name.Length > 0), $"{stage}: 名称规则改变");
                var panel = (StackPanel)title.Parent;
                if (expected.Length == 0)
                {
                    var height = panel.ActualHeight; var y = body.TranslatePoint(new Point(), panel).Y;
                    var index = panel.Children.IndexOf(title); panel.Children.Remove(title); window.UpdateLayout();
                    check(Math.Abs(panel.ActualHeight - height) < .1 && Math.Abs(body.TranslatePoint(new Point(), panel).Y - y) < .1, $"{stage}: 无标题增加空白");
                    panel.Children.Insert(index, title); window.UpdateLayout();
                }
                else
                {
                    check(title.FontSize == vm.ReadingFontSize && title.FontWeight == FontWeights.SemiBold && title.TextWrapping == TextWrapping.Wrap, $"{stage}: 标题样式不同");
                    check(title.ActualWidth <= panel.ActualWidth && title.TranslatePoint(new Point(0, title.ActualHeight), panel).Y <= body.TranslatePoint(new Point(), panel).Y, $"{stage}: 标题溢出或覆盖正文");
                    if (name.Length > 0) check(title.TranslatePoint(new Point(), panel).Y < author.TranslatePoint(new Point(), panel).Y, $"{stage}: 标题与名称顺序错误");
                    if (item.Post.Title.Length > 100) check(title.ActualHeight > title.FontSize * 2, $"{stage}: 长标题没有换行");
                }
                var identity = All<PostIdentity>(container).Single();
                check(identity.Post == item.Post && identity.IsPo == item.IsPo, $"{stage}: 身份绑定改变");
                reports.Add(new { stage, item.Post.Id, title = title.Text, name = author.Text, height = panel.ActualHeight, titleHeight = title.ActualHeight, item.IsPo });
            }
        }
        try
        {
            foreach (var theme in new[] { "light", "dark" })
                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                {
                    App.ApplyTheme(theme); UiCopyChecks.SetDpi(window, scale, 1200, 760);
                    await vm.OpenThreadAsync(12345678, 1, refresh: true); await Inspect($"{theme}-{scale}-page1");
                    list.ScrollIntoView(vm.ReadingPosts[1]); await Task.Delay(40); capture(window, Path.Combine(output, $"titles-{theme}-{scale}.png"));
                }
            await vm.OpenThreadAsync(12345678, 2, refresh: true); await Inspect("jump-page2");
            await vm.LoadPreviousThreadAsync(); await Inspect("prepend-page1");
            await vm.LoadMoreThreadAsync(); await Inspect("append-page3");
            check(vm.ReadingPosts.Any(p => p.Page == 1) && vm.ReadingPosts.Any(p => p.Page == 3), "双向续页未覆盖");
            vm.OnlyPo = true; check(vm.ReadingPosts.All(p => p.IsPo || p.IsOriginal), "只看 PO 过滤错误"); await Inspect("only-po"); vm.OnlyPo = false;
            vm.State.HideSpoilers = true; vm.AppearanceChanged(); await Inspect("hidden-title");
            vm.State.HideSpoilers = false; vm.AppearanceChanged();
            transport.Offline = true;
            await vm.OpenThreadAsync(12345678, 1, refresh: true); check(vm.Status.Contains("离线"), "没有进入离线缓存"); await Inspect("offline-cache");
            var post = vm.ReadingPosts.First(p => !p.IsOriginal && p.Post.Title.Length > 0).Post;
            var reference = new TextWindow("引用预览", "", referencePreview: true, readingFontSize: vm.ReadingFontSize) { Owner = window };
            reference.SetPost(post); reference.Show(); await Task.Delay(80);
            check(All<EmojiLabel>(reference).Any(t => t.IsVisible && t.Text == post.Title), "引用预览标题遗漏");
            capture(reference, Path.Combine(output, "reference-title.png")); reference.Close();
        }
        finally { window.Close(); }
        File.WriteAllText(Path.Combine(output, "titles.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private sealed class Fixture : HttpMessageHandler
    {
        internal bool Offline;
        private static readonly string?[] Titles =
        [
            "泡面岛 v1.1.0 更新", "只有标题", "", null, "无标题",
            string.Concat(Enumerable.Repeat("很长的标题 & < > 「测试」😀 ", 12)), "PO 的标题", "[h]隐藏标题[/h]"
        ];
        internal static string? TitleFor(int index) => Titles[index % Titles.Length];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("只读 fixture");
            if (Offline) throw new HttpRequestException("模拟离线");
            var page = request.RequestUri!.Query.Contains("page=2") ? 2 : request.RequestUri.Query.Contains("page=3") ? 3 : 1;
            var replies = Enumerable.Range(0, 19).Select(i => new { id = 12345679 + (page - 1) * 19 + i, fid = 4, user_hash = i % 2 == 0 ? "PO测试" : "其他人", title = TitleFor(i), name = i % 4 is 0 or 2 ? "更新内容" : "无名氏", content = "阅读与滚动……", admin = i == 5 ? 1 : 0, sage = i == 6 ? 1 : 0, now = "2026-11-25 10:00:00" });
            var data = new { id = 12345678, fid = 4, user_hash = "PO测试", title = "串首标题", name = "串首名称", content = "主楼正文", ReplyCount = 57, Replies = replies };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}

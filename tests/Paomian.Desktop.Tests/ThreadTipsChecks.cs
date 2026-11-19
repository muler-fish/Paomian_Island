using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ThreadTipsChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new TipsHandler();
        var store = new LocalStore(Path.Combine(output, "tips-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show(); await vm.OpenThreadAsync(123, 2); await Task.Delay(180);
            Require(vm.ReadingPosts.Count == 22 && vm.ReadingPosts.Count(p => p.IsTip) == 3, "同页多个提示没有保留");
            Require(vm.ReadingPosts[0].IsTip && vm.ReadingPosts[6].IsTip && vm.ReadingPosts[^1].IsTip, "提示插入位置错误");
            Require(vm.ReadingPosts.Where(p => !p.IsTip).Select(p => p.Index).SequenceEqual(Enumerable.Range(0, 19)), "提示改变了正常回复的索引");
            var reader = (ListBox)window.FindName("ReaderList");
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); await Task.Delay(80);
                var row = (ListBoxItem)reader.ItemContainerGenerator.ContainerFromItem(vm.ReadingPosts[0]);
                Require(UiRefinementChecks.Descendants<TextBlock>(row).Any(t => t.IsVisible && t.Text == "Tips"), "提示标签不可见");
                Require(!UiRefinementChecks.Descendants<PostIdentity>(row).Any(t => t.IsVisible) && !UiRefinementChecks.Descendants<Button>(row).Any(t => t.IsVisible), "提示仍显示饼干/楼号/引用操作");
                var body = UiRefinementChecks.Descendants<PostBody>(row).Single();
                Require(EmojiText.Read(body.Document).Contains("第 2 页的官方提示"), "提示正文缺失");
                Require(body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Hyperlink>().Count() == 1, "提示安全链接处理错误");
                capture(window, Path.Combine(output, "tips-" + theme + ".png"));
            }
            App.ApplyTheme("light");
            vm.OnlyPo = true;
            Require(vm.ReadingPosts.Count(p => p.IsTip) == 3 && vm.ReadingPosts.All(p => p.IsTip || p.IsPo), "只看 PO 错误丢失提示或混入其他回复");
            vm.OnlyPo = false;
            var normal = vm.ReadingPosts.First(p => !p.IsTip);
            vm.SaveReadingPosition(normal, 12);
            var position = store.Load().History.Single().Position;
            vm.SaveReadingPosition(vm.ReadingPosts[0], 40);
            Require(store.Load().History.Single().Position == position && position?.PostId == 2001, "提示覆盖正常阅读进度");

            await vm.LoadMoreThreadAsync();
            Require(vm.ReadingPosts.Count(p => p.IsTip) == 6 && vm.ThreadPageLabel == "第 2–3 / 3 页", "相同提示 ID 在续页时被去重或影响页数");
            await vm.LoadPreviousThreadAsync(); await Task.Delay(150);
            Require(vm.ReadingPosts.Count(p => p.IsTip) == 9 && vm.ReadingPosts.Count(p => p.IsOriginal) == 1, "前插页丢失提示或主楼重复");
            Require(vm.ReadingPosts.Contains(normal) && ReferenceEquals(normal, vm.ReadingPosts.First(p => p.Post.Id == 2001)), "前插/续页重建了旧回复");
            Require(vm.ReadingPosts.Where(p => p.IsTip).Select(p => p.Page).SequenceEqual(new[] {1,1,1,2,2,2,3,3,3}), "各页提示顺序不稳定");
            vm.OnlyPo = true; vm.OnlyPo = false;
            Require(vm.ReadingPosts.Count(p => p.IsTip) == 9, "切换过滤造成提示累积重复");
            await vm.OpenThreadAsync(123, 2, refresh: true);
            Require(vm.ReadingPosts.Count(p => p.IsTip) == 3 && vm.ReadingPosts.All(p => p.Page == 2), "跳页没有替换提示集合");
            Require(store.ReadPage(123, 2, "")?.Tips is { Count: 3 }, "提示未随页缓存");
            handler.Offline = true;
            await vm.OpenThreadAsync(123, 2, refresh: true);
            Require(vm.ReadingPosts.Count(p => p.IsTip) == 3 && vm.Status.StartsWith("离线阅读"), "离线缓存未恢复提示");
            handler.Offline = false; handler.WithTips = false;
            await vm.OpenThreadAsync(456, 1);
            Require(vm.ReadingPosts.Count == 20 && !vm.ReadingPosts.Any(p => p.IsTip), "没有投放提示时仍遗留旧串提示");
            File.WriteAllText(Path.Combine(output, "tips-results.json"), JsonSerializer.Serialize(new {
                OriginalOrder = true, MultiplePerPage = true, RepeatedIdsAcrossPages = true, PoFiltering = true,
                NoQuoteOrAuthor = true, SafeLinks = true, ProgressUnaffected = true, PagingUnaffected = true,
                JumpReplaces = true, OfflineCache = true, NoSyntheticTips = true, Themes = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: tips positions, repeated IDs, PO filtering, themes, safe links, progress, pagination, jump and offline cache.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class TipsHandler : HttpMessageHandler
    {
        public bool Offline, WithTips = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Offline) throw new HttpRequestException("offline fixture");
            var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(s => s.Split('=')).ToDictionary(s => s[0], s => s[1]);
            var page = int.Parse(query.GetValueOrDefault("page", "1"));
            var replies = new List<object>();
            for (var i = 0; i <= 19; i++)
            {
                if (WithTips && i is 0 or 5 or 19)
                    replies.Add(new { id = 9999999, user_hash = "Tips", now = "2099-01-01 00:00:01", title = "Tips", admin = 1,
                        content = $"第 {page} 页的官方提示 😀<br><a href=\"https://www.nmbxd1.com/t/52752005\">查看版规</a> · <a href=\"javascript:alert(1)\">普通文本</a>" });
                if (i < 19) replies.Add(new { id = page * 1000 + i + 1, user_hash = i % 4 == 0 ? "Author" : "Other", now = "2026-10-26 12:00:00", content = $"第 {page} 页的回复 {i + 1}<br>正常正文和阅读位置保持不变。" });
            }
            var value = new { id = long.Parse(query["id"]), user_hash = "Author", content = "主楼", title = "官方提示显示检查", ReplyCount = 57, Replies = replies };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
        }
    }
}

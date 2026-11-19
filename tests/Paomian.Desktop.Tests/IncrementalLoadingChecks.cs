using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class IncrementalLoadingChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new PagingHandler();
        var store = new LocalStore(Path.Combine(output, "incremental-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show();
            var list = (ListBox)window.FindName("ThreadList");
            var reader = (ListBox)window.FindName("ReaderList");
            await vm.SelectForumAsync(new(4, "连续浏览测试", "测试", MaxPage: 6));
            await Pause();
            Require(vm.Posts.Count == 20 && handler.ListCalls == 1, "首屏在滚动之前只能加载第一页。");
            var first = vm.Posts[0];
            Bottom(list); await Until(() => handler.ListSecondStarted, "版块滚动未自动请求第二页");
            await Pause(); var anchor = Anchor(list);
            Require(vm.ListAppending && vm.Posts.Count == 20, "续页等待期间必须保留已显示的内容。");
            for (var i = 0; i < 8; i++) { Bottom(list); await vm.LoadMoreListAsync(); }
            Require(handler.ListCalls == 2, "同一续页被并发请求。");
            capture(window, Path.Combine(output, "forum-loading.png"));
            handler.ListRelease.TrySetResult();
            await Until(() => !vm.ListBusy, "版块续页没有结束"); await Pause();
            capture(window, Path.Combine(output, "forum-appended.png"));
            Require(vm.Posts.Count == 38 && ReferenceEquals(first, vm.Posts[0]), "跨页去重或增量追加失败。");
            Require(AnchorStayed(list, anchor), "追加串列表后阅读位置跳动。");
            Require(handler.ListCalls == 2, "追加后滚动位置跟随到了新底部，自动读穿后续页。");
            Bottom(list); await Until(() => !vm.HasMoreList, "版块空页未停止续载");
            var listCalls = handler.ListCalls;
            Bottom(list); await Pause();
            Require(handler.ListCalls == listCalls && vm.Posts.Count == 38, "空页后继续重复请求或丢失旧列表。");
            await vm.RefreshListAsync();
            Require(vm.Posts.Count == 20 && handler.ListCalls == listCalls + 1, "手动刷新应重新从第一页获取。");
            Console.WriteLine("PASS: forum bottom trigger, serial requests, overlap deduplication, stable anchor, empty end, refresh.");

            vm.State.History = Enumerable.Range(1, 47).Select(i => new SavedThread(Item(30000 + i), 1, DateTimeOffset.UtcNow.AddMinutes(-i))).ToList();
            vm.ShowLibrary("history"); await Pause();
            Require(vm.Posts.Count == 20, "历史首批大小不正确。");
            Bottom(list); await Until(() => vm.Posts.Count == 40, "历史未自动续载");
            Bottom(list); await Until(() => vm.Posts.Count == 47, "历史末批未自动显示");
            Require(!vm.HasMoreList && handler.ListCalls == listCalls + 1, "历史分页不应请求网络。");
            Console.WriteLine("PASS: history uses the same scroll interaction without network requests.");

            await vm.OpenThreadAsync(1000, 1); await Pause();
            var original = vm.ReadingPosts[0];
            var resets = 0;
            vm.ReadingPosts.CollectionChanged += (_, args) => { if (args.Action == NotifyCollectionChangedAction.Reset) resets++; };
            Bottom(reader); await Until(() => handler.ThreadSecondStarted, "回复滚动未自动请求第二页");
            await Pause(); anchor = Anchor(reader);
            Require(vm.ThreadAppending && vm.ReadingPosts.Count == 20 && vm.CanInteractThread, "续载时正文与回应操作应继续可用。");
            capture(window, Path.Combine(output, "thread-loading.png"));
            handler.ThreadRelease.TrySetResult();
            await Until(() => !vm.ThreadBusy, "回复续页没有结束"); await Pause();
            Require(vm.ReadingPosts.Count == 38 && resets == 0 && ReferenceEquals(original, vm.ReadingPosts[0]), "回复追加清空了旧容器，或重复了主楼/跨页回复。");
            Require(AnchorStayed(reader, anchor), "追加回复后阅读位置跳动。");
            Require(handler.ThreadCalls == 2, "读串追加后自动跳到了新的底部。");
            handler.FailThreadThird = true;
            Bottom(reader); await Until(() => vm.ThreadAppendFailed, "续载失败缺少重试状态");
            var failedCalls = handler.ThreadCalls;
            Bottom(reader); await Pause();
            Require(handler.ThreadCalls == failedCalls && vm.ReadingPosts.Count == 38, "失败后自动重试或清空旧正文。");
            capture(window, Path.Combine(output, "thread-retry.png"));
            handler.FailThreadThird = false;
            vm.RetryThreadLoadCommand.Execute(null);
            await Until(() => !vm.ThreadBusy && !vm.HasMoreThread, "手动重试未完成末页");
            Require(vm.ReadingPosts.Count == 57 && vm.ReadingPosts.Count(p => p.IsOriginal) == 1, "重试后回复数量或主楼数量不正确。");
            Require(handler.ThreadCalls == failedCalls + 1 && !vm.ThreadAppendFailed && resets == 0, "重试应只请求失败页且增量追加。");
            Console.WriteLine("PASS: thread bottom trigger, stable anchor, no collection reset, retry without data loss, terminal page.");

            vm.OnlyPo = true;
            await vm.OpenThreadAsync(2000, 1);
            await Until(() => !vm.ThreadBusy && !vm.HasMoreThread, "PO 筛选后的短页面没有继续寻找下一页回复");
            Require(vm.ReadingPosts.Count == 2 && vm.ReadingPosts[1].IsPo, "PO 过滤空页的自动续载不正确。");
            App.ApplyTheme("dark"); await Pause();
            capture(window, Path.Combine(output, "thread-dark.png")); App.ApplyTheme("light");
            Console.WriteLine("PASS: PO filtering keeps loading through pages without matching replies.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
        await SubscriptionScrollAsync(owner, output, capture);
        await PartialSubscriptionRetryAsync(output);
        await CancellationAsync(output);
        File.WriteAllText(Path.Combine(output, "incremental-results.json"), JsonSerializer.Serialize(new
        {
            Forum = true, Timeline = true, Subscriptions = 950, History = 47, Replies = true,
            StableScrollAnchor = true, NoCollectionResetOnAppend = true, SerialRequests = true,
            EmptyEnd = true, Retry = true, PartialSubscriptionRetry = true, StaleResponseRejected = true, Virtualization = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task SubscriptionScrollAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var store = new LocalStore(Path.Combine(output, "subscription-scroll-profile"));
        store.Save(new UserState { SubscriptionAccounts = [new("a", "测试", SecretProtection.Protect("incremental-test")), new("b", "另一号", SecretProtection.Protect("second-test"))], ActiveSubscriptionId = "a" });
        store.SaveSubscriptions("incremental-test", "", new(Enumerable.Range(1, 950).Select(i => Item(50000 + i)).ToList(), true, DateTimeOffset.UtcNow, []));
        store.SaveSubscriptions("second-test", "", new([Item(60000)], true, DateTimeOffset.UtcNow, []));
        var handler = new PagingHandler();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show(); await vm.ShowSubscriptionsAsync(); await Pause();
            var list = (ListBox)window.FindName("ThreadList");
            Require(vm.Posts.Count == 10, "订阅首屏应分批显示。");
            var first = vm.Posts[0];
            Bottom(list); await Until(() => vm.Posts.Count == 20, "订阅滚到底后未自动续载");
            await Pause();
            Require(ReferenceEquals(first, vm.Posts[0]) && vm.Posts.Select(p => p.Post.Id).Distinct().Count() == 20, "订阅追加丢失或重复旧内容。");
            // 按同一入口追加到末尾，验证大列表的控件仍然虚拟化，而非创建 950 个楼层。
            while (vm.HasMoreList) await vm.LoadMoreListAsync();
            await Pause();
            var containers = UiRefinementChecks.Descendants<ListBoxItem>(list).Count();
            Require(vm.Posts.Count == 950 && containers < 100 && handler.TotalCalls == 0, "950 条缓存续载未虚拟化或意外发起网络请求。");
            capture(window, Path.Combine(output, "subscriptions-950.png"));
            vm.ActiveSubscriptionKey = "b"; await Pause();
            Require(vm.Posts.Single().Post.Id == 60000 && !vm.HasMoreList, "切订阅号未重置续载列表。");
            Console.WriteLine($"PASS: 950 subscriptions, cache-only append, {containers} realized containers, account switch.");
        }
        finally { window.Close(); }
    }

    private static async Task CancellationAsync(string output)
    {
        var handler = new PagingHandler();
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(Path.Combine(output, "switch-profile")));
        await vm.SelectForumAsync(new(4, "甲", "测试"));
        var pending = vm.LoadMoreListAsync();
        await Until(() => handler.ListSecondStarted, "未开始旧版块续载");
        await vm.SelectForumAsync(new(5, "乙", "时间线", IsTimeline: true, MaxPage: 2));
        handler.ListRelease.TrySetResult(); await pending;
        Require(vm.Posts.All(p => p.Post.Id >= 70000) && vm.Posts.Count == 20, "旧版块续页覆盖了新时间线。");
        await vm.LoadMoreListAsync();
        Require(vm.Posts.Count == 40 && !vm.HasMoreList, "时间线末页没有停止。");
        var failing = new Forum(4, "重试", "测试");
        await vm.SelectForumAsync(failing);
        handler.FailListSecond = true;
        await vm.LoadMoreListAsync();
        Require(vm.ListAppendFailed && vm.Posts.Count == 20, "版块续载失败必须保留已读列表。");
        var calls = handler.ListCalls;
        await vm.LoadMoreListAsync();
        Require(handler.ListCalls == calls, "版块失败后不应自动重试。");
        handler.FailListSecond = false;
        await vm.RetryListLoadAsync();
        Require(vm.Posts.Count == 38 && !vm.ListAppendFailed, "版块重试未追加失败页。");
        await vm.OpenThreadAsync(1000, 1); pending = vm.LoadMoreThreadAsync();
        await Until(() => handler.ThreadSecondStarted, "未开始旧串续载");
        await vm.OpenThreadAsync(3000, 1); handler.ThreadRelease.TrySetResult(); await pending;
        Require(vm.Original?.Id == 3000 && vm.ReadingPosts.Count == 1, "旧串续页写入了新串。");
        Console.WriteLine("PASS: timeline maximum page and late forum/thread response isolation.");
    }

    private static async Task PartialSubscriptionRetryAsync(string output)
    {
        var store = new LocalStore(Path.Combine(output, "partial-retry-profile"));
        store.Save(new UserState { SubscriptionAccounts = [new("a", "测试", SecretProtection.Protect("partial-test"))], ActiveSubscriptionId = "a" });
        store.SaveSubscriptions("partial-test", "", new(Enumerable.Range(1, 25).Select(i => Item(80000 + i)).ToList(), false, DateTimeOffset.UtcNow, []));
        var handler = new PartialFeed();
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var loading = vm.ShowSubscriptionsAsync();
        await Until(() => handler.SecondCalls == 1, "部分缓存复查未开始");
        await vm.LoadMoreListAsync(); await vm.LoadMoreListAsync();
        Require(vm.Posts.Count == 25, "后台复查第一页时丢弃了未读完的缓存尾部。");
        var last = vm.Posts[^1];
        handler.InitialRelease.TrySetResult(); await loading;
        Require(vm.ListAppendFailed && vm.Posts.Count == 25, "部分订阅同步失败未提供尾部重试。");
        var retry = vm.RetryListLoadAsync();
        await Until(() => handler.SecondCalls == 2, "部分订阅重试没有启动");
        Require(vm.Posts.Count == 25 && ReferenceEquals(last, vm.Posts[^1]), "重试从第一页开始时清空了已读尾部。");
        handler.RetryRelease.TrySetResult(); await retry;
        await vm.LoadMoreListAsync();
        Require(vm.SubscriptionCount == "35" && vm.Posts.Count == 35 && !vm.HasMoreList && !vm.ListAppendFailed, "部分订阅重试未恢复完整列表。");
        Console.WriteLine("PASS: partial subscription retry preserves cached tail and resumes append after synchronization.");
    }

    private static ScrollViewer Viewer(ListBox list) => UiRefinementChecks.Descendants<ScrollViewer>(list).First();
    private static void Bottom(ListBox list) { list.UpdateLayout(); Viewer(list).ScrollToEnd(); }
    private static (object Item, double Y) Anchor(ListBox list)
    {
        var item = UiRefinementChecks.Descendants<ListBoxItem>(list)
            .First(c => c.ActualHeight > 0 && c.TransformToAncestor(list).Transform(new Point()).Y >= 0);
        return (item.DataContext, item.TransformToAncestor(list).Transform(new Point()).Y);
    }
    private static bool AnchorStayed(ListBox list, (object Item, double Y) anchor)
    {
        list.UpdateLayout();
        return list.ItemContainerGenerator.ContainerFromItem(anchor.Item) is ListBoxItem item &&
            Math.Abs(item.TransformToAncestor(list).Transform(new Point()).Y - anchor.Y) < 3;
    }
    private static Task Pause() => Task.Delay(240);
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var i = 0; i < 250; i++) { if (condition()) return; await Task.Delay(20); }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static Post Item(int id) => new() { Id = id, Fid = 4, UserHash = "Reader1", Title = $"连续浏览测试 {id}", Content = "用于检查滚动和位置保持的测试内容，内容只在隔离测试目录中使用。", Time = "2026-10-24(六)12:00:00" };

    private sealed class PagingHandler : HttpMessageHandler
    {
        public int TotalCalls, ListCalls, ThreadCalls;
        public bool ListSecondStarted, ThreadSecondStarted, FailThreadThird, FailListSecond;
        public TaskCompletionSource ListRelease { get; } = new();
        public TaskCompletionSource ThreadRelease { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            TotalCalls++;
            var uri = request.RequestUri!;
            var query = uri.Query.TrimStart('?').Split('&').Select(v => v.Split('=', 2)).ToDictionary(p => p[0], p => p[1]);
            var page = int.Parse(query.GetValueOrDefault("page", "1"));
            var id = int.Parse(query.GetValueOrDefault("id", "0"));
            if (uri.AbsolutePath.EndsWith("showf") || uri.AbsolutePath.EndsWith("timeline"))
            {
                ListCalls++;
                if (id == 5) return Json(Enumerable.Range(70000 + (page - 1) * 20, 20).Select(ApiPost));
                if (page == 2) { ListSecondStarted = true; await ListRelease.Task; if (FailListSecond) throw new HttpRequestException("Synthetic list failure"); }
                return Json(page >= 3 ? [] : Enumerable.Range(page == 1 ? 1 : 19, 20).Select(ApiPost).ToArray());
            }
            if (uri.AbsolutePath.EndsWith("thread"))
            {
                ThreadCalls++;
                if (id == 1000 && page == 2) { ThreadSecondStarted = true; await ThreadRelease.Task; }
                if (id == 1000 && page == 3 && FailThreadThird) throw new HttpRequestException("Synthetic failure");
                var replies = id == 3000 ? [] : Enumerable.Range(page == 1 ? 1 : page == 2 ? 19 : 38, 19)
                    .Select(n => new { id = id * 100 + n, user_hash = id == 2000 && page == 3 && n == 38 ? "Author" : "Other", content = "回复正文<br>第二行<br>第三行<br>第四行<br>第五行", now = "2026-10-24(六)12:00:00" }).ToArray();
                return Json(new { id, user_hash = "Author", content = "连续阅读主楼", ReplyCount = id == 3000 ? 0 : 57, Replies = replies });
            }
            throw new InvalidOperationException("Unexpected test request");
        }
        private static object ApiPost(int id) => new { id, fid = 4, user_hash = "Reader1", title = $"连续浏览测试 {id}", content = "用于检查滚动和位置保持的测试内容，内容只在隔离测试目录中使用。", now = "2026-10-24(六)12:00:00" };
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }

    private sealed class PartialFeed : HttpMessageHandler
    {
        public int SecondCalls;
        public TaskCompletionSource InitialRelease { get; } = new();
        public TaskCompletionSource RetryRelease { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var page = int.Parse(request.RequestUri!.Query.Split("page=")[1]);
            if (page == 2)
            {
                if (++SecondCalls == 1) { await InitialRelease.Task; throw new HttpRequestException("Synthetic feed failure"); }
                await RetryRelease.Task;
            }
            var entries = Enumerable.Range(1, 35).Skip((page - 1) * 10).Take(10)
                .Select(n => new { id = 80000 + n, fid = 4, user_hash = "Reader1", title = $"连续浏览测试 {80000 + n}", content = "测试订阅内容" });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(entries)) };
        }
    }
}

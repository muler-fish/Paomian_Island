using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ReadingProgressChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var store = new LocalStore(Path.Combine(output, "progress-profile"));
        var handler = new ProgressHandler();
        var vm = CreateModel();
        var window = CreateWindow();
        MainViewModel CreateModel() => new(new XIslandApi(new HttpClient(handler)), store);
        MainWindow CreateWindow() => new(vm) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show(); await vm.OpenThreadAsync(100000, 2); await Task.Delay(200);
            var reader = (ListBox)window.FindName("ReaderList");
            var viewer = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            viewer.ScrollToVerticalOffset(520); await Task.Delay(900);
            await vm.LoadMoreThreadAsync(); await Task.Delay(900);
            capture(window, Path.Combine(output, "progress-after-prefetch.png"));
            var saved = store.Load().History.Single();
            File.WriteAllText(Path.Combine(output, "progress-observation.json"), JsonSerializer.Serialize(new { saved.Page, vm.ThreadPageLabel, Offset = viewer.VerticalOffset }));
            Require(saved.Page == 2, "仍在第 2 页阅读，后台加载第 3 页却把保存进度改成了第 3 页。");
            Require(saved.Position is { PostId: > 2001, Offset: > 0 }, "没有保存实际回复和回复内偏移。");
            var before = Visible(reader);
            await vm.LoadPreviousThreadAsync(); await Task.Delay(200);
            Require(Same(Visible(reader), before), "前插页改变阅读位置。");
            window.Close();
            saved = store.Load().History.Single();
            Require(saved.Page == 2, "前插页或关闭改变了保存页码。");

            vm = CreateModel(); window = CreateWindow(); window.Show();
            await vm.OpenThreadAsync(100000); await Task.Delay(250);
            reader = (ListBox)window.FindName("ReaderList"); viewer = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            capture(window, Path.Combine(output, "progress-restored.png"));
            Require(Same(Visible(reader), before), $"重开程序没有恢复到原回复及偏移。Expected={before}, Actual={Visible(reader)}, Saved={saved}");
            await vm.OpenThreadAsync(100000, refresh: true); await Task.Delay(250);
            Require(Same(Visible(reader), before), "刷新没有保留当前阅读位置。");

            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.DelayResponse = release.Task;
            var refresh = vm.OpenThreadAsync(100000, refresh: true);
            UserScroll(reader, viewer, 850); await Task.Delay(80);
            var latest = Visible(reader);
            release.SetResult(); await refresh; await Task.Delay(180);
            Require(!Same(latest, before) && Same(latest, Visible(reader)), "慢刷新覆盖了等待期间的阅读位移。");
            handler.DelayResponse = Task.CompletedTask;

            // 刚滚动就切串，尚未等到节流计时器，也应由导航前补存。
            UserScroll(reader, viewer, 1100); await Task.Delay(60); before = Visible(reader);
            await vm.OpenThreadAsync(200000, 1); await Task.Delay(100);
            await vm.OpenThreadAsync(100000); await Task.Delay(250);
            Require(Same(Visible(reader), before), "快速切串丢失最后一次滚动。");

            await vm.OpenThreadAsync(100000, 4); await Task.Delay(180);
            Require(Visible(reader).Id == 4001 && Math.Abs(Visible(reader).Top) < 2, "显式跳页错误套用了旧进度。");
            vm.OnlyPo = true; await Task.Delay(120);
            UserScroll(reader, viewer, 120); await Task.Delay(60); before = Visible(reader);
            window.Close(); // 不等 750ms；验证关闭补存和过滤状态。
            Require(store.Load().History.First(h => h.Post.Id == 100000).Position?.OnlyPo == true, "只看 PO 状态未保存。");
            vm = CreateModel(); window = CreateWindow(); window.Show(); await vm.OpenThreadAsync(100000); await Task.Delay(250);
            reader = (ListBox)window.FindName("ReaderList"); viewer = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            Require(vm.OnlyPo && Same(Visible(reader), before), "关闭重开未恢复 PO 模式及阅读位置。");
            // 取消恢复锁后，新的滚动不会被图片/布局纠偏强拉回旧位置。
            UserScroll(reader, viewer, 0); await Task.Delay(180);
            Require(Visible(reader).Id == 4004, "用户重新操作后仍被恢复逻辑拉回。");
            window.Close();

            // 旧版仅存页码、目标回复被删，以及过大的旧偏移，都应可继续阅读。
            var state = store.Load(); state.History = [new(new Post { Id = 100000 }, 3, DateTimeOffset.Now)]; store.Save(state);
            vm = CreateModel(); window = CreateWindow(); window.Show(); await vm.OpenThreadAsync(100000); await Task.Delay(200);
            reader = (ListBox)window.FindName("ReaderList");
            Require(Visible(reader).Id == 3001, "旧版历史页码没有兼容。"); window.Close();
            state = store.Load(); state.History[0] = state.History[0] with { Position = new(999999, 5, 50000, false) }; store.Save(state);
            vm = CreateModel(); window = CreateWindow(); window.Show(); await vm.OpenThreadAsync(100000); await Task.Delay(250);
            reader = (ListBox)window.FindName("ReaderList");
            Require(Visible(reader).Id == 3006 && Math.Abs(Visible(reader).Top) < 2, "已删除回复未回退至同页近邻顶部。");
            window.Close();
            state = store.Load(); state.History[0] = state.History[0] with { Page = 6, Position = new(6019, 18, 50000, false) }; store.Save(state);
            vm = CreateModel(); window = CreateWindow(); window.Show(); await vm.OpenThreadAsync(100000); await Task.Delay(350);
            reader = (ListBox)window.FindName("ReaderList");
            Require(UiRefinementChecks.Descendants<ListBoxItem>(reader).Any(c => c.DataContext is ReadPost p && p.Post.Id == 6019), "末页旧偏移导致内容不可见。");
            var result = new { PrefetchPreservesProgress = true, RestartRestoresReplyAndOffset = true, RefreshPreservesPosition = true,
                QuickSwitchFlushes = true, CloseFlushes = true, ExplicitJumpWins = true, PoModeRestored = true, LegacyHistory = true,
                DeletedReplyFallback = true, EndPageClamped = true };
            File.WriteAllText(Path.Combine(output, "progress-results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: actual viewport progress, prefetch/prepend, restart, refresh, quick switch/close, explicit jump, PO filter, legacy history, deleted reply and oversized offset.");
        }
        finally { window.Close(); }
    }
    private static void UserScroll(ListBox reader, ScrollViewer viewer, double offset)
    {
        reader.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(reader), Environment.TickCount, Key.Left) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        viewer.ScrollToVerticalOffset(offset);
    }
    private static (long Id, double Top) Visible(ListBox reader)
    {
        var anchor = ReadingAnchor.Capture(reader) ?? throw new InvalidOperationException("正文没有可见回复。");
        return (((ReadPost)anchor.Item).Post.Id, anchor.Top);
    }
    private static bool Same((long Id, double Top) a, (long Id, double Top) b) => a.Id == b.Id && Math.Abs(a.Top - b.Top) < 3;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ProgressHandler : HttpMessageHandler
    {
        internal Task DelayResponse { get; set; } = Task.CompletedTask;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await DelayResponse.WaitAsync(cancellationToken);
            var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(s => s.Split('=')).ToDictionary(s => s[0], s => s[1]);
            var page = int.Parse(query.GetValueOrDefault("page", "1"));
            var value = new { id = long.Parse(query["id"]), title = "阅读进度测试", content = "主楼", user_hash = "Author", ReplyCount = 114,
                Replies = Enumerable.Range(1, 19).Select(i => new { id = page * 1000 + i, user_hash = i % 4 == 0 ? "Author" : "Reader", content = $"第 {page} 页第 {i} 条回复<br>" + string.Join("<br>", Enumerable.Repeat("用于测试实际阅读位置。", 2 + i % 4)) }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        }
    }
}

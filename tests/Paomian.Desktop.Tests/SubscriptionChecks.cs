using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class SubscriptionChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new CloudHandler(); var store = new LocalStore(Path.Combine(output, "subscription-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show(); vm.SetSubscriptionId("Test-A");
            Require(SecretProtection.Unprotect(store.Load().SubscriptionAccounts.Single(a => a.Id == vm.ActiveSubscriptionKey).EncryptedValue) == "Test-A", "号码未加密保存。");
            Require(!File.ReadAllText(Path.Combine(store.Root, "state.json")).Contains("Test-A"), "明文号码泄露到状态文件。");
            handler.Cloud["Test-A"] = Enumerable.Range(200, 12).Select(i => (long)i).ToList();
            await vm.OpenThreadAsync(211);
            await vm.LoadSubscriptionsAsync();
            Require(vm.SubscriptionKnown && vm.IsSubscribed && vm.SubscriptionCount == "12", "第二页已有订阅被误判为未订阅。");
            await vm.ShowSubscriptionsAsync();
            Require(vm.Posts.Count == 10 && vm.LoadMoreListCommand.CanExecute(null), "订阅续载未启用。");
            await vm.LoadMoreListAsync(); await Pause();
            Require(vm.Posts.Select(p => p.Post.Id).SequenceEqual(Enumerable.Range(200, 12).Select(i => (long)i)), "订阅续页没有保留前一页。");
            capture(window, Path.Combine(output, "subscriptions-page2.png"));
            await vm.ToggleSubscriptionAsync(); Require(!vm.IsSubscribed && !handler.Cloud["Test-A"].Contains(211), "取消订阅没有修改云列表。");
            await vm.ToggleSubscriptionAsync(); Require(vm.IsSubscribed && handler.Cloud["Test-A"].Contains(211), "添加订阅没有修改云列表。");
            await CheckRightClick(window, handler, output);

            // 服务端已写入但回包丢失：下次点击只读取，不会反向取消或自动重发。
            await vm.OpenThreadAsync(999); handler.LoseWriteResponse = true;
            var mutations = handler.Mutations;
            await vm.ToggleSubscriptionAsync();
            Require(!vm.CurrentSubscriptionKnown && handler.Cloud["Test-A"].Contains(999) && handler.Mutations == mutations + 1, "未知结果被当成成功或重试。");
            await vm.ToggleSubscriptionAsync();
            Require(vm.IsSubscribed && handler.Mutations == mutations + 1, "检查未知状态触发了第二次写入。");
            Require(vm.Error.Length == 0, "成功核对状态后仍显示上次订阅错误。");

            handler.FailPageTwo = true; await vm.LoadSubscriptionsAsync(true);
            Require(vm.SubscriptionKnown && vm.Posts.Count > 0 && vm.SubscriptionSyncStatus.StartsWith("缓存"), "刷新失败应保留完整旧快照并标注缓存。");
            handler.FailPageTwo = false;
            handler.HoldNextRead = true; var oldRead = vm.LoadSubscriptionsAsync(true);
            await handler.ReadStarted.Task;
            vm.SetSubscriptionId("Test-B"); handler.Cloud["Test-B"] = [301];
            await vm.LoadSubscriptionsAsync(true); handler.ReleaseRead.TrySetResult(true); await oldRead;
            Require(vm.SubscriptionKnown && vm.SubscriptionCount == "1" && vm.Posts.Single().Post.Id == 301, "切号后旧响应覆盖了新列表。");
            Require(vm.GetSubscriptionId() == "Test-B", "号码切换失败。");
            using (var second = new MainViewModel(new XIslandApi(new HttpClient(new CloudHandler(handler.Cloud))), store))
            {
                await second.OpenThreadAsync(301); Require(second.IsSubscribed, "重启或其他客户端没有读取同号云订阅。");
            }
            vm.State.Theme = "dark"; App.ApplyTheme("dark"); vm.AppearanceChanged();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            await Pause();
            capture(window, Path.Combine(output, "subscriptions-dark.png"));
            var dialog = new SubscriptionWindow(vm) { Owner = window, ShowInTaskbar = false }; dialog.Show(); await Pause();
            Program.RequireWindowFits(dialog); capture(dialog, Path.Combine(output, "subscription-settings-dark.png")); dialog.Close();
            App.ApplyTheme("light"); vm.State.Theme = "light"; vm.AppearanceChanged();
            var light = new SubscriptionWindow(vm) { Owner = window, ShowInTaskbar = false }; light.Show(); await Pause();
            capture(light, Path.Combine(output, "subscription-settings-light.png")); light.Close();
            File.WriteAllText(Path.Combine(output, "subscription-results.json"), JsonSerializer.Serialize(new
            {
                EncryptedId = true, FullPaging = true, CloudAddDelete = true, UnknownReconcilesWithoutWrite = true,
                FailedPageRejectsSnapshot = true, AccountSwitchIgnoresStale = true, ReloadReadsCloud = true,
                LeftClickToggles = true, KnownStatusSkipsFeed = true, UnknownStatusChecksFeed = true, RenewDeletesThenAdds = true,
                FailedDeleteStopsRenewal = true, FailedAddReconcilesRemoval = true, LostReplyReconcilesWithoutRetry = true,
                RapidClicksShareCommand = true, StaleReaderFeedbackSuppressed = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: subscription paging, left/right clicks, ordered renewal, failed deletes/adds, lost replies, rapid clicks and account/thread changes.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }
    private static Task Pause() => Task.Delay(120);
    private static async Task CheckRightClick(MainWindow window, CloudHandler cloud, string output)
    {
        var vm = window.Model;
        var button = (Button)window.FindName("ThreadSubscriptionButton");
        var feedback = new List<InteractionFeedback>();
        vm.FeedbackRaised += feedback.Add;
        try
        {
            var peer = (IInvokeProvider)new ButtonAutomationPeer(button);
            var number = vm.GetSubscriptionId();
            const string large = "Test-large";
            var members = Enumerable.Range(10_000, 1_000).Select(i => (long)i).ToList();
            cloud.Cloud[large] = members.ToList();
            vm.Store.SaveSubscriptions(large, vm.State.ActiveCookieId, new(members.Select(id => new Post { Id = id }).ToList(), true, DateTimeOffset.UtcNow, []));
            vm.SetSubscriptionId(large);
            await vm.OpenThreadAsync(members[^1]);
            Require(vm.CurrentSubscriptionKnown && vm.IsSubscribed, "未从完整缓存恢复串的订阅状态");
            var before = cloud.FeedReads;
            peer.Invoke(); await Until(() => !vm.IsSubscribed && !vm.SubscriptionBusy);
            peer.Invoke(); await Until(() => vm.IsSubscribed && !vm.SubscriptionBusy);
            var leftReads = cloud.FeedReads - before;
            before = cloud.FeedReads;
            var elapsed = Stopwatch.StartNew();
            await RunRight("重新订阅成功", "delFeed/10999", "addFeed/10999");
            var renewMs = elapsed.ElapsedMilliseconds;
            var renewReads = cloud.FeedReads - before;
            await vm.OpenThreadAsync(410);
            before = cloud.FeedReads;
            await RunRight("订阅成功", "addFeed/410");
            var addReads = cloud.FeedReads - before;
            File.WriteAllText(Path.Combine(output, "subscription-requests.json"), JsonSerializer.Serialize(new
            {
                Members = members.Count, LeftRemoveAndAddFeedPages = leftReads, RightRenewFeedPages = renewReads,
                RightAddFeedPages = addReads, RightRenewMilliseconds = renewMs
            }, new JsonSerializerOptions { WriteIndented = true }));
            Require(leftReads == 0 && renewReads == 0 && addReads == 0,
                $"已有 {members.Count} 条订阅时，左键取消再添加读了 {leftReads} 页，右键重新订阅读了 {renewReads} 页，右键添加读了 {addReads} 页。");

            // 部分快照中已出现的串可直接重订。没出现的串需先核对，核对失败不得写入。
            const string partial = "Test-partial";
            cloud.Cloud[partial] = [410, .. members.Take(10), 411];
            vm.Store.SaveSubscriptions(partial, vm.State.ActiveCookieId, new([new Post { Id = 410 }], false, DateTimeOffset.UtcNow, []));
            vm.SetSubscriptionId(partial);
            before = cloud.FeedReads;
            await RunRight("重新订阅成功", "delFeed/410", "addFeed/410");
            Require(cloud.FeedReads == before, "部分快照中已确认的成员仍触发整表读取");
            await vm.OpenThreadAsync(411);
            Require(!vm.CurrentSubscriptionKnown, "把部分快照中缺少的串错误判为未订阅");
            cloud.FailPageTwo = true;
            await RunRight("订阅暂时无法访问");
            cloud.FailPageTwo = false;
            before = cloud.FeedReads;
            await RunRight("重新订阅成功", "delFeed/411", "addFeed/411");
            Require(cloud.FeedReads - before == 3, "当前串未知时应完整核对一次");

            vm.SetSubscriptionId(number);
            await vm.OpenThreadAsync(410);
            peer.Invoke(); await Until(() => vm.IsSubscribed && !vm.SubscriptionBusy);
            peer.Invoke(); await Until(() => !vm.IsSubscribed && !vm.SubscriptionBusy);
            Require(cloud.Writes.TakeLast(2).SequenceEqual(["addFeed/410", "delFeed/410"]), "左键增删顺序错误");

            await RunRight("订阅成功", "addFeed/410");
            var reader = vm.ReadingPosts.First();
            await RunRight("重新订阅成功", "delFeed/410", "addFeed/410");
            Require(vm.IsSubscribed && vm.Posts.First().Post.Id == 410 && ReferenceEquals(reader, vm.ReadingPosts.First()) &&
                vm.Store.ReadSubscriptions(vm.GetSubscriptionId(), "")!.Posts.First().Id == 410, "重新订阅未同步列表、快照或重置了阅读区");
            // 同号在其他设备取消后，本次取消可能被服务端拒绝。核对实际结果后可再次右键。
            cloud.Cloud["Test-A"].Remove(410);
            cloud.Reject = "delFeed";
            await RunRight("当前串未订阅", "delFeed/410");
            Require(vm.CurrentSubscriptionKnown && !vm.IsSubscribed, "未校正其他客户端已取消的订阅");
            await RunRight("订阅成功", "addFeed/410");

            cloud.Reject = "delFeed";
            await RunRight("已停止重新订阅", "delFeed/410");
            Require(vm.CurrentSubscriptionKnown && vm.IsSubscribed, "取消失败破坏已有订阅");
            cloud.Reject = "addFeed";
            await RunRight("当前串未订阅", "delFeed/410", "addFeed/410");
            Require(vm.CurrentSubscriptionKnown && !vm.IsSubscribed && vm.Posts.All(p => p.Post.Id != 410), "取消成功后仍伪造已订阅");
            cloud.Reject = "addFeed";
            await RunRight("订阅未获确认", "addFeed/410");
            cloud.LoseWriteResponse = true;
            await RunRight("当前串已订阅", "addFeed/410");
            Require(vm.IsSubscribed && vm.CurrentSubscriptionKnown, "添加已生效但回包丢失时未核对云端");

            cloud.Reject = "addFeed"; cloud.FailReadAfterRejection = true;
            await RunRight("请刷新订阅列表核对状态", "delFeed/410", "addFeed/410");
            Require(!vm.CurrentSubscriptionKnown && !vm.IsSubscribed, "离线核对失败时把状态显示成确定");
            cloud.FailPageTwo = false; cloud.FailReadAfterRejection = false;
            await RunRight("订阅成功", "addFeed/410");

            var gate = cloud.HoldWrite("delFeed"); var start = cloud.Writes.Count;
            Right(); await Until(() => cloud.Writes.Count > start);
            Right(); vm.SubscriptionCommand.Execute(true); vm.SubscriptionCommand.Execute(null);
            Require(!button.IsEnabled && cloud.Writes.Count == start + 1, "连发或左键竞争导致重复写入");
            await vm.OpenThreadAsync(411);
            feedback.Clear(); gate.SetResult();
            await Until(() => !vm.SubscriptionBusy && button.IsEnabled);
            Require(cloud.Writes.Skip(start).SequenceEqual(["delFeed/410", "addFeed/410"]) && !vm.IsSubscribed &&
                !feedback.Any(f => f.Message == "重新订阅成功"), "切串后写入或提示落在新串");

            await vm.OpenThreadAsync(410);
            gate = cloud.HoldWrite("addFeed"); start = cloud.Writes.Count;
            Right(); await Until(() => cloud.Writes.Count == start + 2);
            vm.SetSubscriptionId("Test-switch"); await vm.LoadSubscriptionsAsync(true); gate.SetResult();
            await Until(() => button.IsEnabled);
            Require(!vm.IsSubscribed && cloud.Cloud["Test-switch"].Count == 0, "切号时第二步污染新订阅号");
            vm.SetSubscriptionId("Test-A");
            // 前一个号的回包被放弃，写前检查点要求重新核对。
            Require(!vm.CurrentSubscriptionKnown, "切回旧订阅号后丢失待核对状态");
            await vm.LoadSubscriptionsAsync(true);
            Require(vm.CurrentSubscriptionKnown && vm.IsSubscribed == cloud.Cloud["Test-A"].Contains(410), "切回旧号未与云端收敛");
        }
        finally { vm.FeedbackRaised -= feedback.Add; }

        void Right()
        {
            foreach (var routed in new[] { UIElement.PreviewMouseRightButtonDownEvent, UIElement.PreviewMouseRightButtonUpEvent })
            {
                var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right) { RoutedEvent = routed };
                button.RaiseEvent(e);
                Require(e.Handled, "订阅按钮的右键事件未被消费");
            }
        }

        async Task RunRight(string message, params string[] writes)
        {
            var start = cloud.Writes.Count; feedback.Clear(); Right();
            await Until(() => button.IsEnabled && feedback.Any(f => f.Area == FeedbackArea.Reader));
            Require(cloud.Writes.Skip(start).SequenceEqual(writes), "右键订阅请求顺序错误");
            var result = feedback.Single(f => f.Area == FeedbackArea.Reader);
            Require(message.EndsWith("成功") ? !result.IsError && result.Message == message :
                result.IsError && result.Message.Contains(message), "右键订阅提示错误：" + result.Message);
        }
        static async Task Until(Func<bool> done)
        {
            for (var i = 0; i < 200 && !done(); i++) await Task.Delay(15);
            Require(done(), "等待订阅按钮或模拟请求超时");
        }
    }

    private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    private sealed class CloudHandler : HttpMessageHandler
    {
        public Dictionary<string, List<long>> Cloud { get; }
        public CloudHandler(Dictionary<string, List<long>>? cloud = null) => Cloud = cloud ?? [];
        public bool LoseWriteResponse, FailPageTwo, HoldNextRead, FailReadAfterRejection;
        public int Mutations, FeedReads;
        public List<string> Writes { get; } = [];
        public string? Reject;
        private string? _hold;
        private TaskCompletionSource? _gate;
        internal TaskCompletionSource HoldWrite(string operation)
        {
            _hold = operation;
            return _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        public TaskCompletionSource<bool> ReadStarted { get; } = new();
        public TaskCompletionSource<bool> ReleaseRead { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var query = uri.Query.TrimStart('?').Split('&').Select(v => v.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            if (uri.AbsolutePath.EndsWith("thread")) return Json(JsonSerializer.Serialize(Post(long.Parse(query["id"]))));
            var id = query["uuid"]; Cloud.TryAdd(id, []);
            if (uri.AbsolutePath.EndsWith("feed"))
            {
                FeedReads++;
                var page = int.Parse(query["page"]);
                var snapshot = Cloud[id].Skip((page - 1) * 10).Take(10).Select(Post).ToArray();
                if (HoldNextRead) { HoldNextRead = false; ReadStarted.SetResult(true); await ReleaseRead.Task; }
                if (FailPageTwo && page == 2) return new(HttpStatusCode.ServiceUnavailable);
                return Json(JsonSerializer.Serialize(snapshot));
            }
            var operation = uri.Segments.Last();
            Require(operation is "addFeed" or "delFeed", "出现未知订阅写接口");
            Mutations++; var tid = long.Parse(query["tid"]); var add = operation == "addFeed";
            Writes.Add($"{operation}/{tid}");
            if (_hold == operation) { _hold = null; await _gate!.Task; }
            if (Reject == operation)
            {
                Reject = null; FailPageTwo = FailReadAfterRejection;
                return new(HttpStatusCode.ServiceUnavailable);
            }
            Cloud[id].Remove(tid); if (add) Cloud[id].Insert(0, tid);
            if (LoseWriteResponse) { LoseWriteResponse = false; throw new HttpRequestException("synthetic lost response"); }
            return Json(JsonSerializer.Serialize(add ? "订阅大成功" : "取消订阅成功"));
        }
        private static object Post(long id) => new { id, fid = 4, user_hash = "TestPo1", content = "云订阅测试内容", title = "测试串 " + id, reply_count = 1, now = "2026-10-23" };
        private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value) };
    }
}

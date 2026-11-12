using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>无需窗口的导航失败回归：临时错误保留历史，明确失效才移除，恢复后可以重试。</summary>
internal static class NavigationFailureChecks
{
    internal static async Task RunAsync(string output)
    {
        const long A = 69711052, B = 69711012, C = 69711070;
        var handler = new FailureHandler();
        var profile = Path.Combine(Path.GetFullPath(output), "profile-" + Guid.NewGuid().ToString("N"));
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(profile));
        foreach (var id in new[] { A, B, C }) await vm.OpenThreadAsync(id);
        Require(vm.Navigation.Count == 3 && vm.Original?.Id == C, "导航夹具未打开 A、B、C");

        foreach (var failure in new[]
        {
            "{\"error\":\"请求过于频繁，请稍后再试\"}",
            "\"服务暂时不可用，请稍后重试\"",
            "{\"error\":\"未知错误\"}",
            "{\"error\":\"该串不存在于缓存中，请稍后重试\"}",
            "{\"error\":\"维护期间提示该串不存在，请稍后重试\"}",
            "{\"error\":\"暂时无法确认该串已被删除，请稍后重试\"}",
            "{}",
            "[]",
            "{\"id\":99999999,\"fid\":4,\"content\":\"错误串正文\",\"Replies\":[]}"
        })
        {
            handler.Failure = failure; vm.ClearTemporaryCache();
            var calls = handler.Calls;
            await vm.GoBackAsync();
            Require(vm.Navigation.Count == 3 && vm.Navigation.Index == 2 && vm.CanGoBack,
                $"暂时失败不应移除后退历史：{failure}");
            Require(vm.Original?.Id == C && vm.ReadingThreadId == C && !vm.ThreadBusy && vm.HasError,
                "后退失败没有保留正文、恢复选中项或显示错误");
            Require(handler.Calls == calls + 1, "失败请求被自动重试");
            handler.Failure = null;
            await vm.GoBackAsync();
            Require(vm.Original?.Id == B && vm.Navigation.Index == 1 && vm.CanGoForward, "恢复后无法重试后退");
            await vm.GoForwardAsync();
            Require(vm.Original?.Id == C && vm.Navigation.Count == 3, "重试后前进历史改变");
        }

        await vm.GoBackAsync(); await vm.GoBackAsync();
        handler.Failure = "{\"error\":\"请求过于频繁，请稍后再试\"}"; vm.ClearTemporaryCache();
        await vm.GoForwardAsync();
        Require(vm.Original?.Id == A && vm.ReadingThreadId == A && vm.Navigation.Count == 3 && vm.Navigation.Index == 0 && vm.CanGoForward,
            "暂时失败不应移除前进历史");
        handler.Failure = null;
        await vm.GoForwardAsync(); await vm.GoForwardAsync();
        handler.Failure = "{\"success\":false,\"error\":\"该串不存在\"}"; vm.ClearTemporaryCache();
        await vm.GoBackAsync();
        Require(vm.Original?.Id == C && vm.ReadingThreadId == C && vm.Navigation.Entries.Select(e => e.ThreadId).SequenceEqual([A, C])
            && vm.Navigation.Index == 1 && vm.Error.Contains("已从后退/前进记录中移除"), "明确失效的串没有从历史移除");
        // 缓存读写移到后台后，离线回退、迟到结果和身份隔离必须保持原行为。
        handler.Failure = null; handler.NetworkDown = true; vm.ClearTemporaryCache();
        await vm.OpenThreadAsync(B, refresh: true);
        Require(vm.Original?.Id == B && vm.Status.StartsWith("离线阅读", StringComparison.Ordinal), "后台缓存读取未恢复离线正文");
        handler.NetworkDown = false;
        var gate = handler.Block(B);
        var stale = vm.OpenThreadAsync(B, refresh: true);
        await gate.Hit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vm.OpenThreadAsync(A, refresh: true);
        gate.Release.SetResult(); await stale;
        Require(vm.Original?.Id == A && vm.ReadingThreadId == A, "迟到请求覆盖了新串");
        gate = handler.Block(B);
        stale = vm.OpenThreadAsync(B, refresh: true);
        await gate.Hit.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.State.ActiveCookieId = "test-identity"; vm.ApplyCookie();
        gate.Release.SetResult(); await stale;
        Require(vm.Original is null && vm.ReadingThreadId is null && vm.Store.ReadPage(B, 1, "test-identity") is null,
            "身份切换后旧结果写进新身份或恢复旧正文");
        const long D = 69711080;
        await vm.OpenThreadAsync(D, refresh: true);
        Require(vm.Store.ReadPage(D, 1, "test-identity")?.Original.Id == D && vm.Store.ReadPage(D, 1, "") is null,
            "后台写盘没有使用固定的请求身份");
        Console.WriteLine("PASS: navigation failures preserve back/forward history and retry; confirmed missing threads are removed (no windows or live requests).");
        Console.WriteLine("PASS: background cache I/O retains offline fallback, cancellation and identity isolation.");
    }

    private sealed class Gate
    {
        internal TaskCompletionSource Hit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        internal string? Failure { get; set; }
        internal bool NetworkDown { get; set; }
        internal int Calls { get; private set; }
        private readonly Dictionary<long, Gate> _gates = [];
        internal Gate Block(long id) => _gates[id] = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || !request.RequestUri!.AbsolutePath.EndsWith("/thread", StringComparison.Ordinal))
                throw new InvalidOperationException("测试只允许模拟读串请求。");
            Calls++;
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var id = long.Parse(query["id"]!);
            if (_gates.Remove(id, out var gate)) { gate.Hit.SetResult(); await gate.Release.Task; }
            if (NetworkDown) throw new HttpRequestException("模拟离线");
            var body = Failure ?? JsonSerializer.Serialize(new { id, fid = 4, content = "模拟正文", Replies = Array.Empty<object>() });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Tide.Core;

/// <summary>串读取失败只在明确不存在或无权访问时使阅读历史失效，临时和未知错误保留重试机会。</summary>
internal static class ThreadFailureChecks
{
    internal static async Task RunAsync()
    {
        var failures = new List<string>();
        var count = 0;
        async Task Check(string name, string response, bool rejected, string? message = null, HttpStatusCode status = HttpStatusCode.OK)
        {
            count++;
            var calls = 0;
            using var api = new XIslandApi(new HttpClient(new FakeHandler((request, _) =>
            {
                calls++;
                if (request.Method != HttpMethod.Get || request.RequestUri!.PathAndQuery != "/api/thread?id=123&page=1")
                    throw new InvalidOperationException("没有经过串读取入口");
                return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(response, Encoding.UTF8) });
            })));
            try
            {
                await api.GetThreadAsync(123, 1);
                failures.Add(name + ": 未报告读取失败");
            }
            catch (IslandException ex)
            {
                if (ex.Rejected != rejected) failures.Add($"{name}: Rejected={ex.Rejected}，应为 {rejected}");
                if (message is not null && ex.Message != message) failures.Add(name + ": 服务端错误消息未保留");
                if (string.IsNullOrWhiteSpace(ex.Message)) failures.Add(name + ": 缺少可显示的错误提示");
            }
            if (calls != 1) failures.Add($"{name}: 发送了 {calls} 次请求");
        }

        // 同一业务错误可能通过 JSON 字符串或 error 对象返回，HTTP 200 不代表永久失效。
        foreach (var message in new[]
        {
            "请求过于频繁，请稍后再试", "服务器维护中，请稍后重试", "未知错误", "该串暂时无法访问",
            "该串暂时不存在于缓存中，请稍后重试", "维护期间暂时没有权限访问该串",
            "该串不存在于缓存中，请稍后重试", "维护期间提示该串不存在，请稍后重试",
            "暂时无法确认该串已被删除，请稍后重试"
        })
        {
            await Check("JSON string " + message, JsonSerializer.Serialize(message), false, message);
            await Check("JSON error " + message, JsonSerializer.Serialize(new { success = false, error = message }), false, message);
        }
        foreach (var message in new[]
        {
            "该串不存在", "该串不存在或已被删除，请检查串号", "该主题不存在，请刷新列表",
            "串已被删除", "该串已删除", "无权访问该串", "没有权限访问该串，请更换饼干",
            "您没有权限访问此串", "必须登入领取饼干后才可以访问"
        })
        {
            await Check("JSON string " + message, JsonSerializer.Serialize(message), true, message);
            await Check("JSON error " + message, JsonSerializer.Serialize(new { success = false, error = message }), true, message);
        }
        foreach (var response in new[]
        {
            "", "null", "[]", "{}", "{\"message\":\"维护中\"}", "{\"error\":null}",
            "{\"id\":null}", "{\"id\":0}", "{\"id\":\"bad\"}", "{\"id\":124}"
        })
            await Check("异常响应 " + response, response, false);
        await Check("HTTP 429", "slow down", false, status: HttpStatusCode.TooManyRequests);
        await Check("HTTP 500", "server error", false, status: HttpStatusCode.InternalServerError);
        await Check("HTML verification", "<html>verify</html>", false);
        await Check("HTML in JSON error", "{\"error\":\"<b>请求过于频繁</b>，请稍后再试\"}", false, "请求过于频繁，请稍后再试");

        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        Console.WriteLine($"PASS: {count} thread failure responses preserve transient errors, recognize unavailable threads, retain messages and never retry.");
    }
}

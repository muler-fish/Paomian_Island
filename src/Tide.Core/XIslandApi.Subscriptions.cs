using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Tide.Core;

/// <summary>订阅写入可能已完成，必须先重新读取云端状态，不能自动重试。</summary>
public sealed class SubscriptionUnknownException() : Exception("订阅操作结果未确认，请刷新订阅状态后再操作。");

public sealed partial class XIslandApi
{
    public static string NormalizeSubscriptionId(string value)
    {
        var id = value;
        // 任意可见文字、中文、符号与空格都原样发送；不 Trim 或改写大小写。
        if (string.IsNullOrEmpty(id) || id.Length > 4096 || id.Any(char.IsControl))
            throw new ArgumentException("请输入订阅号（最多 4096 字符，不能含换行或控制字符）。");
        return id;
    }

    /// <summary>完整读取后才提交快照，避免把“第一页没有”误判成未订阅。</summary>
    public async Task<List<Post>> GetSubscriptionsAsync(string subscriptionId, CancellationToken ct = default, Func<IReadOnlyList<Post>, Task>? pageLoaded = null)
    {
        var id = NormalizeSubscriptionId(subscriptionId);
        var result = new List<Post>(); var seen = new HashSet<long>();
        for (var page = 1; page <= 1000; page++)
        {
            using var doc = await SubscriptionRequestAsync($"feed?uuid={Uri.EscapeDataString(id)}&page={page}", false, ct);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new IslandException("未能读取订阅列表，请稍后刷新。");
            if (doc.RootElement.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object)) throw new IslandException("订阅列表包含无效数据，请稍后刷新。");
            var entries = doc.RootElement.EnumerateArray().Select(ParsePost).ToList();
            if (entries.Any(p => p.Id <= 0)) throw new IslandException("订阅列表包含无效数据，请稍后刷新。");
            var added = 0;
            foreach (var post in entries) if (seen.Add(post.Id)) { result.Add(post); added++; }
            // 一页可能因删除等原因不足 10 条，直到明确的空页才认为快照完整。
            if (entries.Count == 0) return result;
            if (added == 0) throw new IslandException("订阅分页重复，未能确认完整列表，请稍后刷新。");
            if (pageLoaded is not null) await pageLoaded(result);
        }
        throw new IslandException("订阅列表页数过多，未能完成同步。");
    }

    public async Task SetSubscriptionAsync(string subscriptionId, long threadId, bool subscribe, CancellationToken ct = default)
    {
        var id = NormalizeSubscriptionId(subscriptionId);
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        // 该站协议的增删接口虽然是 GET，仍按写操作处理：不缓存、不重试、不跟随重定向。
        using var doc = await SubscriptionRequestAsync($"{(subscribe ? "addFeed" : "delFeed")}?uuid={Uri.EscapeDataString(id)}&tid={threadId}", true, ct);
        if (doc.RootElement.ValueKind != JsonValueKind.String) throw new SubscriptionUnknownException();
        var text = doc.RootElement.GetString() ?? "";
        if (text == (subscribe ? "订阅大成功→_→" : "取消订阅成功!")) return;
        // 部分客户端使用包含判断；只接受明确成功短语，其他响应一律先核对云端状态。
        if (text.StartsWith(subscribe ? "订阅大成功" : "取消订阅成功", StringComparison.Ordinal)) return;
        throw new SubscriptionUnknownException();
    }

    private async Task<JsonDocument> SubscriptionRequestAsync(string path, bool mutation, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            if (_cookie.Length > 0) request.Headers.Add("Cookie", "userhash=" + _cookie);
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                if (mutation) throw new SubscriptionUnknownException();
                throw new IslandException(response.StatusCode == HttpStatusCode.TooManyRequests ? "订阅请求过于频繁，请稍后刷新。" : $"订阅暂时无法访问（HTTP {(int)response.StatusCode}），请稍后刷新。");
            }
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            // 不暴露包含 uuid 的请求 URL、响应正文或原始异常。
            if (mutation) throw new SubscriptionUnknownException();
            throw new IslandException("暂时无法同步订阅，请检查网络后刷新。");
        }
    }
}

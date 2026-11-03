using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tide.Core;

/// <summary>通知版本不是时间戳；正文转换为纯文本，用版本与内容共同区分更新。</summary>
public sealed record SiteNotice(string Version, string Content)
{
    [JsonIgnore] public string Key => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { Version, Content }))));
}

/// <summary>读取独立的站点通知源；不共享读串客户端，不携带饼干或重试请求。</summary>
public sealed class SiteNoticeClient : IDisposable
{
    public const string Endpoint = "https://nmb.ovear.info/nmb-notice.json";
    private const int MaxResponseBytes = 64 * 1024;
    private readonly HttpClient _http;

    // 注入客户端也由本类释放，用于固定响应测试。生产环境使用独立无 Cookie 的处理器。
    public SiteNoticeClient(HttpClient? http = null)
    {
        _http = http ?? NetworkClients.Create();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TideIsland/0.2 (+Windows; WPF)");
    }

    public async Task<SiteNotice?> GetAsync(CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new IslandException($"通知接口返回 HTTP {(int)response.StatusCode}。");
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new IslandException("通知数据过大。");
        // 限制解压后的实际长度；不能只信 Content-Length，也不让异常响应占用大量内存。
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token)) != 0)
        {
            if (buffer.Length + read > MaxResponseBytes) throw new IslandException("通知数据过大。");
            buffer.Write(chunk, 0, read);
        }
        return Parse(Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF'));
    }

    public static SiteNotice? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("通知数据不是对象。");
        if (!root.TryGetProperty("enable", out var enabled) || enabled.ValueKind is JsonValueKind.False or JsonValueKind.Null) return null;
        if (enabled.ValueKind != JsonValueKind.True) throw new JsonException("通知开关格式不正确。");
        if (!root.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null) return null;
        if (content.ValueKind != JsonValueKind.String) throw new JsonException("通知正文格式不正确。");
        // 源站用 <br> 换行；复用纯文本转换，不解释脚本、XAML 或外部资源。
        var text = ContentText.Plain(content.GetString() ?? "").Trim();
        if (text.Length == 0) return null;
        var version = "";
        if (root.TryGetProperty("date", out var date))
            version = date.ValueKind switch
            {
                JsonValueKind.Number => date.GetRawText(),
                JsonValueKind.String => date.GetString() ?? "",
                JsonValueKind.Null => "",
                _ => throw new JsonException("通知版本格式不正确。")
            };
        return new(version, text);
    }

    public void Dispose() => _http.Dispose();
}

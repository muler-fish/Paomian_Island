using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tide.Core;

/// <summary>可展示给用户的接口错误，例如访问受限、限流或明确的发言失败。</summary>
/// <remarks>Rejected 表示服务端正常应答但明确拒绝（串不存在、已删除、无权访问等）；HTTP 状态、限流和网页验证属于暂时性错误。</remarks>
public sealed class IslandException(string message, bool rejected = false) : Exception(message)
{
    public bool Rejected { get; } = rejected;
}
/// <summary>POST 可能已被服务端处理，但客户端无法确认结果；调用方应保留草稿，避免自动重发。</summary>
public sealed class DeliveryUnknownException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>封装 X 岛读接口和网页发言表单；不依赖 WPF，便于使用模拟 HTTP 传输验证协议。</summary>
public sealed partial class XIslandApi : IDisposable
{
    // 读取数据走 API 站，发言走主站的网页表单，两者不是同一组端点。
    public const string Site = "https://www.nmbxd1.com";
    public const string ApiBase = "https://api.nmb.best/api/";
    private readonly HttpClient _http;
    private string _cookie = "";
    // 注入的 HttpClient 也由本类释放；测试可注入自定义 HttpMessageHandler 替代真实网络。
    public XIslandApi(HttpClient? http = null)
    {
        _http = http ?? NetworkClients.Create();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TideIsland/0.1 (+Windows; WPF)");
    }
    public void SetCookie(string? cookie)
    {
        var value = string.IsNullOrWhiteSpace(cookie) ? "" : ContentText.NormalizeCookie(cookie);
        lock (_paginationGate) { if (_cookie != value) _paginationBaselines.Clear(); _cookie = value; }
    }

    /// <summary>展开普通版块分组，再合并时间线，供同一套侧栏导航使用。</summary>
    public async Task<List<Forum>> GetForumsAsync(CancellationToken ct = default)
    {
        using var groups = await GetJsonAsync("getForumList", ct);
        var forums = new List<Forum>();
        EnsureArray(groups.RootElement);
        foreach (var group in groups.RootElement.EnumerateArray())
        {
            if (!group.TryGetProperty("forums", out var children) || children.ValueKind != JsonValueKind.Array) continue;
            foreach (var f in children.EnumerateArray())
            {
                var id = Int(f, "id");
                if (id <= 0) continue;
                var label = Str(f, "showName").Trim();
                forums.Add(new(id, label.Length > 0 ? label : Str(f, "name"), Str(group, "name"), Str(f, "msg")) { UrlName = Str(f, "name") });
            }
        }
        using var timelines = await GetJsonAsync("getTimelineList", ct);
        EnsureArray(timelines.RootElement);
        var lines = timelines.RootElement.EnumerateArray().Select(t => new Forum(Int(t, "id"), Str(t, "name"), "时间线", Str(t, "notice"), true, Math.Max(1, Int(t, "max_page"))));
        return [.. lines, .. forums];
    }

    /// <summary>根据入口类型选择 timeline 或 showf，返回这一页的主串摘要。</summary>
    public async Task<List<Post>> GetThreadsAsync(Forum forum, int page, CancellationToken ct = default)
    {
        if (page < 1 || page > forum.MaxPage) throw new ArgumentOutOfRangeException(nameof(page));
        using var doc = await GetJsonAsync($"{(forum.IsTimeline ? "timeline" : "showf")}?id={forum.Id}&page={page}", ct);
        EnsureArray(doc.RootElement);
        return doc.RootElement.EnumerateArray().Select(ParsePost).Where(p => p.Id > 0).ToList();
    }

    /// <summary>读取主楼及指定页回复；追加到已有阅读内容的策略由视图模型负责。</summary>
    public async Task<ThreadPage> GetThreadAsync(long id, int page, CancellationToken ct = default)
    {
        if (id <= 0 || page <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        var cookie = _cookie;
        var result = await ReadThreadPageAsync(id, page, cookie, ct);
        if (page > 100 && result.Replies.Count > 0)
        {
            var baseline = ReadPaginationBaseline(id, cookie) ?? await ReadThreadPageAsync(id, 100, cookie, ct);
            RememberPaginationBaseline(id, cookie, baseline);
            if (result.Replies.Select(p => p.Id).SequenceEqual(baseline.Replies.Select(p => p.Id)))
                throw new IslandException($"X 岛未返回第 {page} 页，实际内容与第 100 页相同。" +
                    (cookie.Length == 0 ? "请在「饼干管理」导入有效饼干后重试。" : "请检查当前饼干是否有效，或稍后重试。"));
        }
        if (page == 100) RememberPaginationBaseline(id, cookie, result);
        return result with { PaginationVerified = true };
    }

    private async Task<ThreadPage> ReadThreadPageAsync(long id, int page, string cookie, CancellationToken ct)
    {
        using var doc = await GetJsonAsync($"thread?id={id}&page={page}", ct, cookie);
        if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("id", out _))
            throw new IslandException("未找到这个串，可能已被删除，或当前饼干没有访问权限。", rejected: true);
        var post = ParsePost(doc.RootElement);
        // 提示由服务端随页投放；记录相对正常回复的位置，不把它计入 19 条回复的分页。
        var replies = new List<Post>(); var tips = new List<ThreadTip>();
        if (doc.RootElement.TryGetProperty("Replies", out var list) && list.ValueKind == JsonValueKind.Array)
            foreach (var entry in list.EnumerateArray().Select(ParsePost).Where(p => p.Id > 0))
                if (entry.IsSiteTip) tips.Add(new(entry, replies.Count)); else replies.Add(entry);
        return new(post, replies, page, Tips: tips);
    }

    /// <summary>按帖子 ID 读取单条引用；该 ID 可能属于回复，不能假定它一定是主串。</summary>
    public async Task<Post> GetReferenceAsync(long id, CancellationToken ct = default)
    {
        using var doc = await GetJsonAsync($"ref?id={id}", ct);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new IslandException("引用不存在或无法访问。");
        var post = ParsePost(doc.RootElement);
        if (post.Id == 0) throw new IslandException("引用不存在或无法访问。");
        return post;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, CancellationToken ct, string? requestCookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + path);
        var cookie = requestCookie ?? _cookie;
        if (cookie.Length > 0) request.Headers.Add("Cookie", "userhash=" + cookie);
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new IslandException("请求过于频繁，请稍后再试。");
        if (!response.IsSuccessStatusCode) throw new IslandException($"X 岛暂时无法访问（HTTP {(int)response.StatusCode}），请稍后刷新。");
        var raw = await response.Content.ReadAsStringAsync(ct);
        // HTTP 成功不代表业务成功：接口还可能返回验证网页、JSON 错误字符串或 error 对象。
        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { throw new IslandException("X 岛返回了网页而不是数据，可能需要网络验证，请稍后重试。"); }
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out _)))
        {
            var message = root.ValueKind == JsonValueKind.String ? root.GetString() : Str(root, "error");
            doc.Dispose();
            throw new IslandException(ContentText.Plain(message ?? "请求失败"), rejected: true);
        }
        return doc;
    }

    /// <summary>先校验输入并建立网页会话，再提交一次表单；成功、明确失败和结果未知分别处理。</summary>
    public async Task<PostResult> SendPostAsync(PostRequest post, string cookie, CancellationToken ct = default)
    {
        cookie = ContentText.NormalizeCookie(cookie);
        if (string.IsNullOrWhiteSpace(post.Content) || post.Content.Length > 10000) throw new ArgumentException("正文需要为 1–10000 个字符。");
        if (post.Title.Length > 100) throw new ArgumentException("标题不能超过 100 个字符。");
        if (post.Name.Length > 100) throw new ArgumentException("名称不能超过 100 个字符。");
        if (post.ThreadId <= 0 && post.ForumId <= 0) throw new ArgumentException("请先选择发串板块。");
        byte[]? image = null;
        string? mime = null;
        // 在任何网络请求前读取并检查附件，避免预检成功后才发现本地文件不可用。
        if (!string.IsNullOrEmpty(post.ImageFile))
        {
            var info = new FileInfo(post.ImageFile);
            if (!info.Exists || info.Length > 5 * 1024 * 1024) throw new ArgumentException("图片不存在或超过 5 MB。");
            image = await File.ReadAllBytesAsync(info.FullName, ct);
            mime = DetectImage(image) ?? throw new ArgumentException("附图必须是真实的 JPG、PNG 或 GIF 文件。");
        }
        // cookie 参数和不可变 PostRequest 来自发送预览的快照；界面切换身份不会改变此次发言。
        // 预检只发 GET，收集主站会话 Cookie，以及页面存在时的 __hash__ 防伪字段。
        using var preflight = new HttpRequestMessage(HttpMethod.Get, Site + "/Forum");
        preflight.Headers.Add("Cookie", "userhash=" + cookie);
        using var preflightResponse = await _http.SendAsync(preflight, ct);
        if (!preflightResponse.IsSuccessStatusCode) throw new IslandException("暂时无法建立发言会话。尚未发送，请稍后重试。");
        var page = await preflightResponse.Content.ReadAsStringAsync(ct);
        var csrf = Regex.Match(page, "name=[\"']__hash__[\"'][^>]*(?:value|content)=[\"']([^\"']+)", RegexOptions.IgnoreCase);
        // 仅保留格式有效的会话 Cookie，禁止响应中的 userhash 覆盖用户已确认的发送身份。
        var sessionCookies = preflightResponse.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).Where(c => Regex.IsMatch(c, @"^[A-Za-z0-9_]+=[\x21-\x3A\x3C-\x7E]*$") && !c.StartsWith("userhash=", StringComparison.OrdinalIgnoreCase)).ToArray() : [];
        using var form = new MultipartFormDataContent();
        void Add(string key, string value)
        {
            var field = new StringContent(value, Encoding.UTF8);
            field.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"" + key + "\"" };
            form.Add(field);
        }
        Add("content", post.Content); Add("title", post.Title); Add("name", post.Name);
        // 回复表单用 resto 指定主串，新串表单用 fid 指定版块。
        Add(post.ThreadId > 0 ? "resto" : "fid", (post.ThreadId > 0 ? post.ThreadId : post.ForumId).ToString(System.Globalization.CultureInfo.InvariantCulture));
        // 不加水印时必须省略字段；提交字符串 "false" 仍可能被 PHP 当成真值。
        if (post.Watermark) Add("water", "true");
        if (csrf.Success) Add("__hash__", WebUtility.HtmlDecode(csrf.Groups[1].Value));
        if (image is not null)
        {
            var part = new ByteArrayContent(image);
            part.Headers.ContentType = new MediaTypeHeaderValue(mime!);
            form.Add(part, "image", "upload" + (mime == "image/jpeg" ? ".jpg" : mime == "image/gif" ? ".gif" : ".png"));
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, Site + "/Home/Forum/" + (post.ThreadId > 0 ? "doReplyThread.html" : "doPostThread.html")) { Content = form };
        request.Headers.Add("Cookie", string.Join("; ", new[] { "userhash=" + cookie }.Concat(sessionCookies)));
        request.Headers.Referrer = new Uri(Site + "/Forum");
        request.Headers.Add("Origin", Site);
        // POST 不自动重试：断线、超时或取消只表示没有完整响应，服务端可能已经发布成功。
        try
        {
            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            return ParsePostResult(body, response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            throw new DeliveryUnknownException("未收到完整发送结果，草稿已保留。请先刷新串确认是否已发出，再决定是否重发。", ex);
        }
    }

    /// <summary>只在成功状态码且页面明确写出成功时确认送达；其余未知页面不能当作发送失败。</summary>
    public static PostResult ParsePostResult(string body, HttpStatusCode status)
    {
        var error = Regex.Match(body, "<(?:p|div)[^>]*class=[\"'][^\"']*\\berror\\b[^\"']*[\"'][^>]*>([\\s\\S]*?)</(?:p|div)>", RegexOptions.IgnoreCase);
        if (error.Success) throw new IslandException(ContentText.Plain(error.Groups[1].Value));
        var success = Regex.Match(body, "<(?:p|div)[^>]*class=[\"'][^\"']*\\bsuccess\\b[^\"']*[\"'][^>]*>([\\s\\S]*?)</(?:p|div)>", RegexOptions.IgnoreCase);
        if ((int)status is >= 200 and < 300 && success.Success && Regex.IsMatch(ContentText.Plain(success.Groups[1].Value), "成功"))
        {
            var link = Regex.Match(body, @"/(?:t/|Forum/thread/id/)(\d+)", RegexOptions.IgnoreCase);
            return new(ContentText.Plain(success.Groups[1].Value), link.Success ? long.Parse(link.Groups[1].Value) : null);
        }
        throw new DeliveryUnknownException($"无法确认发言结果（HTTP {(int)status}）。草稿已保留，请在网页或串中核对后再重发。");
    }

    /// <summary>按文件头识别可上传的图片格式，避免仅信任文件扩展名。</summary>
    public static string? DetectImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return "image/jpeg";
        if (bytes.Length >= 6 && (bytes[..6].SequenceEqual("GIF87a"u8) || bytes[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        return null;
    }

    /// <summary>集中映射接口字段，兼容不同端点的回复数字段命名。</summary>
    public static Post ParsePost(JsonElement e) => new()
    {
        Id = Long(e, "id"), Fid = Int(e, "fid"), ReplyCount = Math.Max(Int(e, "ReplyCount"), Int(e, "reply_count")),
        ImagePath = Str(e, "img"), Extension = Str(e, "ext"), Time = Str(e, "now"), UserHash = Str(e, "user_hash"),
        Name = Str(e, "name"), Title = Str(e, "title"), Content = Str(e, "content"), IsAdmin = Int(e, "admin") == 1, IsSage = Int(e, "sage") == 1
    };
    // 部分端点把数字作为 JSON 字符串返回，统一转文本后解析，并为缺失字段提供默认值。
    private static string Str(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind is not JsonValueKind.Null ? v.ToString() : "";
    private static int Int(JsonElement e, string key) => int.TryParse(Str(e, key), out var n) ? n : 0;
    private static long Long(JsonElement e, string key) => long.TryParse(Str(e, key), out var n) ? n : 0;
    private static void EnsureArray(JsonElement e) { if (e.ValueKind != JsonValueKind.Array) throw new IslandException("X 岛返回的数据暂时无法识别，请稍后重试。"); }
    public void Dispose() => _http.Dispose();
}

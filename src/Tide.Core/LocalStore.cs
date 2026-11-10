using System.Text.Json;

namespace Tide.Core;

/// <summary>负责一个资料目录内的 JSON 读写：设置与本机记录共用状态文件，正文页按身份单独缓存。</summary>
public sealed partial class LocalStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _root;
    private readonly object _gate = new();
    public string Root => _root;
    public DiskCache Cache { get; }
    public string? LoadWarning { get; private set; }
    public LocalStore(string root) { _root = Path.GetFullPath(root); Directory.CreateDirectory(_root); Cache = new(_root); }

    /// <summary>读取状态并修正缺失或越界的设置；文件损坏时先备份，再使用默认状态。</summary>
    public UserState Load()
    {
        var path = Path.Combine(_root, "state.json");
        if (!File.Exists(path)) return new();
        try
        {
            var state = JsonSerializer.Deserialize<UserState>(File.ReadAllText(path), Options) ?? throw new JsonException("空数据");
            // 兼容旧版本或手动编辑过的 JSON，让界面收到的集合和外观设置保持可用。
            state.FontSize = double.IsFinite(state.FontSize) ? Math.Clamp(state.FontSize, 13, 24) : 16;
            state.ReadingLineHeight = double.IsFinite(state.ReadingLineHeight) ? Math.Clamp(state.ReadingLineHeight, 1.3, 2.5) : 1.85;
            state.ReadingLetterSpacing = double.IsFinite(state.ReadingLetterSpacing) ? Math.Clamp(state.ReadingLetterSpacing, 0, 3) : 0;
            state.InertialScrolling = true;
            state.WindowLayout?.Normalize();
            state.Cookies ??= []; state.Forums ??= []; state.History ??= []; state.Drafts ??= [];
            state.Drafts = state.Drafts.Where(d => d is not null).Select(d => d with { Name = d.Name ?? "" }).ToList();
            state.RecentEmoticons = (state.RecentEmoticons ?? []).Where(s => s is { Length: > 0 and <= 10000 }).Distinct().Take(20).ToList();
            // 旧资料没有这两项；单条损坏（空 Id、空内容、超长）只丢弃该条，不影响其余自定义内容。
            state.CustomEmoticons = (state.CustomEmoticons ?? [])
                .Where(e => e is not null && !string.IsNullOrEmpty(e.Id) && e.Text is { Length: > 0 and <= 10000 })
                .DistinctBy(e => e.Id).Select(e => e with { Name = EmoticonName(e.Name) }).ToList();
            state.HiddenEmoticons = (state.HiddenEmoticons ?? []).Where(s => s is { Length: > 0 and <= 10000 }).Distinct().ToList();
            state.History = state.History.Where(h => h?.Post is { Id: > 0 }).DistinctBy(h => h.Post.Id)
                .OrderByDescending(h => h.ReadAt).Take(500).Select(h => h with
                {
                    Page = Math.Max(1, h.Page),
                    Position = h.Position is { PostId: > 0 } p && double.IsFinite(p.Offset)
                        ? p with { Index = Math.Clamp(p.Index, -1, ThreadPage.PageSize - 1), Offset = Math.Clamp(p.Offset, 0, 1_000_000) } : null
                }).ToList();
            state.EncryptedSubscriptionId ??= "";
            state.SubscriptionAccounts = (state.SubscriptionAccounts ?? []).Where(a => a is not null && !string.IsNullOrEmpty(a.Id)).DistinctBy(a => a.Id).ToList();
            state.ActiveSubscriptionId ??= "";
            // 旧资料没有此字段；手动修改造成的 null / 重复项不应产生重复星标或失效入口。
            state.FavoriteForums = (state.FavoriteForums ?? [])
                .Where(f => f is not null && f.Id > 0)
                .DistinctBy(f => f.Key)
                .Select(f => f with
                {
                    Name = string.IsNullOrWhiteSpace(f.Name) ? $"{(f.IsTimeline ? "时间线" : "版块")} {f.Id}" : f.Name,
                    Group = f.Group ?? "", Notice = f.Notice ?? "", MaxPage = Math.Max(1, f.MaxPage)
                }).ToList();
            state.BlockedThreads = (state.BlockedThreads ?? [])
                .Where(t => t is not null && t.Id > 0).DistinctBy(t => t.Id)
                .Select(t => t with { Title = t.Title ?? "" }).ToList();
            state.BlockedForums = (state.BlockedForums ?? [])
                .Where(f => f is not null && f.Id > 0).DistinctBy(f => f.Id)
                .Select(f => f with { Name = string.IsNullOrWhiteSpace(f.Name) ? $"板块 {f.Id}" : f.Name }).ToList();
            state.ActiveCookieId ??= "";
            state.SuppressedNoticeKeys = (state.SuppressedNoticeKeys ?? [])
                .Where(key => key is { Length: 64 } && key.All(Uri.IsHexDigit))
                .Select(key => key.ToUpperInvariant()).Distinct().TakeLast(100).ToList();
            state.Theme = state.Theme == "dark" ? "dark" : "light";
            // 新增的个性化字段：旧资料没有时为空（默认色、沿用旧图片开关），格式不对的单项回到默认。
            state.ListImages = state.ListImages is ImageModes.Auto or ImageModes.Click or ImageModes.Hidden ? state.ListImages : "";
            state.ReaderImages = state.ReaderImages is ImageModes.Auto or ImageModes.Click ? state.ReaderImages : "";
            state.AccentColor = HexColor(state.AccentColor);
            var identity = state.IdentityColors ?? new();
            state.IdentityColors = new() { Cookie = HexColor(identity.Cookie), Admin = HexColor(identity.Admin), Sage = HexColor(identity.Sage), Po = HexColor(identity.Po) };
            return state;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 后续保存会覆盖 state.json，因此必须先保留损坏原文件，避免丢失可手动恢复的数据。
            var backup = Path.Combine(_root, $"state-unreadable-{DateTime.UtcNow:yyyyMMddHHmmssfff}.json");
            File.Copy(path, backup, false);
            LoadWarning = $"设置文件无法读取，原文件已备份到 {backup}";
            return new();
        }
    }

    /// <summary>规范为大写 #RRGGBB；空值或格式错误返回空字符串（使用默认色）。</summary>
    public static string HexColor(string? value)
    {
        var text = (value ?? "").Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        return text.Length == 7 && text[1..].All(Uri.IsHexDigit) ? text.ToUpperInvariant() : "";
    }

    /// <summary>简称或备注去掉首尾空白，最多 80 字；颜文字正文本身从不 Trim。</summary>
    public static string EmoticonName(string? name) { var value = (name ?? "").Trim(); return value.Length > 80 ? value[..80] : value; }

    public void Save(UserState state)
    {
        // 先完整写入同目录临时文件，再替换目标，降低写到一半就留下残缺 JSON 的风险。
        // 锁只协调本实例的状态保存；跨进程互斥由 App 的资料目录 Mutex 负责。
        lock (_gate)
        {
            var path = Path.Combine(_root, "state.json");
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, path, true);
        }
    }

    /// <summary>按身份、串号和页码保存正文，与图片及订阅快照共用磁盘上限。</summary>
    public void SavePage(ThreadPage page, string identityKey)
    {
        Cache.Write(CacheKey(identityKey, page.Original.Id, page.Page), JsonSerializer.SerializeToUtf8Bytes(page));
    }

    /// <summary>缓存不存在或损坏时返回 null，由调用方决定是否继续显示网络错误。</summary>
    public ThreadPage? ReadPage(long id, int page, string identityKey)
    {
        var bytes = Cache.Read(CacheKey(identityKey, id, page));
        if (bytes is null) return null;
        try
        {
            var cached = JsonSerializer.Deserialize<ThreadPage>(bytes);
            // 旧版本可能把服务端截断到第 100 页的内容存成任意高页；保留文件但不再读为有效离线页。
            return cached is not null && cached.Original.Id == id && cached.Page == page &&
                (page <= 100 || cached.PaginationVerified) ? cached : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    private string CacheKey(string identityKey, long id, int page)
    {
        // 目录名来自本地身份 ID 的摘要，用于隔离不同饼干的正文缓存；这不是正文加密。
        // 游客使用独立目录，不能直接读取另一身份缓存的受限串页。
        var key = string.IsNullOrEmpty(identityKey) ? "guest" : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identityKey)))[..20];
        return $"{key}/{id}-{page}.json";
    }
}

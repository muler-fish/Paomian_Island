using System.Text.Json.Serialization;

namespace Tide.Core;

/// <summary>统一表示普通版块和时间线；时间线只能浏览，不能直接作为发串目的地。</summary>
public sealed record Forum(int Id, string Name, string Group, string Notice = "", bool IsTimeline = false, int MaxPage = 1000)
{
    // 网页链接使用接口 name；showName 可能含补充说明，不能用来拼网页板块名。
    public string UrlName { get; init; } = "";
    // 两类入口的数字 ID 可能相同，加前缀后才能正确判断侧栏选中项。
    [JsonIgnore] public string Key => $"{(IsTimeline ? "t" : "f")}{Id}";
}

/// <summary>主串和回复共用的数据模型，保留服务端正文，由显示层决定如何渲染。</summary>
public sealed record Post
{
    public long Id { get; init; }
    public int Fid { get; init; }
    public int ReplyCount { get; init; }
    public string ImagePath { get; init; } = "";
    public string Extension { get; init; } = "";
    public string Time { get; init; } = "";
    // 这是帖子公开显示的饼干 ID，不是请求 Cookie 中用于认证的完整 userhash。
    public string UserHash { get; init; } = "";
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    public string Content { get; init; } = "";
    public bool IsAdmin { get; init; }
    public bool IsSage { get; init; }
    // 派生的界面属性不写入 JSON；摘要隐藏防剧透内容，避免在串列表里提前泄露。
    [JsonIgnore] public string PlainText => ContentText.Plain(Content, hideSpoilers: true);
    [JsonIgnore] public string DisplayTitle => string.IsNullOrWhiteSpace(Title) || Title == "无标题"
        ? (PlainText.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "图片串") : ContentText.Plain(Title);
    [JsonIgnore] public string Number => $"No.{Id}";
    [JsonIgnore] public string Meta => $"{UserHash}  ·  {Time}";
    [JsonIgnore] public bool HasImage => ImageUrl() is not null;
    // 三个服务端标记同时匹配才是官方提示；可编辑的标题/名称不能用来判定。
    [JsonIgnore] public bool IsSiteTip => Id == 9999999 && UserHash == "Tips" && Time == "2099-01-01 00:00:01";

    public string? ImageUrl(bool thumbnail = false)
    {
        // 只允许图片站内的相对路径与已知扩展名，不直接使用服务端提供的任意 URL。
        if (string.IsNullOrWhiteSpace(ImagePath) || ImagePath.Contains("..") ||
            !System.Text.RegularExpressions.Regex.IsMatch(ImagePath, @"^[a-zA-Z0-9/_-]+$")) return null;
        if (!new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" }.Contains(Extension.ToLowerInvariant())) return null;
        return $"https://image.nmb.best/{(thumbnail ? "thumb" : "image")}/{ImagePath}{Extension}";
    }
}

/// <summary>服务端提示及其插入位置；BeforeReplyIndex 是正常回复中的索引，可以等于回复数量。</summary>
public sealed record ThreadTip(Post Post, int BeforeReplyIndex);

/// <summary>一页读串结果：主楼、正常回复与官方提示分开保存，提示不影响回复分页。</summary>
public sealed record ThreadPage(Post Original, List<Post> Replies, int Page, bool PaginationVerified = false, List<ThreadTip>? Tips = null)
{
    // 接口每页最多 19 条有效回复；主楼和官方 tips 不计入回复分页。
    public const int PageSize = 19;
    [JsonIgnore] public int TotalPages => Math.Max(1, (Original.ReplyCount + PageSize - 1) / PageSize);
}

/// <summary>本机历史串摘要，Page 记录上次读到的页码。</summary>
public sealed record SavedThread(Post Post, int Page, DateTimeOffset ReadAt, ReadingPosition? Position = null);
/// <summary>可见回复及其顶部被滚出的像素；Index 用于原回复删除后的同页近邻回退。</summary>
public sealed record ReadingPosition(long PostId, int Index, double Offset, bool OnlyPo);
/// <summary>未发送的编辑内容；举报也是新串，ReportPostId 保存被举报楼号，Content 单独保存理由。</summary>
public sealed record Draft(string Key, int ForumId, long ThreadId, string Title, string Content, string ImageFile, bool Watermark, string Name = "", long ReportPostId = 0);
/// <summary>本机饼干身份；Id 是本地生成的标识，EncryptedValue 由 Windows 层加密后保存。</summary>
public sealed record CookieIdentity(string Id, string Name, string EncryptedValue)
{
    // 备注只保存在本机，不覆盖二维码/服务端提供的原名称，也不参与认证。
    public string Remark { get; init; } = "";
}
/// <summary>订阅号的本地身份；备注不参与服务端请求，号码由 Windows 层加密。</summary>
public sealed record SubscriptionIdentity(string Id, string Name, string EncryptedValue);
/// <summary>可丢弃的订阅快照；Complete 只在读到末页后成立，未知写入需重新核对。</summary>
public sealed record SubscriptionSnapshot(List<Post> Posts, bool Complete, DateTimeOffset UpdatedAt, List<long> Uncertain);
/// <summary>发送预览时确定的参数快照；ThreadId 大于 0 表示回复，否则使用 ForumId 发串。</summary>
public sealed record PostRequest(int ForumId, long ThreadId, string Title, string Content, string ImageFile = "", bool Watermark = false, string Name = "");
/// <summary>服务端明确确认成功后的结果；响应没有串链接时 ThreadId 可能为空。</summary>
public sealed record PostResult(string Message, long? ThreadId);

/// <summary>本机屏蔽条目，只保留管理列表需要的串号与标题摘要。</summary>
public sealed record BlockedThread(long Id, string Title);
public sealed record BlockedForum(int Id, string Name);

/// <summary>整个资料目录的可持久化状态；正文页缓存另存，不混入 state.json。</summary>
public sealed class UserState
{
    public int SchemaVersion { get; set; } = 1;
    public string Theme { get; set; } = "light";
    public double FontSize { get; set; } = 16;
    public double ReadingLineHeight { get; set; } = 1.85;
    public double ReadingLetterSpacing { get; set; }
    public bool IslandUseProxy { get; set; } = true;
    public bool UpdateUseProxy { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public bool InstallUpdatesOnExit { get; set; }
    public string CacheParentPath { get; set; } = "";
    public int CacheLimitMb { get; set; } = 256;
    public bool ShowImages { get; set; } = true;
    public bool InertialScrolling { get; set; } = true;
    // 新资料和缺少此字段的旧资料默认关闭，显式保存的开关状态继续沿用。
    public bool HideSpoilers { get; set; }
    // 缺少布局的旧资料沿用首次启动尺寸；仅记录主窗口，不记住临时弹窗。
    public WindowLayoutState? WindowLayout { get; set; }
    public string ActiveCookieId { get; set; } = "";
    public List<CookieIdentity> Cookies { get; set; } = [];
    public List<Forum> Forums { get; set; } = [];
    // 保存入口快照：目录暂时不可用时，仍能显示收藏名称并保留快捷入口。
    public List<Forum> FavoriteForums { get; set; } = [];
    // 本机列表屏蔽，不代表取消订阅或删除阅读历史。
    public List<BlockedThread> BlockedThreads { get; set; } = [];
    public List<BlockedForum> BlockedForums { get; set; } = [];
    // 只保存已明确选择“不再显示”的通知摘要，不缓存公告正文，避免离线弹出过期维护消息。
    public List<string> SuppressedNoticeKeys { get; set; } = [];
    // 订阅号可用于修改云端列表，由 Windows 层加密；不保留本地串收藏或旧版迁移路径。
    public string EncryptedSubscriptionId { get; set; } = "";
    public List<SubscriptionIdentity> SubscriptionAccounts { get; set; } = [];
    public string ActiveSubscriptionId { get; set; } = "";
    public List<SavedThread> History { get; set; } = [];
    public List<Draft> Drafts { get; set; } = [];
    // 成功插入才进入最近使用，最多 20 项；保存文本便于去重，不记录单纯的悬停或预览。
    public List<string> RecentEmoticons { get; set; } = [];
}

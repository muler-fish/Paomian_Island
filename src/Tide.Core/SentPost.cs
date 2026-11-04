namespace Tide.Core;

/// <summary>服务端明确确认成功的本机发言；只存公开名称和本地身份标识，不保存认证内容。</summary>
public sealed record SentPost(string Id, bool IsReply, long? ThreadId, int ForumId, string ForumName,
    string CookieName, string Title, string Name, string Content, bool HasImage, DateTimeOffset SentAt)
{
    // 旧记录没有该字段，仍可在原名称唯一时关联；新记录不再因改备注/原名发生串号统计混淆。
    public string CookieId { get; init; } = "";
}

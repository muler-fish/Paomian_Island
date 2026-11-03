namespace Tide.Core;

/// <summary>服务端明确确认成功的本机发言；只存公开饼干名称，不保存认证内容和附件本地路径。</summary>
public sealed record SentPost(string Id, bool IsReply, long? ThreadId, int ForumId, string ForumName,
    string CookieName, string Title, string Name, string Content, bool HasImage, DateTimeOffset SentAt);

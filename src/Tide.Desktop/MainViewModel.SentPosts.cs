using System.IO;
using Tide.Core;

namespace Tide.Desktop;

public sealed partial class MainViewModel
{
    public event Action? SentPostsChanged;
    private readonly HashSet<string> _sentPostLookups = [];
    internal bool IsLookingUpSentPost(string id) => _sentPostLookups.Contains(id);

    // 仅从发送成功分支调用。回复的目标串号来自提交快照，不能误用当前正在浏览的串。
    internal bool RecordSentPost(PostRequest request, PostResult result, CookieIdentity cookie)
    {
        var entry = new SentPost(Guid.NewGuid().ToString("N"), request.ThreadId > 0,
            request.ThreadId > 0 ? request.ThreadId : result.ThreadId is > 0 ? result.ThreadId : null,
            request.ForumId, ForumName(request.ForumId), cookie.Name, request.Title, request.Name,
            request.Content, request.ImageFile.Length > 0, DateTimeOffset.Now) { CookieId = cookie.Id };
        try { Store.AddSentPost(entry); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "发言已发送成功，但本机记录保存失败；草稿已保留，请勿重复发送。";
            return false;
        }
        SentPostsChanged?.Invoke();
        if (!entry.IsReply && entry.ThreadId is not > 0) _ = ResolveSentThreadAsync(entry, cookie);
        return true;
    }

    // 发送已成功并已落盘，此处的网络/存储失败不能再传播到发送异常分支。
    internal async Task<string> ResolveSentThreadAsync(SentPost entry, CookieIdentity? identity = null)
    {
        if (entry.IsReply || entry.ThreadId is > 0 || !_sentPostLookups.Add(entry.Id)) return "";
        SentPostsChanged?.Invoke();
        try
        {
            var identities = State.Cookies.Where(c => string.IsNullOrEmpty(entry.CookieId)
                ? c.Name == entry.CookieName : c.Id == entry.CookieId).ToArray();
            identity ??= identities.Length == 1 ? identities[0] : null;
            if (identity is null) return "请先导入发送这条主题时使用的饼干，再查找串号。";
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var id = await Api.FindSentThreadAsync(entry, SecretProtection.Unprotect(identity.EncryptedValue), timeout.Token);
            if (id is not > 0) return "暂未找到唯一匹配的主题，可稍后重试。发言已经成功，请勿重复发送。";
            Store.SetSentThreadId(entry.Id, id.Value);
            return "";
        }
        catch (OperationCanceledException) { return "查找串号超时，可稍后重试。发言已经成功。"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return "串号保存失败，本机原记录已保留。"; }
        catch (Exception) { return "暂时无法查找串号，可稍后重试。发言已经成功。"; }
        finally { _sentPostLookups.Remove(entry.Id); SentPostsChanged?.Invoke(); }
    }
}

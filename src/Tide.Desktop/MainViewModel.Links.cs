using Tide.Core;

namespace Tide.Desktop;

public sealed partial class MainViewModel
{
    internal bool IsKnownThreadRoot(long id) => id > 0 &&
        (Original?.Id == id || Posts.Any(card => card.Post.Id == id) || State.History.Any(entry => entry.Post.Id == id) ||
         _recentThreads.Values.Any(entry => entry.Value.Original.Id == id) ||
         _recentLists.Values.Any(entry => entry.Value.Any(post => post.Id == id)));

    /// <summary>预览只读取串首，不改正文、页码、PO 筛选或历史；关闭小窗即可取消。</summary>
    internal async Task<Post> ReadLinkPreviewAsync(long id, bool refresh, CancellationToken ct)
    {
        var identity = State.ActiveCookieId;
        var page = await ReadThreadAsync(id, 1, refresh, ct);
        ct.ThrowIfCancellationRequested();
        if (_disposed || identity != State.ActiveCookieId) throw new IslandException("浏览身份已改变，请重新打开预览。");
        return page.Original;
    }

    /// <summary>仅解析板块名称；加载状态属于确认小窗，不占用或取消主窗口的列表请求。</summary>
    internal async Task<Forum?> ResolveLinkForumAsync(IslandLink link, CancellationToken ct)
    {
        if (_disposed) throw new OperationCanceledException(ct);
        var forum = link.FindForum(State.Forums);
        if (forum is null)
        {
            var forums = await Api.GetForumsAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (_disposed) throw new OperationCanceledException(ct);
            State.Forums = forums; BuildForumGroups(); Save(); forum = link.FindForum(forums);
        }
        return forum is not null && link.Page <= forum.MaxPage ? forum : null;
    }
}

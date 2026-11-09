using Tide.Core;

namespace Tide.Desktop;

public sealed partial class MainViewModel
{
    public void ClearTemporaryCache()
    {
        _recentLists.Clear(); _recentThreads.Clear(); ImageService.ClearMemoryCache();
    }
    // 普通浏览只保留本次运行中最近 40 个结果，30 秒后重新请求；显式刷新总是绕过。
    private readonly Dictionary<(string Identity, string Forum, int Page), (DateTimeOffset At, List<Post> Value)> _recentLists = [];
    private readonly Dictionary<(string Identity, long Id, int Page), (DateTimeOffset At, ThreadPage Value)> _recentThreads = [];

    private async Task<List<Post>> ReadListAsync(Forum forum, int page, bool refresh, CancellationToken ct)
    {
        var key = (State.ActiveCookieId, forum.Key, page);
        if (!refresh && _recentLists.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(30)) return cached.Value;
        var result = await Api.GetThreadsAsync(forum, page, ct);
        ct.ThrowIfCancellationRequested(); _recentLists[key] = (DateTimeOffset.UtcNow, result);
        foreach (var old in _recentLists.OrderByDescending(p => p.Value.At).Skip(40).Select(p => p.Key).ToList()) _recentLists.Remove(old);
        return result;
    }

    /// <param name="maxAge">普通打开 30 秒；后退/前进返回刚读过的串时放宽，避免每次都重新下载同一页。</param>
    private async Task<ThreadPage> ReadThreadAsync(long id, int page, bool refresh, CancellationToken ct, TimeSpan? maxAge = null)
    {
        var key = (State.ActiveCookieId, id, page);
        if (!refresh && _recentThreads.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < (maxAge ?? RecentThreadAge)) return cached.Value;
        var result = await Api.GetThreadAsync(id, page, ct);
        ct.ThrowIfCancellationRequested(); _recentThreads[key] = (DateTimeOffset.UtcNow, result);
        foreach (var old in _recentThreads.OrderByDescending(p => p.Value.At).Skip(40).Select(p => p.Key).ToList()) _recentThreads.Remove(old);
        return result;
    }
}

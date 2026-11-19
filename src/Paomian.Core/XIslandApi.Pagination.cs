namespace Paomian.Core;

public sealed partial class XIslandApi
{
    // 2026-10-24 实测匿名请求 200/400/474 都返回第 100 页。不能只把请求页码贴在返回内容上。
    // 这是错误响应检测，不是客户端页数上限；内容不同的高页照常放行。基准仅在本次身份下短暂复用。
    private readonly object _paginationGate = new();
    private readonly Dictionary<long, (DateTimeOffset At, ThreadPage Page)> _paginationBaselines = [];
    private ThreadPage? ReadPaginationBaseline(long id, string cookie)
    {
        lock (_paginationGate)
            return cookie == _cookie && _paginationBaselines.TryGetValue(id, out var entry) &&
                DateTimeOffset.UtcNow - entry.At < TimeSpan.FromSeconds(30) ? entry.Page : null;
    }
    private void RememberPaginationBaseline(long id, string cookie, ThreadPage page)
    {
        lock (_paginationGate)
        {
            if (cookie != _cookie) return;
            _paginationBaselines[id] = (DateTimeOffset.UtcNow, page);
            foreach (var old in _paginationBaselines.OrderByDescending(p => p.Value.At).Skip(40).Select(p => p.Key).ToList())
                _paginationBaselines.Remove(old);
        }
    }
}

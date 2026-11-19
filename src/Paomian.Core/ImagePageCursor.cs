namespace Paomian.Core;

public sealed record ImagePageBatch(IReadOnlyList<Post> Images, bool HasMore, int Page);

/// <summary>看图使用独立页游标；成功后才前进，失败重试同页，不修改正文阅读位置。</summary>
public sealed class ImagePageCursor
{
    private readonly long _threadId;
    private readonly string? _po;
    private readonly Func<int, CancellationToken, Task<ThreadPage>> _read;
    private readonly HashSet<long> _seen;
    private int _page, _totalPages;
    private bool _ended;

    public ImagePageCursor(long threadId, int page, int totalPages, IEnumerable<long> seen, string? po,
        Func<int, CancellationToken, Task<ThreadPage>> read)
    { _threadId = threadId; _page = page; _totalPages = totalPages; _seen = seen.ToHashSet(); _po = po; _read = read; }
    public bool HasMore => !_ended && _page < _totalPages;

    public async Task<ImagePageBatch> NextAsync(CancellationToken ct)
    {
        if (!HasMore) return new([], false, _page);
        var target = _page + 1;
        var result = await _read(target, ct);
        ct.ThrowIfCancellationRequested();
        if (result.Original.Id != _threadId || result.Page != target)
            throw new IslandException("未返回预期的下一页，请重试。");
        var fresh = result.Replies.Where(p => !_seen.Contains(p.Id)).DistinctBy(p => p.Id).ToArray();
        // 服务端可能重复返回旧页；不能把重复页当新页，更不能直接宣布已到末尾。
        if (result.Replies.Count > 0 && fresh.Length == 0)
            throw new IslandException("下一页返回了重复内容，请稍后重试。");
        foreach (var p in fresh) _seen.Add(p.Id);
        _page = target; _totalPages = result.TotalPages; _ended = result.Replies.Count == 0;
        return new(fresh.Where(p => p.HasImage && !p.IsSiteTip && (_po is null || p.UserHash == _po)).ToArray(), HasMore, _page);
    }
}

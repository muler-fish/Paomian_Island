namespace Tide.Core;

/// <summary>阅读历史中的一项。串号在全站唯一，足以确定目标；页码和阅读位置用于返回时恢复。</summary>
public sealed record ReadingHistoryEntry(long ThreadId, int Page = 1, ReadingPosition? Position = null, int ForumId = 0);

/// <summary>类似浏览器的双向阅读历史：Index 指向当前项，后退/前进只移动位置，主动打开其他串时删除前进部分。</summary>
/// <remarks>只属于本次运行的阅读会话，不写盘，与「最近浏览」（<see cref="UserState.History"/>）互不影响。</remarks>
public sealed class ReadingNavigationHistory(int capacity = ReadingNavigationHistory.DefaultCapacity)
{
    public const int DefaultCapacity = 100;
    private readonly List<ReadingHistoryEntry> _entries = [];
    public int Capacity { get; } = Math.Max(2, capacity);
    public int Index { get; private set; } = -1;
    public int Count => _entries.Count;
    public IReadOnlyList<ReadingHistoryEntry> Entries => _entries;
    public ReadingHistoryEntry? Current => Index >= 0 ? _entries[Index] : null;
    public bool CanGoBack => Index > 0;
    public bool CanGoForward => Index >= 0 && Index < _entries.Count - 1;
    public ReadingHistoryEntry? At(int index) => index >= 0 && index < _entries.Count ? _entries[index] : null;

    /// <summary>主动打开串。与当前项是同一串（刷新、跳页、重复打开）时只更新当前项，不产生连续重复记录。</summary>
    public void Visit(ReadingHistoryEntry entry)
    {
        if (Current?.ThreadId == entry.ThreadId) { _entries[Index] = entry; return; }
        _entries.RemoveRange(Index + 1, _entries.Count - Index - 1);
        _entries.Add(entry);
        // 超出上限时丢弃最早的记录，当前项始终是最后一项。
        if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        Index = _entries.Count - 1;
    }

    /// <summary>后退/前进加载成功后提交位置，不增删记录。</summary>
    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _entries.Count) return false;
        Index = index; return true;
    }

    /// <summary>保存当前项的页码和阅读位置；只更新同一串，避免较晚的异步结果写到其他项上。</summary>
    public void UpdateCurrent(long threadId, int page, ReadingPosition? position)
    {
        if (Current is { } current && current.ThreadId == threadId) _entries[Index] = current with { Page = page, Position = position };
    }

    /// <summary>移除已失效的记录；前后两项因此变成同一串时合并，并保留当前项。</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count) return;
        _entries.RemoveAt(index);
        if (Index > index || Index == _entries.Count) Index--;
        if (index > 0 && index < _entries.Count && _entries[index - 1].ThreadId == _entries[index].ThreadId)
        {
            var drop = Index == index ? index - 1 : index;
            _entries.RemoveAt(drop);
            if (Index >= drop) Index--;
        }
    }

    public void Clear() { _entries.Clear(); Index = -1; }
}

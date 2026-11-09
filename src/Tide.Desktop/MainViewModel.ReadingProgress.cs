using Tide.Core;

namespace Tide.Desktop;

public sealed partial class MainViewModel
{
    private readonly Dictionary<long, (int Page, int Index)> _replyLocations = [];
    public event Action? ReaderLeaving;
    internal ReadingPosition? ReaderRestorePosition { get; private set; }

    private ReadPost ReadingItem(Post post, bool isPo)
    {
        var location = _replyLocations.GetValueOrDefault(post.Id, (_threadStartPage, 0));
        return new(post, isPo, Page: location.Item1, Index: location.Item2);
    }

    private void RegisterReadingHistory(SavedThread saved)
    {
        State.History.RemoveAll(h => h.Post.Id == saved.Post.Id); State.History.Insert(0, saved);
        if (State.History.Count > 500) State.History.RemoveRange(500, State.History.Count - 500);
    }

    /// <summary>由可见项记录阅读进度；无变化时不写盘，也不使用已预载到的最远页。</summary>
    internal void SaveReadingPosition(ReadPost item, double offset)
    {
        if (_disposed || _original is null || item.IsTip || !ReadingPosts.Contains(item) || !double.IsFinite(offset)) return;
        var position = new ReadingPosition(item.Post.Id, item.Index, Math.Max(0, offset), OnlyPo);
        // 同一位置也写进当前历史项：从引用串后退时回到离开前的页码和位置，而不是最近一次打开时的位置。
        _navigation.UpdateCurrent(_original.Id, item.Page, position);
        var old = State.History.Find(h => h.Post.Id == _original.Id);
        if (old?.Page == item.Page && old.Position is { } p && p.PostId == position.PostId && p.Index == position.Index &&
            p.OnlyPo == position.OnlyPo && Math.Abs(p.Offset - position.Offset) < .5) return;
        RegisterReadingHistory(new(_original, item.Page, DateTimeOffset.Now, position)); Save();
    }
}

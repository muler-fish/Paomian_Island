using Tide.Core;

namespace Tide.Desktop;

/// <summary>右侧阅读区的「当前阅读串」与本次运行的后退/前进历史。</summary>
/// <remarks>
/// 所有切换阅读对象的入口（串列表、引用、串链接、跳转框、我的发言等）都经过 OpenThreadAsync，
/// 在正文真正替换时统一提交历史；串列表的选中项由窗口按 ReadingThreadId 同步，不再反过来充当阅读状态。
/// </remarks>
public sealed partial class MainViewModel
{
    // 后退时复用本次运行中已读过的页面；普通打开仍沿用 30 秒的短缓存。
    private static readonly TimeSpan RecentThreadAge = TimeSpan.FromSeconds(30), TraversalThreadAge = TimeSpan.FromMinutes(10);
    private readonly ReadingNavigationHistory _navigation = new();
    private long? _readingThreadId;
    // 正在加载的后退/前进目标；连续点击以它为起点，而不是以已提交的位置为起点。
    private int? _traversalTarget;
    private string? _pageTextBeforeNavigation;

    /// <summary>右侧正在显示或正在切换过去的串；为空表示阅读区没有串。串列表据此显示选中项。</summary>
    public long? ReadingThreadId { get => _readingThreadId; private set => Set(ref _readingThreadId, value); }
    internal ReadingNavigationHistory Navigation => _navigation;
    private int NavigationIndex => _traversalTarget ?? _navigation.Index;
    public bool CanGoBack => !_disposed && NavigationIndex > 0;
    public bool CanGoForward => !_disposed && NavigationIndex >= 0 && NavigationIndex < _navigation.Count - 1;
    public string BackLabel => CanGoBack ? $"后退到 No.{_navigation.At(NavigationIndex - 1)!.ThreadId} · Alt+←" : "后退 · Alt+←";
    public string ForwardLabel => CanGoForward ? $"前进到 No.{_navigation.At(NavigationIndex + 1)!.ThreadId} · Alt+→" : "前进 · Alt+→";
    public TaskCommand BackCommand { get; private set; } = null!;
    public TaskCommand ForwardCommand { get; private set; } = null!;

    private void InitializeNavigation()
    {
        // 可重入：连续点击由版本号和取消源保证只有最后一次生效。
        BackCommand = new(_ => GoBackAsync(), ex => Report(ex, FeedbackArea.Reader), () => CanGoBack, allowConcurrent: true);
        ForwardCommand = new(_ => GoForwardAsync(), ex => Report(ex, FeedbackArea.Reader), () => CanGoForward, allowConcurrent: true);
    }

    public Task GoBackAsync() => TraverseAsync(-1);
    public Task GoForwardAsync() => TraverseAsync(1);

    private Task TraverseAsync(int step)
    {
        var target = NavigationIndex + step;
        if (_disposed || NavigationIndex < 0 || _navigation.At(target) is not { } entry) return Task.CompletedTask;
        // 后退尚未完成又按前进回到正在显示的串：只取消那次后退，不重新加载。
        if (target == _navigation.Index && entry.ThreadId == _original?.Id) { CancelPendingNavigation(); return Task.CompletedTask; }
        return LoadThreadAsync(entry.ThreadId, null, false, false, false, null, (target, entry));
    }

    /// <summary>串列表点击。右侧已显示的串保持当前页面和位置，不重复请求；正在切换到别的串时取消那次切换。</summary>
    public Task OpenListThreadAsync(long id)
    {
        if (_disposed || id == ReadingThreadId) return Task.CompletedTask;
        if (id == _original?.Id) { CancelPendingNavigation(); return Task.CompletedTask; }
        return OpenThreadAsync(id);
    }

    /// <summary>开始切换阅读对象：先更新当前阅读串，让列表选中项立即对应用户最新的选择。</summary>
    private void BeginNavigation(long id, int? traversal)
    {
        if (ReadingThreadId == _original?.Id && _traversalTarget is null) _pageTextBeforeNavigation = PageText;
        _traversalTarget = traversal;
        ReadingThreadId = id;
        NotifyNavigation();
    }

    /// <summary>正文已替换：后退/前进只移动位置，其他打开方式记为新的历史项。</summary>
    private void CommitNavigation(long id, int page, (int Index, ReadingHistoryEntry Entry)? traversal)
    {
        if (traversal is { } t && _navigation.At(t.Index)?.ThreadId == id) _navigation.MoveTo(t.Index);
        else _navigation.Visit(new(id, page, ReaderRestorePosition, _original?.Fid ?? 0));
        _traversalTarget = null; _pageTextBeforeNavigation = null;
        ReadingThreadId = id;
        NotifyNavigation();
    }

    /// <summary>切换失败：阅读区仍是原来的串，恢复对应的列表选中项和页码输入；失效的历史项被移除。</summary>
    private void FailNavigation(long id, Exception error, (int Index, ReadingHistoryEntry Entry)? traversal)
    {
        RestoreReadingState();
        if (traversal is not { } t) { Report(error, FeedbackArea.Reader); return; }
        Status = "操作未完成";
        if (error is IslandException { Rejected: true } && _navigation.At(t.Index)?.ThreadId == id)
        {
            _navigation.RemoveAt(t.Index); NotifyNavigation();
            SetError($"无法打开 No.{id}：{error.Message} 已从后退/前进记录中移除。", FeedbackArea.Reader);
        }
        else SetError($"无法返回 No.{id}：{Describe(error)}", FeedbackArea.Reader);
    }

    /// <summary>用户在列表里点回正在显示的串：放弃尚未完成的切换。</summary>
    private void CancelPendingNavigation()
    {
        _threadCts?.Cancel(); ++_threadVersion;
        _threadAppending = false; _threadPrepending = false;
        ThreadBusy = false; Status = "就绪";
        RestoreReadingState();
    }

    private void RestoreReadingState()
    {
        _traversalTarget = null;
        if (_pageTextBeforeNavigation is { } text) PageText = text;
        _pageTextBeforeNavigation = null;
        ReadingThreadId = _original?.Id;
        NotifyNavigation();
    }

    /// <summary>身份改变会清空阅读区；历史项只是串号和位置，保留下来供之后后退。</summary>
    private void ResetReadingState()
    {
        _traversalTarget = null; _pageTextBeforeNavigation = null;
        ReadingThreadId = null;
        NotifyNavigation();
    }

    private void NotifyNavigation()
    {
        foreach (var name in new[] { nameof(CanGoBack), nameof(CanGoForward), nameof(BackLabel), nameof(ForwardLabel) }) Changed(name);
        BackCommand?.Refresh(); ForwardCommand?.Refresh();
    }
}

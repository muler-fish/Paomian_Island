using Paomian.Core;

namespace Paomian.Desktop;

public sealed partial class MainViewModel
{
    private bool _listHasMore, _threadHasMore, _listAppending, _threadAppending;
    private bool _listAppendFailed, _threadAppendFailed;
    private bool _threadHasPrevious, _threadPrepending, _threadPrependFailed;
    private int _threadStartPage = 1, _subscriptionVisibleCount = SubscriptionPageSize;
    private List<SavedThread> _historyItems = [];
    private const int HistoryBatchSize = 20;

    public TaskCommand LoadMoreListCommand { get; private set; } = null!;
    public TaskCommand LoadMoreThreadCommand { get; private set; } = null!;
    public TaskCommand RetryListLoadCommand { get; private set; } = null!;
    public TaskCommand RetryThreadLoadCommand { get; private set; } = null!;
    public TaskCommand LoadPreviousThreadCommand { get; private set; } = null!;
    public TaskCommand RetryPreviousThreadCommand { get; private set; } = null!;
    public bool ThreadOpening => ThreadBusy && !_threadAppending && !_threadPrepending;
    public bool ThreadPrepending => ThreadBusy && _threadPrepending;
    public bool ThreadPrependFailed => _threadPrependFailed;
    public bool HasPreviousThread => HasThread && _threadHasPrevious;
    public bool ReaderStartsAfterFirstPage => _threadStartPage > 1;
    public bool ListAppending => ListBusy && _listAppending;
    public bool ThreadAppending => ThreadBusy && _threadAppending;
    public bool ListAppendFailed => IsSubscriptions
        ? !_subscriptionBusy && !_subscriptionKnown && _subscriptionError is not null && Posts.Count >= _subscriptions.Count
        : _listAppendFailed;
    public bool ThreadAppendFailed => _threadAppendFailed;
    public bool HasMoreList => IsSubscriptions ? Posts.Count < _subscriptions.Count || !_subscriptionKnown
        : IsLibrary ? Posts.Count < _historyItems.Count : _listHasMore;
    public bool HasMoreThread => HasThread && _threadHasMore;
    public string ListTailStatus => ListAppending ? "正在加载…" : ListAppendFailed ? "加载失败" : Posts.Count > 0 && !HasMoreList ? "已到底" : "";
    public string ThreadTailStatus => ThreadAppending ? "正在加载…" : ThreadAppendFailed ? "加载失败" : HasThread && !HasMoreThread && !ThreadBusy ? "已到底" : "";

    private bool CanLoadMoreList => !_disposed && !ListBusy && !ListAppendFailed && HasMoreList &&
        (!IsSubscriptions || Posts.Count < _subscriptions.Count);

    private void InitializeIncrementalLoading()
    {
        LoadMoreListCommand = new(_ => LoadMoreListAsync(), Report, () => CanLoadMoreList);
        LoadMoreThreadCommand = new(_ => LoadMoreThreadAsync(), Report,
            () => !_disposed && !ThreadBusy && !_threadAppendFailed && HasMoreThread);
        RetryListLoadCommand = new(_ => RetryListLoadAsync(), Report, () => !_disposed && !ListBusy && ListAppendFailed);
        RetryThreadLoadCommand = new(_ => LoadMoreThreadAsync(true), Report,
            () => !_disposed && !ThreadBusy && ThreadAppendFailed && HasMoreThread);
        LoadPreviousThreadCommand = new(_ => LoadPreviousThreadAsync(), Report,
            () => !_disposed && !ThreadBusy && !_threadPrependFailed && HasPreviousThread);
        RetryPreviousThreadCommand = new(_ => LoadPreviousThreadAsync(true), Report,
            () => !_disposed && !ThreadBusy && _threadPrependFailed && HasPreviousThread);
    }

    /// <summary>只追加一批。滚动事件可能连续触发，忙碌状态与命令共同保证请求串行。</summary>
    public Task LoadMoreListAsync()
    {
        if (!CanLoadMoreList) return Task.CompletedTask;
        if (IsSubscriptions)
        {
            _subscriptionVisibleCount = Math.Min(_subscriptions.Count, Posts.Count + SubscriptionPageSize);
            RenderSubscriptions(); NotifyIncrementalLoading();
            return Task.CompletedTask;
        }
        if (IsLibrary) { AppendHistory(); return Task.CompletedTask; }
        return LoadListAsync(_listPage + 1, append: true);
    }

    public Task RetryListLoadAsync()
    {
        if (_disposed || ListBusy || !ListAppendFailed) return Task.CompletedTask;
        if (IsSubscriptions) return LoadSubscriptionsAsync(true);
        return LoadListAsync(_listPage + 1, refresh: true, append: true);
    }

    public Task LoadMoreThreadAsync(bool retry = false)
    {
        if (_disposed || ThreadBusy || !HasMoreThread || _threadAppendFailed && !retry) return Task.CompletedTask;
        return OpenThreadAsync(_original!.Id, _threadPage + 1, append: true, refresh: retry);
    }

    public Task LoadPreviousThreadAsync(bool retry = false)
    {
        if (_disposed || ThreadBusy || !HasPreviousThread || _threadPrependFailed && !retry) return Task.CompletedTask;
        return OpenThreadAsync(_original!.Id, _threadStartPage - 1, prepend: true, refresh: retry);
    }

    private void AppendHistory()
    {
        foreach (var item in _historyItems.Skip(Posts.Count).Take(HistoryBatchSize))
            Posts.Add(Card(item.Post, $"读至第 {item.Page} 页"));
        Changed(nameof(ListPageLabel)); NotifyIncrementalLoading(); RefreshCommands();
    }

    private List<ReadPost> PageReading(ThreadPage page, IReadOnlySet<long> includedReplies)
    {
        var visible = new List<ReadPost>();
        var tips = (page.Tips ?? []).Where(t => t.Post.IsSiteTip)
            .ToLookup(t => Math.Clamp(t.BeforeReplyIndex, 0, page.Replies.Count));
        for (var index = 0; index <= page.Replies.Count; index++)
        {
            // 每页可有相同提示 ID；不能使用正常回复的串号去重，否则后续页提示会消失。
            foreach (var tip in tips[index]) visible.Add(new(tip.Post, false, Page: page.Page));
            if (index == page.Replies.Count) break;
            var post = page.Replies[index];
            if (includedReplies.Contains(post.Id))
                visible.Add(ReadingItem(post, !string.IsNullOrWhiteSpace(page.Original.UserHash) && post.UserHash == page.Original.UserHash));
        }
        return visible;
    }

    private void AppendReading(IEnumerable<ReadPost> posts)
    {
        if (_original is null) return;
        // 增量追加保留已显示楼层的对象和容器，滚动位置及已展开的防剧透内容不会因续页重置。
        foreach (var post in posts)
            if (post.IsTip || !OnlyPo || post.IsPo) ReadingPosts.Add(post);
    }

    private void PrependReading(IEnumerable<ReadPost> posts, bool includeOriginal)
    {
        if (_original is null) return;
        var visible = posts.Where(p => p.IsTip || !OnlyPo || p.IsPo).ToList();
        if (includeOriginal) visible.Insert(0, new(_original, true, true));
        if (visible.Count == 0) return;
        // 在实际写入前取视口锚点，而不是发请求时取；等待网络期间用户仍可继续阅读。
        ReaderPrepending?.Invoke();
        for (var i = 0; i < visible.Count; i++) ReadingPosts.Insert(i, visible[i]);
        ReaderPrepended?.Invoke();
    }

    private void NotifyIncrementalLoading()
    {
        foreach (var name in new[] { nameof(ListAppending), nameof(ThreadAppending), nameof(ListAppendFailed), nameof(ThreadAppendFailed),
                     nameof(HasMoreList), nameof(HasMoreThread), nameof(ListTailStatus), nameof(ThreadTailStatus),
                     nameof(ThreadPrepending), nameof(ThreadPrependFailed), nameof(HasPreviousThread) }) Changed(name);
    }
}

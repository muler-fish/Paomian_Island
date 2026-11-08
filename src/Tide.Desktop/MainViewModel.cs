using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using Tide.Core;

namespace Tide.Desktop;

// 把核心数据包装成界面需要的分组、摘要和阅读项，避免把显示细节写进 API 模型。
public sealed record ForumGroup(string Name, List<Forum> Items);
public sealed class PostCard(Post post, string forumName, string progress = "", bool showReplies = true) : Observable
{
    public Post Post { get; } = post;
    public string ForumName { get; } = forumName;
    public string Progress { get; } = progress;
    private bool _hideSpoilers;
    // 列表只展示真实填写的标题/名称，不再拿正文首行补标题。
    public string Title => Optional(Post.Title, "无标题", _hideSpoilers);
    public string Author => Optional(Post.Name, "无名氏", _hideSpoilers);
    public bool HasTitle => Title.Length > 0;
    public bool HasAuthor => Author.Length > 0;
    public string DateLabel => PostTime.Format(Post.Time);
    internal static string Optional(string value, string placeholder, bool hideSpoilers)
    {
        var plain = ContentText.Plain(value, hideSpoilers);
        return plain == placeholder ? "" : plain;
    }
    internal void SetSpoilerProtection(bool enabled)
    {
        if (_hideSpoilers == enabled) return;
        _hideSpoilers = enabled; Changed(nameof(Title)); Changed(nameof(Author)); Changed(nameof(HasTitle)); Changed(nameof(HasAuthor)); Changed(nameof(Excerpt));
    }
    // 纯文本用于自动化描述；实际预览由 PostPreview 保留富文本和原始空行。
    public string Excerpt => ContentText.Plain(Post.Content, _hideSpoilers);
    // 订阅接口（feed）不返回回复数，订阅列表里不显示，避免一律显示「0 回复」。
    public bool ShowReplies { get; } = showReplies;
    public string ReplyLabel => $"{Post.ReplyCount} 回复";
    public string Number => Post.Number;
}
public sealed record ReadPost(Post Post, bool IsPo, bool IsOriginal = false, int Page = 1, int Index = -1)
{
    public bool IsTip => Post.IsSiteTip;
    public string TimeLabel => PostTime.Format(Post.Time, includeSeconds: true);
}

/// <summary>主窗口的状态与操作入口，连接 XAML 绑定、网络接口和本机存储。</summary>
/// <remarks>从 WPF 界面线程调用；ObservableCollection 的修改会直接驱动列表刷新。</remarks>
public sealed partial class MainViewModel : Observable, IDisposable
{
    public XIslandApi Api { get; }
    public LocalStore Store { get; }
    public UserState State { get; }
    internal UpdateController Updates { get; }
    public bool HasUpdate => Updates.Available is not null;
    public string SettingsHint => HasUpdate ? $"发现新版本 {Updates.Available!.Version} · 设置与关于" : "设置与关于";
    public ObservableCollection<ForumGroup> ForumGroups { get; } = [];
    public ObservableCollection<Forum> FavoriteForums { get; } = [];
    private IReadOnlySet<string> _favoriteForumKeys = new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> FavoriteForumKeys => _favoriteForumKeys;
    public bool HasFavoriteForums => FavoriteForums.Count > 0;
    public ObservableCollection<PostCard> Posts { get; } = [];
    public ObservableCollection<ReadPost> ReadingPosts { get; } = [];
    // 保留当前已加载的全部回复，“只看 PO”仅重建显示集合，不必再次请求网络。
    private readonly List<Post> _replies = [];
    private readonly SortedDictionary<int, List<ReadPost>> _readingPages = [];
    // 列表和正文可以独立加载，各用一组取消源和版本号。
    // 取消尽量停止旧请求；版本号负责拒绝那些来不及取消、稍后才返回的响应。
    private CancellationTokenSource? _listCts, _threadCts;
    private int _listVersion, _threadVersion;
    private bool _disposed;
    private string? _appliedCookieId;
    // 空字符串表示版块列表，subscriptions/history 表示云订阅或本机历史。
    private string _library = "";
    private string _jumpText = "", _pageText = "1";
    private string _listTitle = "综合线", _status = "就绪", _error = "";
    private string _readerTitle = "", _readerMeta = "";
    private bool _listBusy, _threadBusy, _onlyPo;
    private int _listPage = 1, _listStartPage = 1, _threadPage = 1, _totalPages = 1;
    private Post? _original;
    public Forum? ActiveForum { get; private set; }
    public string ActiveForumKey => IsLibrary ? "" : ActiveForum?.Key ?? "";
    public Post? Original => _original;
    public bool HasThread => _original is not null;
    public bool HasNoThread => !HasThread && !ThreadBusy;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool EmptyList => VisiblePostCount == 0 && !ListLoading;
    public bool ListLoading => ListBusy && !_listAppending || IsSubscriptions && _subscriptionBusy;
    public string ListLoadingText => IsSubscriptions ? SubscriptionSyncStatus : "正在加载列表";
    public bool CanInteractThread => HasThread && (!ThreadBusy || _threadAppending || _threadPrepending);
    public string ListTitle { get => _listTitle; private set => Set(ref _listTitle, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Error { get => _error; set => SetError(value, FeedbackArea.General); }
    public string ReaderTitle { get => _readerTitle; private set => Set(ref _readerTitle, value); }
    public string ReaderMeta { get => _readerMeta; private set => Set(ref _readerMeta, value); }
    // 固定头部使用串号，实际填写的标题留在第一页主楼内，避免标题内容丢失。
    public string OriginalTitle => _original is null ? "" : PostCard.Optional(_original.Title, "无标题", State.HideSpoilers);
    public bool ListBusy { get => _listBusy; private set { Set(ref _listBusy, value); Changed(nameof(EmptyList)); Changed(nameof(ListLoading)); NotifyIncrementalLoading(); RefreshCommands(); } }
    public bool ThreadBusy { get => _threadBusy; private set { Set(ref _threadBusy, value); Changed(nameof(CanInteractThread)); Changed(nameof(HasNoThread)); Changed(nameof(ThreadOpening)); NotifyIncrementalLoading(); RefreshCommands(); } }
    public bool OnlyPo
    {
        get => _onlyPo;
        set
        {
            if (_onlyPo == value) return;
            ReaderLeaving?.Invoke();
            Set(ref _onlyPo, value); RebuildReading();
        }
    }
    public string JumpText { get => _jumpText; set => Set(ref _jumpText, value); }
    public string PageText { get => _pageText; set => Set(ref _pageText, value); }
    public string ListPageLabel => IsSubscriptions ? SubscriptionListLabel : IsLibrary ? $"已显示 {VisiblePostCount} / {_historyItems.Count} 个串"
        : _listStartPage == 1 ? $"已加载 {_listPage} 页 · {VisiblePostCount} 个串"
        : $"第 {(_listPage == _listStartPage ? _listPage.ToString() : $"{_listStartPage}–{_listPage}")} 页 · {VisiblePostCount} 个串";
    public string ThreadPageLabel => _threadStartPage == _threadPage ? $"第 {_threadPage} / {_totalPages} 页" : $"第 {_threadStartPage}–{_threadPage} / {_totalPages} 页";
    public string HistoryCount => State.History.Count.ToString();
    public double ReadingFontSize => State.FontSize;
    public double PreviewAuthorFontSize => ReadingFontSize - 1;
    public bool HideSpoilers => State.HideSpoilers;
    // 图片加载模式：串列表缩略图与串内图片分开设置，原图始终在看图窗口按需加载。
    public bool ListImagesAuto => ImageModes.List(State) == ImageModes.Auto;
    public bool ListImagesHidden => ImageModes.List(State) == ImageModes.Hidden;
    public bool ReaderImagesAuto => ImageModes.Reader(State) == ImageModes.Auto;
    public bool InertialScrolling => true;
    public string ThemeLabel => State.Theme == "dark" ? "浅色模式" : "深色模式";
    public bool IsDark => State.Theme == "dark";
    public bool IsLibrary => _library.Length > 0;
    public bool ShowNewThreadAction => !IsLibrary && ActiveForum is not null;
    public bool HasDrafts => State.Drafts.Any(d => !string.IsNullOrWhiteSpace(d.Content));
    // 通过事件通知窗口重置滚动位置，视图模型本身不持有 ListBox 等控件。
    public event Action? ReaderReset;
    public event Action? ReaderPrepending;
    public event Action? ReaderPrepended;
    public event Action? ListReset;
    public TaskCommand RefreshListCommand { get; }
    public TaskCommand JumpCommand { get; }
    public TaskCommand RefreshThreadCommand { get; }
    public TaskCommand JumpPageCommand { get; }
    public ActionCommand ToggleForumFavoriteCommand { get; }
    public ActionCommand ClearErrorCommand { get; }
    public ActionCommand LibraryCommand { get; }

    public MainViewModel(XIslandApi api, LocalStore store)
    {
        Api = api; Store = store; State = store.Load();
        ReadingTypography.Apply(State);
        NetworkClients.IslandUseProxy = State.IslandUseProxy;
        NetworkClients.UpdateUseProxy = State.UpdateUseProxy;
        try { Store.ConfigureCache(State); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
        { Error = "缓存目录不可用，使用默认目录：" + ex.Message; State.CacheParentPath = ""; Store.ConfigureCache(State); }
        ImageService.DiskCache = Store.Cache;
        Updates = new(this);
        Updates.Changed += () => { Changed(nameof(HasUpdate)); Changed(nameof(SettingsHint)); };
        VisiblePosts = new(Posts); InitializeBlocking();
        App.ApplySpoilerProtection(State.HideSpoilers);
        Posts.CollectionChanged += (_, _) => { Changed(nameof(ListPageLabel)); Changed(nameof(EmptyList)); };
        // 命令同时提供操作和可用条件，XAML 中的按钮与快捷键复用同一个入口。
        RefreshListCommand = new(_ => RefreshListAsync(), ex => Report(ex, FeedbackArea.List), () => !ListLoading);
        InitializeIncrementalLoading();
        JumpCommand = new(async _ =>
        {
            if (IslandLink.Parse(JumpText) is { Kind: IslandLinkKind.Thread } link)
            { await OpenThreadAsync(link.Id, link.Page, targetPostId: link.ReplyId); return; }
            var id = ContentText.ParseThreadId(JumpText) ?? throw new ArgumentException("输入串号、No.串号，或 X 岛串链接即可跳转。");
            await OpenThreadAsync(id);
        }, ex => Report(ex, FeedbackArea.Reader));
        RefreshThreadCommand = new(_ => OpenThreadAsync(_original!.Id, refresh: true), ex => Report(ex, FeedbackArea.Reader), () => HasThread && !ThreadBusy);
        JumpPageCommand = new(async _ =>
        {
            if (!int.TryParse(PageText, out var page) || page < 1 || page > _totalPages) throw new ArgumentException($"请输入 1–{_totalPages} 之间的页码。");
            await OpenThreadAsync(_original!.Id, page);
        }, ex => Report(ex, FeedbackArea.Reader), () => HasThread && !_disposed, allowConcurrent: true);
        ToggleForumFavoriteCommand = new(p => { if (p is Forum forum) ToggleForumFavorite(forum); });
        LibraryCommand = new(_ => ShowLibrary("history"));
        ClearErrorCommand = new(_ => Error = "");
        ApplyCookie(); BuildForumGroups(); InitializeSubscriptions();
        if (Store.LoadWarning is not null) Error = Store.LoadWarning;
    }

    public async Task InitializeAsync()
    {
        _ = LoadSubscriptionsAsync();
        try
        {
            Status = "正在获取版块";
            State.Forums = await Api.GetForumsAsync(); BuildForumGroups(); Save();
        }
        catch (Exception ex) { Report(ex); }
        // 刷新版块目录失败时仍可利用已保存目录，或尝试默认综合线入口。
        var forum = State.Forums.Find(f => f.IsTimeline && f.Id == 1) ?? new Forum(1, "综合线", "时间线", "", true, 20);
        await SelectForumAsync(forum);
    }

    private void BuildForumGroups()
    {
        ForumGroups.Clear();
        foreach (var group in State.Forums.GroupBy(f => f.Group))
            ForumGroups.Add(new(group.Key, group.ToList()));
        SyncFavoriteForums();
    }

    /// <summary>切换本机版块收藏；只更新导航状态，不触发选版、读串或网络请求。</summary>
    public void ToggleForumFavorite(Forum forum)
    {
        if (forum.Id <= 0) return;
        var removed = State.FavoriteForums.RemoveAll(f => f.Key == forum.Key) > 0;
        if (!removed) State.FavoriteForums.Add(forum);
        SyncFavoriteForums(); Save();
        Notify(removed ? $"已取消收藏板块：{forum.Name}" : $"已收藏板块：{forum.Name}", FeedbackArea.Navigation);
    }

    private void SyncFavoriteForums()
    {
        // 目录刷新后更新名称等信息；暂时缺少的入口仍保留原快照，避免维护/权限变化丢失收藏。
        var current = State.Forums.GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.First());
        State.FavoriteForums = State.FavoriteForums
            .Select(f => current.GetValueOrDefault(f.Key) ?? f).ToList();
        // 按键增量同步，收藏一个入口不会重建其他入口，尽量保留键盘焦点和悬停状态。
        for (var i = 0; i < State.FavoriteForums.Count; i++)
        {
            var item = State.FavoriteForums[i];
            if (i < FavoriteForums.Count && FavoriteForums[i].Key == item.Key)
            {
                if (FavoriteForums[i] != item) FavoriteForums[i] = item;
                continue;
            }
            var previous = FavoriteForums.ToList().FindIndex(f => f.Key == item.Key);
            if (previous >= 0)
            {
                FavoriteForums.Move(previous, i);
                if (FavoriteForums[i] != item) FavoriteForums[i] = item;
            }
            else FavoriteForums.Insert(i, item);
        }
        while (FavoriteForums.Count > State.FavoriteForums.Count) FavoriteForums.RemoveAt(FavoriteForums.Count - 1);
        _favoriteForumKeys = State.FavoriteForums.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        Changed(nameof(FavoriteForumKeys)); Changed(nameof(HasFavoriteForums));
    }

    public async Task SelectForumAsync(Forum forum, int page = 1)
    {
        // 切换版块只更新中栏；右侧已经打开的串继续保留，方便对照阅读。
        _library = ""; ActiveForum = forum; _listStartPage = _listPage = page;
        _listHasMore = false; _listAppendFailed = false;
        ListTitle = forum.Name;
        Posts.Clear(); Changed(nameof(IsLibrary)); Changed(nameof(IsSubscriptions)); Changed(nameof(ActiveForumKey)); Changed(nameof(ShowNewThreadAction));
        await LoadListAsync(page);
    }

    private async Task LoadListAsync(int page, bool refresh = false, bool append = false)
    {
        if (ActiveForum is null || _disposed || append && ListBusy) return;
        _listCts?.Cancel(); _listCts?.Dispose(); _listCts = new();
        var ct = _listCts.Token; var version = ++_listVersion; var forum = ActiveForum;
        _listAppending = append; _listAppendFailed = false;
        ListBusy = true; Error = ""; Status = append ? "正在续载串列表" : "正在加载串列表";
        try
        {
            var posts = await ReadListAsync(forum, page, refresh, ct);
            // 新版块请求已开始或窗口已关闭时，不能再把旧列表写回界面。
            if (version != _listVersion || _disposed) return;
            if (!append) { Posts.Clear(); _listStartPage = page; }
            var ids = Posts.Select(p => p.Post.Id).ToHashSet();
            var added = 0;
            foreach (var post in posts) if (ids.Add(post.Id)) { Posts.Add(Card(post)); added++; }
            // 空页或整页重复时停下，避免服务端异常令滚动触发无休止请求。
            _listHasMore = added > 0 && page < forum.MaxPage;
            _listPage = page; Changed(nameof(ListPageLabel)); Status = $"已更新 · {DateTime.Now:HH:mm}";
            if (!append) ListReset?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (version == _listVersion) { _listAppendFailed = append; Report(ex, FeedbackArea.List); } }
        finally { if (version == _listVersion) ListBusy = false; }
    }

    public async Task RefreshListAsync()
    {
        if (IsSubscriptions) await LoadSubscriptionsAsync(true);
        else if (IsLibrary) ShowLibrary("history");
        else await LoadListAsync(1, true);
    }

    public void ShowLibrary(string name)
    {
        // 本机视图不请求 API，但仍要让未结束的在线列表请求失效。
        _listCts?.Cancel(); ++_listVersion; _library = "history"; ListBusy = false;
        ListTitle = "最近浏览";
        _listAppendFailed = false; _historyItems = State.History.OrderByDescending(s => s.ReadAt).ToList();
        Posts.Clear(); AppendHistory(); ListReset?.Invoke();
        Changed(nameof(IsLibrary)); Changed(nameof(IsSubscriptions)); Changed(nameof(ActiveForumKey)); Changed(nameof(ShowNewThreadAction)); RefreshCommands(); Status = "已显示本机记录";
    }

    /// <summary>显式跳页只显示目标页；续载可向两端扩展连续页区间，分别维护起点和终点。</summary>
    public async Task OpenThreadAsync(long id, int? requestedPage = null, bool append = false, bool refresh = false, bool prepend = false, long? targetPostId = null)
    {
        var extending = append || prepend;
        if (_disposed || extending && (ThreadBusy || _original?.Id != id)) return;
        if (!extending) ReaderLeaving?.Invoke();
        var resume = !extending && requestedPage is null ? State.History.Find(h => h.Post.Id == id) : null;
        var page = requestedPage ?? resume?.Page ?? 1;
        _threadCts?.Cancel(); _threadCts?.Dispose(); _threadCts = new();
        var ct = _threadCts.Token; var version = ++_threadVersion;
        // 固定本次请求的缓存身份，即使等待期间换了饼干，也不能写进新身份的目录。
        var identity = State.ActiveCookieId;
        _threadAppending = append; _threadPrepending = prepend;
        if (!prepend) _threadAppendFailed = false;
        if (!append) _threadPrependFailed = false;
        // 页码是跳转输入，不让后台续载或较晚返回的请求覆盖用户正在输入的数字。
        if (!extending) PageText = page.ToString();
        ThreadBusy = true; Error = ""; Status = extending ? "正在续载回复" : "正在加载正文";
        try
        {
            ThreadPage result; var offline = false;
            try
            {
                result = await ReadThreadAsync(id, page, refresh, ct);
                // 写缓存失败只提示保存问题，不阻止展示已经成功下载的正文。
                try { Store.SavePage(result, identity); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetError("正文已加载，但无法写入离线缓存。", FeedbackArea.Reader); }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !ct.IsCancellationRequested)
            {
                // 仅网络失败或超时尝试离线回退；主动取消和服务端权限错误不会触发缓存回看。
                var cached = Store.ReadPage(id, page, identity);
                if (cached is null) throw;
                result = cached;
                offline = true;
            }
            if (version != _threadVersion || ct.IsCancellationRequested || _disposed) return;
            if (!extending)
            {
                // 网络等待期间仍可阅读旧串；真正替换前再保存一次眼前位置。
                ReaderLeaving?.Invoke();
                _replies.Clear(); _replyLocations.Clear(); _readingPages.Clear(); _threadStartPage = _threadPage = page;
                ReaderRestorePosition = resume?.Position;
                if (resume?.Position is { } position) { _onlyPo = position.OnlyPo; Changed(nameof(OnlyPo)); }
            }
            _original = result.Original;
            if (targetPostId is not null && !extending)
            {
                var targetIndex = result.Replies.FindIndex(p => p.Id == targetPostId);
                if (targetIndex >= 0 || targetPostId == _original.Id && page == 1)
                {
                    // 链接指向明确楼层时确保它可见，再交给已有阅读定位器恢复位置。
                    _onlyPo = false; Changed(nameof(OnlyPo));
                    ReaderRestorePosition = new(targetPostId.Value, targetIndex, 0, false);
                }
            }
            _totalPages = Math.Max(Math.Max(page, _threadPage), result.TotalPages);
            var ids = _replies.Select(p => p.Id).ToHashSet();
            var added = result.Replies.Where(p => ids.Add(p.Id)).ToList();
            for (var i = 0; i < result.Replies.Count; i++)
                _replyLocations.TryAdd(result.Replies[i].Id, (page, i));
            var readingPage = PageReading(result, added.Select(p => p.Id).ToHashSet());
            _readingPages[page] = readingPage;
            if (prepend)
            {
                _threadStartPage = page; _replies.InsertRange(0, added);
                _threadHasPrevious = page > 1 && added.Count > 0;
            }
            else
            {
                _threadPage = page; _replies.AddRange(added);
                _threadHasMore = page < _totalPages && added.Count > 0;
                if (!append) _threadHasPrevious = page > 1;
            }
            ReaderTitle = _original.Number;
            ReaderMeta = ForumName(_original.Fid);
            if (prepend) PrependReading(readingPage, page == 1);
            else if (append) AppendReading(readingPage);
            else RebuildReading();
            // 续载不等于读到该页；实际位置由阅读视口节流上报。
            if (!extending)
            {
                RegisterReadingHistory(new(_original, page, DateTimeOffset.Now, resume?.Position)); Save();
            }
            NotifyReader(); Changed(nameof(HistoryCount));
            Status = offline ? "离线阅读 · 显示本机缓存，内容可能不是最新" : "正文已更新";
            if (offline) SetError(Status, FeedbackArea.Reader);
            // 累积加载时保持当前位置；直接打开或跳页时才滚回本次内容顶部。
            if (!extending) ReaderReset?.Invoke();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (version == _threadVersion)
            {
                if (append) _threadAppendFailed = true;
                if (prepend) _threadPrependFailed = true;
                Report(ex, FeedbackArea.Reader);
            }
        }
        finally { if (version == _threadVersion) ThreadBusy = false; }
        // 串正文独立完成；订阅快照由启动/订阅页维护，打开新串不扫描全部订阅。
    }

    private void RebuildReading()
    {
        // 主楼仅属于第 1 页。其他页用 Original 判断 PO 和标题，不再次插入主楼。
        ReadingPosts.Clear();
        if (_original is null) return;
        if (_threadStartPage == 1) ReadingPosts.Add(new(_original, true, true));
        AppendReading(_readingPages.Values.SelectMany(p => p));
    }
    private PostCard Card(Post p, string progress = "", bool showReplies = true)
    {
        var card = new PostCard(p, ForumName(p.Fid), progress, showReplies); card.SetSpoilerProtection(State.HideSpoilers); return card;
    }
    public string ForumName(int id) => State.Forums.Find(f => !f.IsTimeline && f.Id == id)?.Name ?? $"版块 {id}";
    public string ActiveCookie()
    {
        // State 里只存密文；需要设置请求身份时才调用 Windows DPAPI 解密。
        var identity = State.Cookies.Find(c => c.Id == State.ActiveCookieId);
        return identity is null ? "" : SecretProtection.Unprotect(identity.EncryptedValue);
    }
    public void ApplyCookie()
    {
        // 关闭饼干管理但没有更换身份时，避免清空用户正在看的内容。
        if (_appliedCookieId == State.ActiveCookieId) return;
        ReaderLeaving?.Invoke();
        _listCts?.Cancel(); _threadCts?.Cancel(); ++_listVersion; ++_threadVersion;
        ListBusy = false; ThreadBusy = false;
        try { Api.SetCookie(ActiveCookie()); }
        catch (Exception) { State.ActiveCookieId = ""; Api.SetCookie(null); Error = "无法解密饼干，请在这台电脑上重新导入。"; }
        _appliedCookieId = State.ActiveCookieId;
        _recentLists.Clear(); _recentThreads.Clear();
        ResetSubscriptions();
        RestoreSubscriptionSnapshot();
        // 身份改变后清空当前列表和正文，避免继续展示上一身份加载的受限内容。
        _original = null; _replies.Clear(); _replyLocations.Clear(); _readingPages.Clear(); ReaderRestorePosition = null; ReadingPosts.Clear(); Posts.Clear(); NotifyReader();
        _listHasMore = false; _threadHasMore = false; _listAppendFailed = false; _threadAppendFailed = false;
        _threadHasPrevious = false; _threadPrependFailed = false; _threadPrepending = false;
        NotifyIncrementalLoading();
    }
    public void AppearanceChanged()
    {
        ReadingTypography.Apply(State);
        App.ApplySpoilerProtection(State.HideSpoilers);
        foreach (var card in Posts) card.SetSpoilerProtection(State.HideSpoilers);
        Changed(nameof(OriginalTitle));
        Changed(nameof(HideSpoilers));
        Changed(nameof(ReadingFontSize)); Changed(nameof(PreviewAuthorFontSize)); Changed(nameof(ListImagesAuto)); Changed(nameof(ListImagesHidden)); Changed(nameof(ReaderImagesAuto)); Changed(nameof(InertialScrolling)); Changed(nameof(ThemeLabel)); Changed(nameof(IsDark)); Save();
    }
    public void Save()
    {
        try { Store.Save(State); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = "无法保存本机数据，请检查数据目录是否可写。"; }
    }
    public void SuppressNotice(SiteNotice notice)
    {
        if (!State.SuppressedNoticeKeys.Contains(notice.Key)) State.SuppressedNoticeKeys.Add(notice.Key);
        State.SuppressedNoticeKeys = State.SuppressedNoticeKeys.TakeLast(100).ToList();
        Save();
    }
    public void Report(Exception ex) => Report(ex, FeedbackArea.General);
    public void Report(Exception ex, FeedbackArea area)
    {
        SetError(ex switch
        {
            HttpRequestException => "暂时连不上 X 岛，请检查网络或系统代理后重试。",
            OperationCanceledException => "请求超时，请稍后重试。",
            _ => ex.Message
        }, area);
        Status = "操作未完成";
    }
    private void NotifyReader()
    {
        // 这些是由内部字段计算的属性，需要显式通知 WPF 重新读取绑定值。
        foreach (var p in new[] { nameof(Original), nameof(OriginalTitle), nameof(HasThread), nameof(HasNoThread), nameof(CanInteractThread), nameof(ThreadPageLabel), nameof(SubscriptionLabel), nameof(IsSubscribed), nameof(CurrentSubscriptionKnown) }) Changed(p);
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        RefreshListCommand?.Refresh();
        RefreshThreadCommand?.Refresh(); JumpPageCommand?.Refresh(); SubscriptionCommand?.Refresh();
        LoadMoreListCommand?.Refresh(); LoadMoreThreadCommand?.Refresh(); RetryListLoadCommand?.Refresh(); RetryThreadLoadCommand?.Refresh();
        LoadPreviousThreadCommand?.Refresh(); RetryPreviousThreadCommand?.Refresh();
    }
    public void Dispose()
    {
        // 窗口退出时使所有在途操作失效，并释放本视图模型拥有的网络资源。
        _disposed = true; _subscriptionEpoch++; _subscriptionCts?.Cancel(); _subscriptionCts?.Dispose(); _listCts?.Cancel(); _threadCts?.Cancel(); _listCts?.Dispose(); _threadCts?.Dispose(); Api.Dispose();
    }
}

using System.Collections.ObjectModel;
using Tide.Core;

namespace Tide.Desktop;

public sealed class SubscriptionProfile(string id, string name, string value) : Observable
{
    public string Id { get; } = id;
    public string Name { get; private set; } = name;
    public string Value { get; private set; } = value;
    // 备注是用户自填的普通名称：有备注时作主名称，号码退为次级文字；没有备注时直接显示号码。
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Value : Name.Trim();
    public string Detail => string.IsNullOrWhiteSpace(Name) ? "" : Value;
    public void Update(string name, string value)
    {
        Name = name; Value = value; Changed(nameof(Name)); Changed(nameof(Value)); Changed(nameof(DisplayName)); Changed(nameof(Detail));
    }
}

public sealed partial class MainViewModel
{
    private string _subscriptionId = "";
    private List<Post> _subscriptions = [];
    private readonly HashSet<long> _uncertainSubscriptions = [];
    private bool _subscriptionKnown, _subscriptionBusy, _subscriptionWriting;
    private bool _subscriptionCached;
    private bool _updatingSubscriptionAccounts;
    private DateTimeOffset _subscriptionUpdatedAt, _subscriptionAttempt;
    private int _subscriptionLoaded;
    private CancellationTokenSource? _subscriptionCts;
    private int _subscriptionEpoch;
    private Task? _subscriptionLoad;
    private string? _subscriptionError;
    private const int SubscriptionPageSize = 10;
    private static readonly TimeSpan SubscriptionFreshness = TimeSpan.FromMinutes(5);

    public ObservableCollection<SubscriptionProfile> SubscriptionAccounts { get; } = [];
    public string ActiveSubscriptionKey
    {
        // 重建列表时，移除原选中项会让 ComboBox 写回空值并立即重读；此时返回空，等列表就绪后再通知，
        // 否则选择框会停在 SelectedValue 有值而 SelectedItem 为空的状态。
        get => _updatingSubscriptionAccounts ? "" : State.ActiveSubscriptionId;
        set { if (!_updatingSubscriptionAccounts && !string.IsNullOrEmpty(value) && value != State.ActiveSubscriptionId) SwitchSubscription(value); }
    }
    public bool IsSubscriptions => _library == "subscriptions";
    public bool SubscriptionKnown => _subscriptionKnown;
    public bool SubscriptionBusy => _subscriptionBusy;
    // 局部列表中找到即为已订阅；没找到但尚未读完时只能说未知，不能误判未订阅。
    public bool IsSubscribed => _original is not null && _subscriptions.Any(p => p.Id == _original.Id);
    public bool CurrentSubscriptionKnown => _original is not null && !_uncertainSubscriptions.Contains(_original.Id) && (_subscriptionKnown || IsSubscribed);
    public string SubscriptionCount => _subscriptionKnown ? _subscriptions.Count.ToString() : _subscriptions.Count > 0 ? $"{_subscriptions.Count}+" : "—";
    public string SubscriptionLabel => _subscriptionWriting ? "正在保存订阅" : !CurrentSubscriptionKnown ? "核对订阅状态" : IsSubscribed ? "取消订阅" : "订阅";
    public string SubscriptionAccountStatus => _subscriptionId.Length > 0 ? "订阅号已保存" : "未设置订阅号";
    public string SubscriptionSyncStatus => _subscriptionBusy ? (_subscriptionWriting ? "正在保存" : $"正在同步 · 已读取 {_subscriptionLoaded} 个")
        : _uncertainSubscriptions.Count > 0 ? "有订阅操作待核对 · 刷新可重新同步"
        : _subscriptionUpdatedAt == default ? "尚无缓存" : $"{(_subscriptionKnown ? (_subscriptionCached ? "缓存" : "已同步") : "部分缓存")} · {_subscriptionUpdatedAt.ToLocalTime():MM-dd HH:mm}";
    public string SubscriptionListLabel => $"已显示 {VisiblePostCount} / {_subscriptions.Count}{(_subscriptionKnown ? "" : "+")} 个订阅";
    public TaskCommand SubscriptionCommand { get; private set; } = null!;
    // 导航是即时同步动作，不让长期同步任务占用命令而禁用入口。
    public ActionCommand SubscriptionsCommand { get; private set; } = null!;

    private void InitializeSubscriptions()
    {
        SubscriptionCommand = new(_ => ToggleSubscriptionAsync(), Report, () => CanInteractThread && !_subscriptionWriting && _subscriptionId.Length > 0);
        SubscriptionsCommand = new(_ => { _ = ShowSubscriptionsAsync(); });
        try
        {
            if (State.SubscriptionAccounts.Count == 0)
            {
                var number = State.EncryptedSubscriptionId.Length > 0 ? SecretProtection.Unprotect(State.EncryptedSubscriptionId) : Guid.NewGuid().ToString();
                var account = new SubscriptionIdentity(Guid.NewGuid().ToString(), "默认", SecretProtection.Protect(number));
                State.SubscriptionAccounts.Add(account); State.ActiveSubscriptionId = account.Id;
                State.EncryptedSubscriptionId = ""; Store.Save(State);
            }
            ReloadSubscriptionAccounts();
            var selected = SubscriptionAccounts.FirstOrDefault(a => a.Id == State.ActiveSubscriptionId) ?? SubscriptionAccounts.First();
            State.ActiveSubscriptionId = selected.Id; _subscriptionId = selected.Value;
            RestoreSubscriptionSnapshot();
        }
        catch (Exception) { Error = "无法保存或读取订阅号，请在订阅号管理中重新设置。"; }
    }

    private void ReloadSubscriptionAccounts()
    {
        _updatingSubscriptionAccounts = true;
        try
        {
            // 保留选中对象实例；清空后重建会使 ComboBox 在备注保存后丢失当前显示。
            for (var index = 0; index < State.SubscriptionAccounts.Count; index++)
            {
                var account = State.SubscriptionAccounts[index];
                var existing = SubscriptionAccounts.FirstOrDefault(a => a.Id == account.Id);
                var number = SecretProtection.Unprotect(account.EncryptedValue);
                if (existing is null) SubscriptionAccounts.Insert(index, new(account.Id, account.Name ?? "", number));
                else
                {
                    existing.Update(account.Name ?? "", number);
                    var previousIndex = SubscriptionAccounts.IndexOf(existing);
                    if (previousIndex != index) SubscriptionAccounts.Move(previousIndex, index);
                }
            }
            while (SubscriptionAccounts.Count > State.SubscriptionAccounts.Count) SubscriptionAccounts.RemoveAt(SubscriptionAccounts.Count - 1);
        }
        finally { _updatingSubscriptionAccounts = false; }
        Changed(nameof(ActiveSubscriptionKey)); Changed(nameof(SubscriptionAccountStatus));
    }

    public string GetSubscriptionId() => _subscriptionId;

    public void SetSubscriptionId(string value)
    {
        var number = XIslandApi.NormalizeSubscriptionId(value);
        var accounts = SubscriptionAccounts.ToList();
        var account = accounts.Find(a => a.Value == number);
        if (account is null) { account = new(Guid.NewGuid().ToString(), "", number); accounts.Add(account); }
        SaveSubscriptionAccounts(accounts, account.Id);
    }

    public void SaveSubscriptionAccounts(IEnumerable<SubscriptionProfile> values, string activeId)
    {
        var accounts = values.ToList();
        if (accounts.Count == 0) throw new ArgumentException("请至少保留一个订阅号。");
        if (accounts.Select(a => a.Id).Distinct().Count() != accounts.Count || !accounts.Any(a => a.Id == activeId)) throw new ArgumentException("请选择当前订阅号。");
        foreach (var account in accounts) XIslandApi.NormalizeSubscriptionId(account.Value);
        if (accounts.Select(a => a.Value).Distinct(StringComparer.Ordinal).Count() != accounts.Count) throw new ArgumentException("存在相同的订阅号，请合并重复项。");
        var persisted = accounts.Select(a => new SubscriptionIdentity(a.Id, a.Name, SecretProtection.Protect(a.Value))).ToList();
        var previous = State.SubscriptionAccounts; var previousId = State.ActiveSubscriptionId;
        try { State.SubscriptionAccounts = persisted; State.ActiveSubscriptionId = activeId; Store.Save(State); }
        catch { State.SubscriptionAccounts = previous; State.ActiveSubscriptionId = previousId; throw; }
        var selected = accounts.Single(a => a.Id == activeId);
        var numberChanged = _subscriptionId != selected.Value;
        _subscriptionId = selected.Value; ReloadSubscriptionAccounts();
        if (numberChanged)
        {
            ResetSubscriptions(); RestoreSubscriptionSnapshot(); _subscriptionVisibleCount = SubscriptionPageSize;
            if (IsSubscriptions) { Posts.Clear(); RenderSubscriptions(); ListReset?.Invoke(); }
        }
        NotifySubscriptions();
    }

    private void SwitchSubscription(string key)
    {
        try
        {
            SaveSubscriptionAccounts(SubscriptionAccounts, key);
            if (IsSubscriptions) _ = LoadSubscriptionsAsync();
        }
        catch (Exception ex) { Report(ex); Changed(nameof(ActiveSubscriptionKey)); }
    }

    private void ResetSubscriptions()
    {
        _subscriptionEpoch++; _subscriptionCts?.Cancel(); _subscriptionCts?.Dispose(); _subscriptionCts = null;
        _subscriptionLoad = null; _subscriptions = []; _subscriptionKnown = false; _subscriptionBusy = false; _subscriptionWriting = false;
        _uncertainSubscriptions.Clear(); _subscriptionUpdatedAt = default; _subscriptionAttempt = default; _subscriptionLoaded = 0;
        if (Error == _subscriptionError) Error = "";
        _subscriptionError = null;
        NotifySubscriptions();
    }

    private void RestoreSubscriptionSnapshot()
    {
        if (_subscriptionId.Length == 0) return;
        var snapshot = Store.ReadSubscriptions(_subscriptionId, State.ActiveCookieId);
        if (snapshot is not null)
        {
            _subscriptions = snapshot.Posts.DistinctBy(p => p.Id).ToList(); _subscriptionKnown = snapshot.Complete;
            _subscriptionUpdatedAt = snapshot.UpdatedAt; _uncertainSubscriptions.UnionWith(snapshot.Uncertain);
            _subscriptionCached = true;
        }
        NotifySubscriptions();
    }

    private async Task PersistSubscriptionSnapshotAsync()
    {
        var number = _subscriptionId; var cookie = State.ActiveCookieId; var epoch = _subscriptionEpoch;
        var snapshot = new SubscriptionSnapshot(_subscriptions.ToList(), _subscriptionKnown, _subscriptionUpdatedAt, _uncertainSubscriptions.ToList());
        try { await Task.Run(() => Store.SaveSubscriptions(number, cookie, snapshot)); }
        catch (Exception) { if (epoch == _subscriptionEpoch && !_disposed) Error = "订阅已读取，但无法保存临时缓存。"; }
    }

    private void NotifySubscriptions()
    {
        foreach (var name in new[] { nameof(IsSubscribed), nameof(CurrentSubscriptionKnown), nameof(SubscriptionKnown), nameof(SubscriptionBusy), nameof(SubscriptionCount), nameof(SubscriptionLabel), nameof(SubscriptionSyncStatus), nameof(SubscriptionListLabel), nameof(ListPageLabel), nameof(EmptyList), nameof(ListLoading), nameof(ListLoadingText) }) Changed(name);
        NotifyIncrementalLoading(); SubscriptionCommand?.Refresh(); RefreshCommands();
    }

    public Task LoadSubscriptionsAsync(bool refresh = false)
    {
        if (_disposed || _subscriptionWriting) return Task.CompletedTask;
        if (_subscriptionLoad is { IsCompleted: false }) return _subscriptionLoad;
        // 短期内重复导航只看快照。手动刷新绕过时效，失败后的导航设冷却防止连点重试。
        if (!refresh && (_subscriptionKnown && DateTimeOffset.UtcNow - _subscriptionUpdatedAt < SubscriptionFreshness || DateTimeOffset.UtcNow - _subscriptionAttempt < TimeSpan.FromSeconds(15))) return Task.CompletedTask;
        return _subscriptionLoad = ReadSubscriptionsAsync();
    }

    private async Task ReadSubscriptionsAsync()
    {
        if (_subscriptionId.Length == 0) { Error = "请先设置订阅号。"; return; }
        _subscriptionCts?.Dispose(); _subscriptionCts = new();
        var ct = _subscriptionCts.Token; var epoch = _subscriptionEpoch; var id = _subscriptionId;
        var hadCompleteSnapshot = _subscriptionKnown;
        _subscriptionAttempt = DateTimeOffset.UtcNow; _subscriptionLoaded = 0; _subscriptionBusy = true; NotifySubscriptions();
        try
        {
            var posts = await Api.GetSubscriptionsAsync(id, ct, async partial =>
            {
                if (epoch != _subscriptionEpoch || _disposed || ct.IsCancellationRequested) return;
                _subscriptionLoaded = partial.Count;
                if (!hadCompleteSnapshot)
                {
                    // 部分快照重试时先保留已读尾部；直到完整同步成功，才删除服务端已不存在的条目。
                    // 否则重新读取第一页就会把用户滚到的后续内容暂时清空。
                    var seen = partial.Select(p => p.Id).ToHashSet();
                    _subscriptions = partial.Concat(_subscriptions.Where(p => !seen.Contains(p.Id))).ToList();
                    _subscriptionUpdatedAt = DateTimeOffset.UtcNow;
                    if (IsSubscriptions) RenderSubscriptions();
                }
                NotifySubscriptions();
                // 首屏及后续检查点落盘，首次导入未完成就退出也能恢复部分列表。
                if (!hadCompleteSnapshot && (partial.Count <= 10 || partial.Count % 100 == 0)) await PersistSubscriptionSnapshotAsync();
            });
            if (epoch != _subscriptionEpoch || _disposed || ct.IsCancellationRequested) return;
            _subscriptions = posts; _subscriptionKnown = true; _subscriptionCached = false;
            _subscriptionUpdatedAt = DateTimeOffset.UtcNow; _uncertainSubscriptions.Clear();
            if (IsSubscriptions) RenderSubscriptions();
            if (Error == _subscriptionError) Error = "";
            _subscriptionError = null;
            await PersistSubscriptionSnapshotAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (epoch == _subscriptionEpoch && !_disposed)
            {
                // 失败保留最后可用列表。局部结果仍标记为局部，不能证明某串不在订阅中。
                _subscriptionCached = true; Report(ex, FeedbackArea.List); _subscriptionError = Error;
                if (_subscriptionUpdatedAt != default) await PersistSubscriptionSnapshotAsync();
            }
        }
        finally { if (epoch == _subscriptionEpoch && !_disposed) { _subscriptionBusy = false; NotifySubscriptions(); } }
    }

    public Task ShowSubscriptionsAsync()
    {
        _listCts?.Cancel(); ++_listVersion; ListBusy = false;
        var alreadyHere = IsSubscriptions; _library = "subscriptions"; ListTitle = "我的订阅";
        if (!alreadyHere) { _subscriptionVisibleCount = SubscriptionPageSize; Posts.Clear(); }
        Changed(nameof(IsLibrary)); Changed(nameof(IsSubscriptions)); Changed(nameof(ActiveForumKey)); Changed(nameof(ShowNewThreadAction));
        RenderSubscriptions(); NotifySubscriptions();
        if (!alreadyHere) ListReset?.Invoke();
        return LoadSubscriptionsAsync();
    }

    private void RenderSubscriptions()
    {
        var desired = _subscriptions.Take(_subscriptionVisibleCount).ToList();
        // 同号后台同步采用增量对齐；追加时不清空列表，保留选中项、图片及滚动锚点。
        for (var index = 0; index < desired.Count; index++)
        {
            var post = desired[index];
            if (index < Posts.Count && Posts[index].Post == post) continue;
            var previous = Posts.ToList().FindIndex(index, p => p.Post.Id == post.Id);
            if (previous >= 0)
            {
                if (previous != index) Posts.Move(previous, index);
                if (Posts[index].Post != post) Posts[index] = Card(post, Posts[index].Progress, showReplies: false);
            }
            else
            {
                var history = State.History.Find(h => h.Post.Id == post.Id);
                Posts.Insert(index, Card(post, history is null ? "" : $"读至第 {history.Page} 页", showReplies: false));
            }
        }
        while (Posts.Count > desired.Count) Posts.RemoveAt(Posts.Count - 1);
        Changed(nameof(ListPageLabel)); NotifyIncrementalLoading(); RefreshCommands();
    }

    public async Task ToggleSubscriptionAsync()
    {
        if (_subscriptionWriting || _original is null || _disposed) return;
        if (!CurrentSubscriptionKnown) { await LoadSubscriptionsAsync(true); return; }
        // 写入与正在进行的扫描不能竞争；取消扫描后用确认响应更新快照。
        _subscriptionEpoch++; _subscriptionCts?.Cancel(); _subscriptionLoad = null;
        var post = _original; var add = !IsSubscribed; var epoch = _subscriptionEpoch; var id = _subscriptionId;
        _subscriptionCts?.Dispose(); _subscriptionCts = new(); var ct = _subscriptionCts.Token;
        _subscriptionWriting = true; _subscriptionBusy = true; NotifySubscriptions(); Error = "";
        try
        {
            await Api.SetSubscriptionAsync(id, post.Id, add, ct);
            if (epoch != _subscriptionEpoch || ct.IsCancellationRequested || _disposed) return;
            _subscriptions.RemoveAll(p => p.Id == post.Id);
            if (add) _subscriptions.Insert(0, post);
            _uncertainSubscriptions.Remove(post.Id); _subscriptionCached = false;
            if (_subscriptionUpdatedAt == default) _subscriptionUpdatedAt = DateTimeOffset.UtcNow;
            if (IsSubscriptions) RenderSubscriptions();
            Notify(add ? "已订阅" : "已取消订阅", FeedbackArea.Reader);
            await PersistSubscriptionSnapshotAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (epoch == _subscriptionEpoch && !_disposed)
            {
                _uncertainSubscriptions.Add(post.Id); Report(ex, FeedbackArea.Reader); _subscriptionError = Error;
                await PersistSubscriptionSnapshotAsync();
            }
        }
        finally { if (epoch == _subscriptionEpoch && !_disposed) { _subscriptionWriting = false; _subscriptionBusy = false; NotifySubscriptions(); } }
    }
}

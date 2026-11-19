using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

/// <summary>版块收藏的离线行为检查：固定传输层与隔离资料目录，不访问论坛或使用真实饼干。</summary>
internal static class ForumFavoritesStateChecks
{
    public static async Task RunAsync(string output)
    {
        var profiles = Path.Combine(output, "forum-favorites-state", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profiles);
        var results = new Dictionary<string, object>();

        var legacy = new LocalStore(Path.Combine(profiles, "legacy"));
        File.WriteAllText(Path.Combine(legacy.Root, "state.json"), """{"Theme":"dark","Favorites":[],"Forums":[]} """);
        var legacyHandler = new FixtureHandler();
        using (var vm = Create(legacy, legacyHandler))
        {
            Require(!vm.HasFavoriteForums && vm.FavoriteForumKeys.Count == 0 && vm.FavoriteForums.Count == 0,
                "旧资料缺少收藏字段时没有使用空集合。");
            Require(vm.State.Theme == "dark" && legacyHandler.Paths.Count == 0, "加载旧资料改变了外观或发起了请求。");
        }
        File.WriteAllText(Path.Combine(legacy.Root, "state.json"), """{"FavoriteForums":null} """);
        Require(legacy.Load().FavoriteForums.Count == 0, "null 收藏字段未兼容。");
        File.WriteAllText(Path.Combine(legacy.Root, "state.json"), """
            {"FavoriteForums":[null,{"Id":0,"Name":"无效","Group":"综合"},
              {"Id":1,"Name":"普通版块","Group":"综合"},{"Id":1,"Name":"重复","Group":"综合"},
              {"Id":1,"Name":null,"Group":null,"IsTimeline":true,"MaxPage":0}]}
            """);
        var normalized = legacy.Load().FavoriteForums;
        Require(normalized.Select(f => f.Key).SequenceEqual(new[] { "f1", "t1" }), "空项/无效项/重复项未去除，或两类同 ID 被错误合并。");
        Require(normalized[1].Name == "时间线 1" && normalized[1].MaxPage == 1, "损坏的入口显示信息未归一化。");
        results["legacyNullAndDuplicateCompatibility"] = true;

        var board = new Forum(1, "旧版块名称", "综合", "旧版规");
        var timeline = new Forum(1, "旧时间线", "时间线", "", true, 20);
        var missing = new Forum(9, "暂缺版块", "兴趣");
        var store = new LocalStore(Path.Combine(profiles, "roundtrip"));
        store.Save(new UserState
        {
            Forums = [timeline, board, missing],
            History = [new(new Post { Id = 101, Content = "历史记录保持独立" }, 1, DateTimeOffset.UnixEpoch)]
        });
        var handler = new FixtureHandler();
        using (var vm = Create(store, handler))
        {
            await vm.SelectForumAsync(timeline);
            vm.ShowLibrary("history");
            var requestCount = handler.Paths.Count;
            var title = vm.ListTitle;
            var changeNames = new List<string?>();
            vm.PropertyChanged += (_, e) => changeNames.Add(e.PropertyName);

            vm.ToggleForumFavoriteCommand.Execute(board);
            vm.ToggleForumFavorite(timeline);
            Require(vm.FavoriteForums.Select(f => f.Key).SequenceEqual(new[] { "f1", "t1" }), "收藏顺序或时间线/版块身份不正确。");
            Require(vm.FavoriteForumKeys.SetEquals(new[] { "f1", "t1" }) && vm.HasFavoriteForums, "星标键与快捷收藏集合没有同步。");
            Require(changeNames.Contains(nameof(vm.FavoriteForumKeys)) && changeNames.Contains(nameof(vm.HasFavoriteForums)), "收藏状态未通知绑定刷新。");
            Require(vm.IsLibrary && vm.ListTitle == title && vm.ActiveForum?.Key == timeline.Key && handler.Paths.Count == requestCount,
                "收藏操作切换了当前视图或发起了网络请求。");
            Require(vm.State.History.Count == 1 && vm.Posts.Single().Post.Id == 101, "版块收藏改变了历史记录或当前列表。");

            vm.ToggleForumFavorite(board);
            Require(!vm.FavoriteForumKeys.Contains("f1") && vm.FavoriteForumKeys.Contains("t1"), "取消普通版块错误地影响了同 ID 时间线。");
            vm.ToggleForumFavorite(board);
            vm.ToggleForumFavorite(missing);
            Require(store.Load().FavoriteForums.Select(f => f.Key).SequenceEqual(new[] { "t1", "f1", "f9" }), "新增/取消没有持久化或产生了重复收藏。");
        }
        results["separateKeysStableOrderAndNotifications"] = true;
        results["toggleDoesNotNavigateOrRequest"] = true;
        results["historyRemainsIndependent"] = true;

        var refreshHandler = new FixtureHandler();
        using (var reopened = Create(store, refreshHandler))
        {
            Require(reopened.FavoriteForums.Count == 3 && refreshHandler.Paths.Count == 0, "重新打开没有从本机恢复收藏。");
            await reopened.InitializeAsync();
            Require(reopened.FavoriteForums.Select(f => f.Key).SequenceEqual(new[] { "t1", "f1", "f9" }), "目录刷新丢失收藏或改变顺序。");
            Require(reopened.FavoriteForums[0].Name == "新时间线" && reopened.FavoriteForums[0].MaxPage == 40 &&
                    reopened.FavoriteForums[1].Name == "新版块名称" && reopened.FavoriteForums[1].Notice == "新版规" &&
                    reopened.FavoriteForums[2].Name == "暂缺版块", "最新元信息没有同步，或目录暂缺项未保留快照。");
            Require(store.Load().FavoriteForums[1].Name == "新版块名称", "目录更新后的收藏快照没有保存。");
            await reopened.SelectForumAsync(reopened.FavoriteForums.Single(f => f.Key == "f1"));
            Require(refreshHandler.Paths.Last() == "/api/showf?id=1&page=1", "普通版块快捷入口请求了错误端点。");
            await reopened.SelectForumAsync(reopened.FavoriteForums.Single(f => f.Key == "t1"));
            await reopened.RefreshListAsync(); // 再访可使用短时缓存；显式刷新仍必须请求时间线端点。
            Require(refreshHandler.Paths.Last() == "/api/timeline?id=1&page=1", "时间线快捷入口请求了错误端点。");
        }
        results["restartAndLatestMetadata"] = true;
        results["missingDirectoryEntryPreserved"] = true;
        results["shortcutUsesCorrectEndpoint"] = true;

        var offlineHandler = new FixtureHandler { Offline = true };
        using (var offline = Create(store, offlineHandler))
        {
            await offline.InitializeAsync();
            Require(offline.FavoriteForums.Count == 3 && offline.FavoriteForumKeys.SetEquals(new[] { "t1", "f1", "f9" }),
                "接口失败使已收藏入口丢失。");
            var requests = offlineHandler.Paths.Count;
            offline.ToggleForumFavorite(offline.FavoriteForums.Single(f => f.Key == "f9"));
            Require(offline.FavoriteForums.Count == 2 && offlineHandler.Paths.Count == requests, "断网时无法纯本地取消收藏。");
        }
        using (var reopened = Create(store, new FixtureHandler { Offline = true }))
        {
            Require(reopened.FavoriteForumKeys.SetEquals(new[] { "t1", "f1" }), "取消收藏后重新打开仍有旧入口。");
            foreach (var forum in reopened.FavoriteForums.ToArray()) reopened.ToggleForumFavorite(forum);
            Require(!reopened.HasFavoriteForums && reopened.FavoriteForumKeys.Count == 0 && store.Load().FavoriteForums.Count == 0,
                "取消全部收藏后未恢复空状态。");
        }
        var snapshotsOnly = new LocalStore(Path.Combine(profiles, "snapshots-only"));
        snapshotsOnly.Save(new UserState { FavoriteForums = [timeline, missing] });
        using (var vm = Create(snapshotsOnly, new FixtureHandler { Offline = true }))
            Require(vm.ForumGroups.Count == 0 && vm.FavoriteForums.Select(f => f.Name).SequenceEqual(new[] { timeline.Name, missing.Name }),
                "没有缓存目录时不能仅凭快照恢复收藏。");
        results["offlineSnapshotsAndRemovalPersist"] = true;
        results["emptyStateAfterRemovingAll"] = true;
        results["fixedHttpHandlerOnly"] = true;

        File.WriteAllText(Path.Combine(output, "forum-favorites-state-results.json"),
            JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: forum favorites legacy state, separate f/t keys, persistence, metadata refresh, missing/offline snapshots, no navigation on toggle.");
    }

    private static MainViewModel Create(LocalStore store, FixtureHandler handler) => new(new XIslandApi(new HttpClient(handler)), store);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("版块收藏：" + message); }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Offline { get; init; }
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("版块收藏检查禁止提交发言。");
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            if (Offline) throw new HttpRequestException("离线测试夹具");
            var response = path switch
            {
                "/api/getForumList" => """[{"name":"综合","forums":[{"id":1,"name":"新版块名称","msg":"新版规"}]}]""",
                "/api/getTimelineList" => """[{"id":1,"name":"新时间线","max_page":40}]""",
                _ when path.StartsWith("/api/timeline?") || path.StartsWith("/api/showf?") => "[]",
                _ => throw new InvalidOperationException("未知的夹具请求：" + path)
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
}

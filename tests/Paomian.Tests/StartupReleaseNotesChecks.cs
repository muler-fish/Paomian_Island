using System.Net;
using System.Net.Http.Json;
using Paomian.Core;

internal static class StartupReleaseNotesChecks
{
    private static Version V(int patch) => new(1, 0, patch);
    private static ReleaseNote Note(int patch, string body) =>
        new(V(patch), body, new Uri(ReleaseUpdater.Repository + "/releases/tag/v" + V(patch)), null);

    internal static async Task RunAsync()
    {
        var upgrade = new PendingUpdateNotice(V(7), V(14));
        const string log = "## 1.0.15 · future\nfuture\n## 1.0.14 · current\n\n## 更新内容\n\n- 本版说明\n\n## 1.0.10 · previous\nprior";
        var bundled = StartupNotes.ReadBundled(new StringReader(log), V(14)) ?? throw new Exception("missing bundled note");
        Require(bundled.Body == "## 更新内容" + Environment.NewLine + Environment.NewLine + "- 本版说明", "exact embedded section");
        Require(StartupNotes.ReadBundled(new StringReader(log), V(9)) is null &&
            StartupNotes.ReadBundled(new StringReader("## 1.0.14\n \n## 1.0.13\nolder"), V(14)) is null, "missing or empty bundled section");
        var local = StartupNotes.FromLocal(upgrade, [bundled], []);
        Require(local is { IsComplete: false, NeedsOnline: true }, "manual cross-version upgrade needs history");
        var migration = new PendingUpdateNotice(null, V(14), V(14));
        var first = StartupNotes.FromLocal(migration, [Note(7, "older"), bundled], []);
        Require(first is { IsComplete: false, NeedsOnline: false } && first.Entries.Single() == bundled, "one-version legacy migration");
        var adjacent = StartupNotes.FromLocal(new(V(13), V(14)), [bundled], []);
        Require(adjacent is { IsComplete: true, NeedsOnline: false }, "local adjacent upgrade");

        object Row(string tag, string body, bool draft = false, bool pre = false) => new { tag_name = tag, body, draft, prerelease = pre };
        var rows = new[] { Row("v1.0.15", "future"), Row("v1.0.14", "正式说明"), Row("v1.0.9", "先前正式版"),
            Row("v1.0.7", "already installed"), Row("v1.0.12", "draft", draft: true), Row("v1.0.13", "preview", pre: true),
            Row("v1.0.10-rc.1", "preview") };
        using var complete = new ReleaseUpdater(new HttpClient(new Mock(_ => Page(rows))));
        var fetched = await complete.CompleteStartupNotesAsync(upgrade, local, default);
        Require(fetched is { IsComplete: true, NeedsOnline: false } && fetched.Entries.Select(n => n.Body)
            .SequenceEqual(["正式说明", "先前正式版"]), "official authority, exact stable range and ordering");
        var later = new PendingUpdateNotice(null, V(14), V(9));
        var migrated = await complete.CompleteStartupNotesAsync(later, StartupNotes.FromLocal(later, [bundled], []), default);
        Require(!migrated.IsComplete && migrated.Entries.Select(n => n.Version).SequenceEqual([V(14), V(9)]),
            "unacknowledged migration includes the first known version and no earlier releases");

        using var failedPage = new ReleaseUpdater(new HttpClient(new Mock(r =>
        {
            if (r.RequestUri!.Query.EndsWith("page=2", StringComparison.Ordinal)) return new(HttpStatusCode.ServiceUnavailable);
            var response = Page(new[] { Row("v1.0.11", "部分历史") });
            response.Headers.Add("Link", "<https://api.github.com/next>; rel=\"next\"");
            return response;
        })));
        var partial = await failedPage.CompleteStartupNotesAsync(upgrade, local, default);
        Require(!partial.IsComplete && !partial.NeedsOnline && partial.Entries.Select(n => n.Version).SequenceEqual([V(14), V(11)]),
            "late page failure preserves local and previously fetched notes");
        using var offline = new ReleaseUpdater(new HttpClient(new Mock(_ => throw new HttpRequestException("offline"))));
        Require((await offline.CompleteStartupNotesAsync(upgrade, local, default)).Entries.Single() == bundled, "offline fallback");
        using var missing = new ReleaseUpdater(new HttpClient(new Mock(_ => Page(new[] { Row("v1.0.9", "") }))));
        var unpublished = await missing.CompleteStartupNotesAsync(upgrade, local, default);
        Require(!unpublished.IsComplete && unpublished.Entries.Select(n => n.Version).SequenceEqual([V(14), V(9)]), "unpublished current and blank historical notes");
        using var emptyBody = new ReleaseUpdater(new HttpClient(new Mock(_ => Page(new[] { Row("v1.0.14", "  ") }))));
        var blank = await emptyBody.CompleteStartupNotesAsync(upgrade, local, default);
        Require(!blank.IsComplete && blank.Entries.Single() == bundled, "empty published note keeps bundled content and incomplete status");

        var profile = Directory.CreateTempSubdirectory("Paomian-UpgradeNotes-");
        try
        {
            var store = new LocalStore(profile.FullName);
            store.SaveUpgradeNotes(new(V(7), V(9), [Note(9, "old official")], true));
            store.SaveUpgradeNotes(new(V(9), V(14), [Note(14, "saved official")], true));
            store.SaveUpgradeNotes(new(V(9), V(14), [Note(14, "saved official")], false));
            var saved = StartupNotes.FromLocal(upgrade, [bundled], new LocalStore(profile.FullName).ReadUpgradeNotes());
            Require(saved is { IsComplete: true, NeedsOnline: false } && saved.Entries.Select(n => n.Body)
                .SequenceEqual(["saved official", "old official"]), "chained staged upgrades survive restart and partial snapshots");
        }
        finally { profile.Delete(true); }
        var mixed = StartupNotes.FromLocal(upgrade, [bundled], [new(V(7), V(14), [Note(14, ""), Note(9, "ok")], true)]);
        Require(mixed is { IsComplete: false, NeedsOnline: true } && mixed.Entries[0] == bundled, "empty cached official body");
        var unseen = StartupNotes.FromLocal(upgrade, [bundled], [new(V(10), V(14), [Note(14, "one")], true)]);
        Require(unseen.NeedsOnline, "an uncovered source interval needs backfill");
        Console.WriteLine("PASS startup release notes: embedded changelog, manual/in-app ranges, unpublished versions, missing pages/bodies, offline and persisted snapshots");
    }

    private static HttpResponseMessage Page(object rows) => new(HttpStatusCode.OK) { Content = JsonContent.Create(rows) };
    private static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private sealed class Mock(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}

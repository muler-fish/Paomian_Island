using System.Net;
using System.Net.Http.Json;
using Paomian.Core;

internal static class ReleaseNotesChecks
{
    private const string List = "https://api.github.com/repos/muler-fish/Paomian_Island/releases";
    internal static async Task RunAsync()
    {
        object Row(string tag, string? body = null, bool draft = false, bool pre = false, int day = 1, string? page = null) => new
        {
            tag_name = tag, body, draft, prerelease = pre, published_at = $"2026-11-{day:D2}T00:00:00Z", html_url = page ?? ReleaseUpdater.Repository + "/releases/tag/" + tag
        };
        var name = AppIdentity.PackageName("1.0.6");
        var latest = new { tag_name = "v1.0.6", body = "最新说明", draft = false, prerelease = false, published_at = "2026-11-02T00:00:00Z",
            assets = new[] { new { name, browser_download_url = $"{ReleaseUpdater.Repository}/releases/download/v1.0.6/{name}", digest = "sha256:" + new string('a', 64), size = 1234 } } };
        var first = Enumerable.Range(0, 24).Select(i => Row($"0.0.{i}")).Concat([Row("v1.0.6", "列表版本与检查时不同"), Row("1.0.5", "重复的旧副本"),
            Row("v1.0.4"), Row("v1.0.10"), Row("v1.0.5", "draft", draft: true, day: 28), Row("v1.0.5", "preview", pre: true, day: 29)]).ToArray();
        var second = new[] { Row("v1.0.5", null, day: 20, page: "file:///ignore"), Row("not-a-version"), Row("1.0.5-rc.1"), Row("1.0.3", "较早"), Row("v1.0.1"), Row("1.0.0"), new { name = "v1.0.2", commit = new { sha = "ordinary-tag" } } };
        var requests = new List<string>();
        using var updater = new ReleaseUpdater(new HttpClient(new Handler(request =>
        {
            var uri = request.RequestUri!.AbsoluteUri; requests.Add(uri);
            Require(request.Headers.Authorization is null && !request.Headers.Contains("Cookie"), "credentials isolation");
            if (uri == ReleaseUpdater.Endpoint) return Ok(latest);
            if (uri == List + "?per_page=30&page=2") return Ok(second);
            Require(uri == List + "?per_page=30&page=1", "unexpected endpoint: " + uri);
            var response = Ok(first);
            response.Headers.Add("Link", $"<{List}?per_page=30&page=2>; rel=\"next\", <{List}?per_page=30&page=2>; rel=\"last\"");
            return response;
        })));
        var target = await updater.CheckAsync("1.0.4") ?? throw new Exception("missing update");
        var notes = await updater.GetNotesAsync("1.0.4", target);
        Versions(notes, "1.0.6", "1.0.5");
        Require(notes[0].Body == target.Notes && notes[1].Body == "" && notes[1].PublishedAt?.Day == 20 &&
            notes[1].Page.AbsoluteUri == ReleaseUpdater.Repository + "/releases/tag/v1.0.5", "target snapshot, empty body, duplicate precedence and release page");
        Require(requests.Count == 3, "two list pages after latest");
        Versions(await updater.GetNotesAsync("v1.0.5", target), "1.0.6");
        Versions(await updater.GetNotesAsync("1.0.4-preview.2+build", target), "1.0.6", "1.0.5", "1.0.4");
        Versions(await updater.GetNotesAsync("1.0.0", target with { Version = "1.0.12" }), "1.0.12", "1.0.10", "1.0.6", "1.0.5", "1.0.4", "1.0.3", "1.0.1");
        var before = requests.Count;
        Require(await updater.CheckAsync("1.0.6") is null && (await updater.GetNotesAsync("1.0.6", target)).Count == 0 && requests.Count == before + 1, "up-to-date must not request history");
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable })
        {
            using var fail = new ReleaseUpdater(new HttpClient(new Handler(r => r.RequestUri!.AbsoluteUri == ReleaseUpdater.Endpoint ? Ok(latest) : new(status))));
            var found = await fail.CheckAsync("1.0.4") ?? throw new Exception("lost release");
            try { await fail.GetNotesAsync("1.0.4", found); throw new Exception("expected history failure"); }
            catch (HttpRequestException ex) when (ex.StatusCode == status) { }
            Require(found.Download == target.Download && found.Sha256 == target.Sha256, "notes failure changed asset");
        }
        using var slow = new ReleaseUpdater(new HttpClient(new Handler(_ => throw new TaskCanceledException("simulated timeout"))));
        try { await slow.GetNotesAsync("1.0.4", target); throw new Exception("expected timeout"); } catch (OperationCanceledException) { }
        Console.WriteLine("PASS release notes: target snapshot, ordered ranges, multiple pages, stable/preview, duplicates, empty body, missing tags, limit/offline/timeout isolation");
    }

    private static void Versions(IReadOnlyList<ReleaseNote> notes, params string[] expected) =>
        Require(notes.Select(n => n.Version.ToString()).SequenceEqual(expected), "unexpected versions: " + string.Join(", ", notes.Select(n => n.Version)));
    private static HttpResponseMessage Ok(object data) => new(HttpStatusCode.OK) { Content = JsonContent.Create(data) };
    private static void Require(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handle(request)); }
}

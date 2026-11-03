using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tide.Core;

internal static class SettingsServicesChecks
{
    internal static async Task RunAsync()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "preview7-services", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(root);
        var store = new LocalStore(Path.Combine(root, "profile")); var state = new UserState { CacheLimitMb = 1 };
        store.Save(state); var stateBytes = File.ReadAllBytes(Path.Combine(store.Root, "state.json")); store.ConfigureCache(state);
        var key1 = "images/" + new string('A', 64) + ".bin"; var key2 = "images/" + new string('B', 64) + ".bin";
        store.Cache.Write(key1, new byte[600_000]); await Task.Delay(20); store.Cache.Write(key2, new byte[600_000]);
        Require(store.Cache.SizeBytes() <= 1_000_000 && store.Cache.Read(key1) is null && store.Cache.Read(key2)?.Length == 600_000, "capacity eviction");
        var page = new ThreadPage(new Post { Id = 42, Content = "正文" }, [], 1);
        store.SavePage(page, "identity"); Require(store.ReadPage(42, 1, "identity") is not null && store.ReadPage(42, 1, "other") is null, "page identity isolation");
        var snapshot = new SubscriptionSnapshot([page.Original], true, DateTimeOffset.UtcNow, []);
        store.SaveSubscriptions("中文订阅号", "identity", snapshot);
        Require(store.ReadSubscriptions("中文订阅号", "identity")?.Posts.Single().Id == 42 && store.ReadSubscriptions("中文订阅号", "other") is null, "subscription snapshot isolation");
        var legacy = Path.Combine(store.Root, "subscription-cache"); Directory.CreateDirectory(legacy);
        var legacyKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { "旧订阅号", "identity" }))));
        File.WriteAllText(Path.Combine(legacy, legacyKey + ".json"), JsonSerializer.Serialize(snapshot)); store.ConfigureCache(state);
        Require(store.ReadSubscriptions("旧订阅号", "identity")?.Complete == true && !Directory.EnumerateFiles(legacy).Any(), "legacy subscription cache migration");
        var before = store.Cache.Root; var alternate = Path.Combine(root, "alternate");
        store.Cache.Configure(alternate, 1);
        Require(store.ReadPage(42, 1, "identity") is not null && !Directory.EnumerateFiles(before, "*.json", SearchOption.AllDirectories).Any(), "cache move");
        store.Cache.Clear(); Require(store.Cache.SizeBytes() == 0 && stateBytes.SequenceEqual(File.ReadAllBytes(Path.Combine(store.Root, "state.json"))), "clear preserves records");
        store.Cache.Configure(alternate, 0); store.Cache.Write(key1, [1]); Require(store.Cache.SizeBytes() == 0, "disabled disk cache");
        Throws(() => store.Cache.Write("../state.json", [1]), "cache traversal");
        Console.WriteLine("PASS cache capacity, identity, migration, clear and personal data preservation");

        var proxy = true; var visited = new List<string>();
        using (var http = new HttpClient(new ProxyRouteHandler(() => proxy,
            new Handler(r => { visited.Add("proxy"); return Ok("{}"); }), new Handler(r => { visited.Add("direct"); return Ok("{}"); }))))
        { await http.GetAsync("https://example.com"); proxy = false; await http.GetAsync("https://example.com"); }
        Require(visited.SequenceEqual(new[] { "proxy", "direct" }), "live proxy switch");

        var zip = Path.Combine(root, "release.zip");
        var bytes = Encoding.UTF8.GetBytes("new executable fixture"); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (var stream = archive.CreateEntry("Tide.exe").Open()) stream.Write(bytes);
            using (var writer = new StreamWriter(archive.CreateEntry("SHA256SUMS.txt").Open())) writer.Write(hash + "  Tide.exe\n");
            using (var writer = new StreamWriter(archive.CreateEntry("licenses/NOTICE.txt").Open())) writer.Write("license fixture");
            using (var writer = new StreamWriter(archive.CreateEntry("licenses/MiSans-LICENSE.pdf").Open())) writer.Write("license fixture PDF");
        }
        var zipBytes = File.ReadAllBytes(zip); var zipHash = ReleaseUpdater.Hash(zip);
        byte[] Metadata(string version = "9.0.0") => JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "v" + version, draft = false, prerelease = false, body = "release notes", assets = new[] { new { name = $"Tide-{version}-win-x64.zip", browser_download_url = $"https://github.com/muler-fish/Tide/releases/download/v{version}/Tide-{version}-win-x64.zip", digest = "sha256:" + zipHash, size = zipBytes.Length } } });
        Require(!ReleaseUpdater.IsNewer("0.2.1", "0.2.2-preview.7") && ReleaseUpdater.IsNewer("0.2.2", "0.2.2-preview.7") && !ReleaseUpdater.IsNewer("0.2.2", "0.2.2"), "stable/preview version comparison");
        var requests = new List<string>();
        using var updater = new ReleaseUpdater(new HttpClient(new Handler(r =>
        {
            Require(!r.Headers.Contains("Cookie") && r.Headers.Authorization is null, "update credentials isolation"); requests.Add(r.RequestUri!.Host);
            if (r.RequestUri!.Host == "api.github.com") return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Metadata()) };
            if (r.RequestUri!.Host == "github.com") return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("https://release-assets.githubusercontent.com/fixture.zip") } };
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
        })));
        var release = await updater.CheckAsync("0.2.2-preview.7") ?? throw new Exception("release absent");
        var staged = await updater.DownloadAsync(release, Path.Combine(store.Root, "updates"));
        Require(staged.ExecutableHash == hash && requests.SequenceEqual(new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com" }), "download redirect and hashes");
        var installDir = Path.Combine(root, "installed"); Directory.CreateDirectory(installDir);
        var target = Path.Combine(installDir, "Tide.exe"); File.WriteAllText(target, "old executable fixture");
        File.WriteAllText(Path.Combine(installDir, "personal.txt"), "preserve");
        var planPath = UpdateInstaller.Prepare(staged, store.Root, target, false); var plan = UpdateInstaller.ReadPlan(planPath);
        UpdateInstaller.Apply(planPath, plan);
        Require(ReleaseUpdater.Hash(target) == hash && File.ReadAllText(Path.Combine(installDir, "personal.txt")) == "preserve", "install preserves unrelated files");
        // A locked executable forces rollback after the license/checksum files have been replaced.
        var rollback = Path.Combine(root, "rollback"); Directory.CreateDirectory(rollback); var locked = Path.Combine(rollback, "Tide.exe"); File.WriteAllText(locked, "old locked");
        File.WriteAllText(Path.Combine(rollback, "SHA256SUMS.txt"), "old checksum");
        var staged2 = await updater.DownloadAsync(release, Path.Combine(store.Root, "updates"));
        var rollbackPlan = UpdateInstaller.Prepare(staged2, store.Root, locked, false);
        using (var hold = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            Throws(() => UpdateInstaller.Apply(rollbackPlan, UpdateInstaller.ReadPlan(rollbackPlan)), "locked executable");
        Require(File.ReadAllText(locked) == "old locked" && File.ReadAllText(Path.Combine(rollback, "SHA256SUMS.txt")) == "old checksum" && !File.Exists(Path.Combine(rollback, "licenses", "NOTICE.txt")), "transaction rollback");
        await ThrowsAsync(() => updater.DownloadAsync(release with { Sha256 = new string('0', 64) }, Path.Combine(store.Root, "updates")), "corrupt download");
        var evil = Path.Combine(root, "evil.zip"); using (var archive = ZipFile.Open(evil, ZipArchiveMode.Create)) { using var writer = new StreamWriter(archive.CreateEntry("../outside.txt").Open()); writer.Write("bad"); }
        Throws(() => ReleaseUpdater.ExtractVerified(evil, Path.Combine(root, "evil-extract")), "zip traversal");
        Require(!File.Exists(Path.Combine(root, "outside.txt")), "zip escaped");
        Console.WriteLine("PASS independent routing; release versions; bounded download; SHA-256; ZIP paths; install and rollback");
    }
    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Throws(Action action, string message) { try { action(); } catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException) { return; } throw new Exception("Expected rejection: " + message); }
    private static async Task ThrowsAsync(Func<Task> action, string message) { try { await action(); } catch (Exception ex) when (ex is IOException or InvalidDataException) { return; } throw new Exception("Expected rejection: " + message); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handle(request)); }
}

using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tide.Core;

public sealed record AppRelease(string Version, string Notes, Uri Page, Uri Download, string Sha256, long Size);
public sealed record StagedUpdate(string Directory, string ExecutableHash);

/// <summary>Stable releases from the project's own repository, downloaded without island credentials.</summary>
public sealed class ReleaseUpdater : IDisposable
{
    public const string Repository = "https://github.com/muler-fish/Tide";
    public const string Endpoint = "https://api.github.com/repos/muler-fish/Tide/releases/latest";
    private readonly HttpClient _http;
    public ReleaseUpdater(HttpClient? http = null)
    {
        _http = http ?? NetworkClients.Create(updates: true);
        _http.Timeout = TimeSpan.FromMinutes(10);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Tide-Updater/0.2");
    }
    public async Task<AppRelease?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var output = new MemoryStream();
        await CopyLimitedAsync(await response.Content.ReadAsStreamAsync(timeout.Token), output, 2_000_000, null, timeout.Token);
        return ParseRelease(output.ToArray(), currentVersion);
    }
    public static AppRelease? ParseRelease(byte[] json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$")) throw new InvalidDataException("更新版本号格式不支持。");
        if (!IsNewer(tag, currentVersion)) return null;
        var version = tag.TrimStart('v');
        var name = AppIdentity.PackageName(version);
        var assets = root.GetProperty("assets");
        var asset = assets.EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        if (asset.ValueKind == JsonValueKind.Undefined)
        {
            name = AppIdentity.LegacyPackageName(version);
            asset = assets.EnumerateArray().FirstOrDefault(a => a.GetProperty("name").GetString() == name);
        }
        if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("新版本尚未提供 Windows x64 更新包。");
        var url = new Uri(asset.GetProperty("browser_download_url").GetString()!);
        var expected = new Uri($"{Repository}/releases/download/{tag}/{Uri.EscapeDataString(name)}");
        if (!string.Equals(url.AbsoluteUri, expected.AbsoluteUri, StringComparison.Ordinal)) throw new InvalidDataException("更新包不属于本项目。");
        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$")) throw new InvalidDataException("更新包缺少 SHA-256 校验信息，请在仓库手动下载。");
        var size = asset.GetProperty("size").GetInt64();
        if (size is <= 0 or > 100_000_000) throw new InvalidDataException("更新包大小异常。");
        return new(version, root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "", new Uri($"{Repository}/releases/tag/{tag}"), url, digest[7..], size);
    }
    public static bool IsNewer(string release, string current)
    {
        var parsed = Version.Parse(release.TrimStart('v'));
        var clean = current.Split('+')[0].TrimStart('v'); var pieces = clean.Split('-', 2);
        if (!Version.TryParse(pieces[0], out var installed)) throw new InvalidDataException("当前版本号无法识别。");
        return parsed > installed || parsed == installed && pieces.Length == 2;
    }
    public async Task<StagedUpdate> DownloadAsync(AppRelease release, string updatesRoot, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var stage = Path.Combine(Path.GetFullPath(updatesRoot), Guid.NewGuid().ToString("N"));
        DiskCache.NoLinks(stage); Directory.CreateDirectory(stage);
        var zip = Path.Combine(stage, "package.zip");
        try
        {
            var url = release.Download;
            for (var redirects = 0; ; redirects++)
            {
                if (url.Scheme != "https" || !(url.Host == "github.com" || url.Host == "release-assets.githubusercontent.com" || url.Host == "objects.githubusercontent.com"))
                    throw new InvalidDataException("更新下载跳转到了不支持的地址。");
                using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    if (redirects >= 4 || response.Headers.Location is null) throw new InvalidDataException("更新下载跳转异常。");
                    url = new Uri(url, response.Headers.Location); continue;
                }
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } length && length != release.Size) throw new InvalidDataException("更新包大小不匹配。");
                await using (var file = File.Create(zip))
                    await CopyLimitedAsync(await response.Content.ReadAsStreamAsync(ct), file, release.Size, progress, ct);
                break;
            }
            if (new FileInfo(zip).Length != release.Size || !string.Equals(Hash(zip), release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新包校验失败，请重新下载。");
            var package = Path.Combine(stage, "package");
            var hash = await Task.Run(() => ExtractVerified(zip, package), ct);
            File.Delete(zip); progress?.Report(100); return new(package, hash);
        }
        catch { DeleteStage(stage, updatesRoot); throw; }
    }
    public static string ExtractVerified(string zip, string destination)
    {
        DiskCache.NoLinks(destination);
        if (Directory.Exists(destination)) throw new IOException("解压目录已存在。");
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(zip); long total = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) { if (entry.FullName != "licenses/") throw new InvalidDataException("更新包目录无效。"); continue; }
            if (!AllowedFile(entry.FullName) || !names.Add(entry.FullName) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException("更新包含有不支持的文件。");
            total += entry.Length; if (total > 100_000_000 || archive.Entries.Count > 100) throw new InvalidDataException("更新解压后超过大小限制。");
            var target = Path.Combine(destination, entry.FullName); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = new FileStream(target, FileMode.CreateNew);
            CopyLimitedAsync(entry.Open(), output, entry.Length, null, CancellationToken.None).GetAwaiter().GetResult();
            if (output.Length != entry.Length) throw new InvalidDataException("更新文件大小不匹配。");
        }
        if (!names.Contains(AppIdentity.ExecutableName) || !names.Contains("SHA256SUMS.txt")) throw new InvalidDataException("更新包不完整。");
        var exeHash = Hash(Path.Combine(destination, AppIdentity.ExecutableName));
        var manifest = File.ReadAllText(Path.Combine(destination, "SHA256SUMS.txt"));
        if (!Regex.IsMatch(manifest, @"(?im)^" + exeHash + @"\s+\*?Tide\.exe\s*$")) throw new InvalidDataException("可执行文件校验失败。");
        return exeHash;
    }
    public static bool AllowedFile(string path) => path is AppIdentity.ExecutableName or "SHA256SUMS.txt" || Regex.IsMatch(path, @"^licenses/[a-zA-Z0-9_.-]+\.(?:txt|md|pdf)$");
    public static string Hash(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
    private static async Task CopyLimitedAsync(Stream source, Stream target, long limit, IProgress<int>? progress, CancellationToken ct)
    {
        using (source)
        {
            var buffer = new byte[65536]; long read = 0; int count;
            while ((count = await source.ReadAsync(buffer, ct)) > 0)
            { read += count; if (read > limit) throw new InvalidDataException("响应超过大小限制。"); await target.WriteAsync(buffer.AsMemory(0, count), ct); progress?.Report((int)(read * 99 / limit)); }
        }
    }
    public static void DeleteStage(string stage, string root)
    {
        var full = Path.GetFullPath(stage); var parent = Path.GetDirectoryName(full);
        if (!string.Equals(parent, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(Path.GetFileName(full), "^[a-f0-9]{32}$")) throw new IOException("无效的更新暂存目录。");
        DiskCache.NoLinks(full);
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
    public void Dispose() => _http.Dispose();
}

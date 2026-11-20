using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Paomian.Core;

public sealed record ReleaseNote(Version Version, string Body, Uri Page, DateTimeOffset? PublishedAt);

public sealed partial class ReleaseUpdater
{
    private sealed record MetadataPage(byte[] Json, bool HasNext);

    private async Task<MetadataPage?> ReadMetadataAsync(string url, CancellationToken ct, bool allowMissing = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (allowMissing && response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var output = new MemoryStream();
        await CopyLimitedAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), output, 2_000_000, null, ct).ConfigureAwait(false);
        var next = response.Headers.TryGetValues("Link", out var links) && links.Any(link =>
            Regex.IsMatch(link, "<[^>]+>\\s*;\\s*rel=\"next\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        return new(output.ToArray(), next);
    }

    private static bool IsPublished(JsonElement row) => !row.GetProperty("draft").GetBoolean() && !row.GetProperty("prerelease").GetBoolean();
    private static Version? StableVersion(string tag) => Regex.IsMatch(tag, @"^v?[0-9]+\.[0-9]+\.[0-9]+\z") &&
        Version.TryParse(tag.TrimStart('v'), out var parsed) ? parsed : null;

    private static ReleaseNote ReadNote(JsonElement row, string tag, Version version)
    {
        var path = "/releases/tag/" + tag;
        var page = row.TryGetProperty("html_url", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
        if (page != Repository + path && page != LegacyRepository + path) page = Repository + path;
        DateTimeOffset? published = row.TryGetProperty("published_at", out var date) && date.ValueKind == JsonValueKind.String &&
            date.TryGetDateTimeOffset(out var value) ? value : null;
        var text = row.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null;
        return new(version, text ?? "", new Uri(page), published);
    }

    public async Task<IReadOnlyList<ReleaseNote>> GetNotesAsync(string installed, AppRelease target, CancellationToken ct = default)
    {
        var latest = target.Note;
        if (!IsNewer(latest.Version, installed)) return [];
        var notes = new Dictionary<Version, ReleaseNote> { [latest.Version] = latest };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        // 发布时间不保证与版本号同序；看完整个分页链，不在碰到旧版本时提前停止。
        for (var page = 1; page <= 64; page++)
        {
            var result = (await ReadMetadataAsync($"{ReleasesEndpoint}?per_page=30&page={page}", timeout.Token).ConfigureAwait(false))!;
            using var doc = JsonDocument.Parse(result.Json);
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("tag_name", out var label) ||
                    label.ValueKind != JsonValueKind.String || !IsPublished(row)) continue;
                var tag = label.GetString()!;
                if (StableVersion(tag) is not { } version || version >= latest.Version || !IsNewer(version, installed)) continue;
                var note = ReadNote(row, tag, version);
                if (!notes.TryGetValue(version, out var existing) || Nullable.Compare(note.PublishedAt, existing.PublishedAt) > 0) notes[version] = note;
            }
            if (!result.HasNext) return notes.Values.OrderByDescending(n => n.Version).ToArray();
        }
        throw new InvalidDataException("更新记录过多，请在 GitHub 查看完整记录。");
    }
}

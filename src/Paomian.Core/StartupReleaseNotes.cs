using System.Text;
using System.Text.Json;

namespace Paomian.Core;

public sealed record UpgradeNotesSnapshot(Version Source, Version Target, IReadOnlyList<ReleaseNote> Entries, bool Complete);

public sealed record StartupNotes(IReadOnlyList<ReleaseNote> Entries, bool IsComplete, bool NeedsOnline)
{
    // CHANGELOG 既记录正式版也记录本地版；只按实际运行过的版本取内置说明。
    public static ReleaseNote? ReadBundled(TextReader changelog, Version version)
    {
        var body = new StringBuilder();
        var found = false;
        while (changelog.ReadLine() is { } line)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) &&
                ReleaseUpdater.StableVersion(line[3..].Trim().Split(' ', 2)[0]) is { } sectionVersion)
            {
                if (found) break;
                found = sectionVersion == version;
            }
            else if (found) body.AppendLine(line);
        }
        var text = body.ToString().Trim();
        return text.Length == 0 ? null : new(version, text, new Uri(ReleaseUpdater.Repository + "/releases"), null);
    }

    public static StartupNotes FromLocal(PendingUpdateNotice notice, IEnumerable<ReleaseNote> bundled,
        IReadOnlyList<UpgradeNotesSnapshot> snapshots)
    {
        var notes = new Dictionary<Version, ReleaseNote>();
        foreach (var note in (notice.BundledNotes ?? []).Concat(bundled).Where(n => notice.Includes(n.Version)))
            PreferAvailableBody(notes, note);
        var published = new Dictionary<Version, ReleaseNote>();
        foreach (var entry in snapshots.SelectMany(s => s.Entries).Where(n => notice.Includes(n.Version)))
            PreferAvailableBody(published, entry);
        foreach (var entry in published.Values) PreferAvailableBody(notes, entry);

        var lower = notice.Source ?? notice.FirstKnownVersion ?? notice.Target;
        var covered = lower;
        foreach (var snapshot in snapshots.Where(s => s.Complete).OrderBy(s => s.Source))
            if (snapshot.Source <= covered && snapshot.Target > covered) covered = snapshot.Target;

        // 三段数字版本中相邻的补丁号之间没有其他正式版，可直接显示内置说明。
        var adjacent = lower.Major == notice.Target.Major && lower.Minor == notice.Target.Minor &&
            (long)notice.Target.Build - lower.Build == 1;
        var knownBodies = notes.ContainsKey(notice.Target) && (notice.Source is not null || notes.ContainsKey(lower)) &&
            notes.Values.All(n => !string.IsNullOrWhiteSpace(n.Body));
        var missingPublishedBody = published.Values.Any(n => string.IsNullOrWhiteSpace(n.Body));
        var localRangeReady = knownBodies && !missingPublishedBody && (covered >= notice.Target || adjacent);
        return new(Sorted(notes), notice.Source is not null && localRangeReady, !localRangeReady);
    }

    internal static void PreferAvailableBody(Dictionary<Version, ReleaseNote> notes, ReleaseNote entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Body) || !notes.ContainsKey(entry.Version))
            notes[entry.Version] = entry;
    }

    internal static ReleaseNote[] Sorted(Dictionary<Version, ReleaseNote> notes) =>
        notes.Values.OrderByDescending(n => n.Version).ToArray();
}

public sealed partial class ReleaseUpdater
{
    public async Task<StartupNotes> CompleteStartupNotesAsync(PendingUpdateNotice notice, StartupNotes local, CancellationToken ct)
    {
        if (!local.NeedsOnline) return local;
        var online = new Dictionary<Version, ReleaseNote>();
        var allPages = false;
        try
        {
            await ReadNotesIntoAsync(online, notice.Includes, ct).ConfigureAwait(false);
            allPages = true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested &&
            ex is HttpRequestException or IOException or JsonException or InvalidOperationException or
                KeyNotFoundException or InvalidDataException or OperationCanceledException)
        {
            // 已取得的分页及本地说明仍然可读。
        }
        ct.ThrowIfCancellationRequested();
        var merged = local.Entries.ToDictionary(n => n.Version);
        foreach (var entry in online.Values) StartupNotes.PreferAvailableBody(merged, entry);
        var complete = allPages && notice.Source is not null && online.ContainsKey(notice.Target) &&
            online.Values.All(n => !string.IsNullOrWhiteSpace(n.Body)) &&
            merged.Values.All(n => !string.IsNullOrWhiteSpace(n.Body));
        return new(StartupNotes.Sorted(merged), complete, false);
    }
}

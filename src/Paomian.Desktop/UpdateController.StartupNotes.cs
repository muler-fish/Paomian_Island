using System.IO;
using Paomian.Core;

namespace Paomian.Desktop;

internal sealed partial class UpdateController
{
    internal static ReleaseNote? BundledNote(Version version)
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("Paomian.Changelog.md");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return StartupNotes.ReadBundled(reader, version);
    }

    internal StartupNotes LocalStartupNotes(PendingUpdateNotice notice)
    {
        var bundled = new List<ReleaseNote>();
        if (notice.FirstKnownVersion is { } first && first != notice.Target &&
            notice.BundledNotes?.Any(n => n.Version == first) != true && BundledNote(first) is { } earlier)
            bundled.Add(earlier);
        return StartupNotes.FromLocal(notice, bundled, vm.Store.ReadUpgradeNotes());
    }

    internal Task<StartupNotes> CompleteStartupNotesAsync(PendingUpdateNotice notice, StartupNotes local, CancellationToken ct) =>
        updater.CompleteStartupNotesAsync(notice, local, ct);

    private void SaveStagedNotes()
    {
        if (Staged is null || Available is not { } release || Notes is not { } notes ||
            !System.Version.TryParse(InstalledVersion, out var source)) return;
        try
        {
            vm.Store.SaveUpgradeNotes(new(source, release.Note.Version, notes.Entries, notes.Phase == NotesPhase.Ready));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { /* 说明快照写入失败时，更新包仍可安装，新版可读取内置说明并补取历史。 */ }
    }
}

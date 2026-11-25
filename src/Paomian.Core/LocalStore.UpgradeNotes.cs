using System.Text.Json;

namespace Paomian.Core;

public sealed partial class LocalStore
{
    private string UpgradeNotesPath => Path.Combine(Root, "upgrade-notes.json");

    public IReadOnlyList<UpgradeNotesSnapshot> ReadUpgradeNotes()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(UpgradeNotesPath)) return [];
                var saved = JsonSerializer.Deserialize<UpgradeNotesSnapshot[]>(File.ReadAllText(UpgradeNotesPath));
                return (saved ?? []).Where(s => s is { Source: not null, Target: not null, Entries: not null } &&
                    s.Source < s.Target && s.Entries.All(n => n is { Version: not null, Body: not null, Page.IsAbsoluteUri: true } &&
                    n.Version > s.Source && n.Version <= s.Target && n.Page.Scheme == "https" && n.Page.Host == "github.com")).ToArray();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            { return []; }
        }
    }

    public void SaveUpgradeNotes(UpgradeNotesSnapshot snapshot)
    {
        lock (_gate)
        {
            var saved = ReadUpgradeNotes().ToList();
            var previous = saved.Find(s => s.Source == snapshot.Source && s.Target == snapshot.Target);
            if (previous is not null)
            {
                saved.Remove(previous);
                if (!snapshot.Complete)
                {
                    var entries = previous.Entries.ToDictionary(n => n.Version);
                    foreach (var note in snapshot.Entries) StartupNotes.PreferAvailableBody(entries, note);
                    snapshot = snapshot with { Entries = StartupNotes.Sorted(entries), Complete = previous.Complete };
                }
            }
            saved.Add(snapshot);
            WriteJsonAtomically(UpgradeNotesPath, saved);
        }
    }

    internal void DiscardUpgradeNotesThrough(Version version)
    {
        lock (_gate)
        {
            try
            {
                var snapshots = ReadUpgradeNotes();
                var remaining = snapshots.Where(s => s.Target > version).ToArray();
                if (remaining.Length == snapshots.Count) return;
                if (remaining.Length == 0) File.Delete(UpgradeNotesPath);
                else WriteJsonAtomically(UpgradeNotesPath, remaining);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { /* 确认状态已保存，旧快照在下次读取时仍按版本过滤。 */ }
        }
    }
}

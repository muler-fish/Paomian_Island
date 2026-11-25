using System.Text.Json;

namespace Paomian.Core;

// 已知来源时起点不包含在范围内。旧资料只知道本次运行版本时，从这个版本起（含）记录。
public sealed record PendingUpdateNotice(Version? Source, Version Target, Version? FirstKnownVersion = null)
{
    public IReadOnlyList<ReleaseNote>? BundledNotes { get; init; }

    public bool Includes(Version version) => version <= Target &&
        (Source is { } source ? version > source : version >= (FirstKnownVersion ?? Target));
}

public sealed partial class LocalStore
{
    private sealed record NoticeState(Version HighestRun, Version? LastAcknowledged, PendingUpdateNotice? Pending);
    private readonly bool _existingProfile;
    // 旧备份的哈希校验不应占用普通设置、草稿和发言记录的写锁。
    private readonly object _noticeGate = new();
    private string NoticePath => Path.Combine(_root, "update-notice.json");

    // 在视图模型创建缓存标记前登记全新资料。已有更新暂存时留给后台核验安装计划。
    public void InitializeNewProfileVersion(string installedVersion)
    {
        if (_existingProfile || File.Exists(NoticePath) || Directory.Exists(Path.Combine(_root, "updates"))) return;
        try { BeginUpdateNotice(installedVersion); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { LoadWarning = "首次运行版本未能保存：" + ex.Message; }
    }

    public PendingUpdateNotice? BeginUpdateNotice(string installedVersion, string? executablePath = null,
        Func<Version, ReleaseNote?>? readBundled = null)
    {
        var current = ReleaseUpdater.StableVersion(installedVersion.Split('+')[0])
            ?? throw new InvalidDataException("无法识别当前版本。");
        lock (_noticeGate)
        {
            var saved = ReadNoticeState();
            var state = saved;
            if (state is null)
            {
                var source = UpdateInstaller.FindPreviousVersion(_root, executablePath, current);
                var first = source is not null ? new PendingUpdateNotice(source, current)
                    : _existingProfile ? new PendingUpdateNotice(null, current, current) : null;
                state = new NoticeState(current, null, first);
            }
            else if (current < state.HighestRun) return null;
            else if (current > state.HighestRun)
            {
                var pending = state.Pending is { } previous ? previous with { Target = current }
                    : new PendingUpdateNotice(state.HighestRun, current);
                state = state with { HighestRun = current, Pending = pending };
            }
            // 本地开发版可能没有 GitHub Release，把本次实际运行版本的说明与待确认范围一起保存。
            if (state.Pending is { } notice && notice.BundledNotes?.Any(n => n.Version == current) != true &&
                readBundled?.Invoke(current) is { } note && note.Version == current && !string.IsNullOrWhiteSpace(note.Body))
                state = state with { Pending = notice with { BundledNotes = [.. notice.BundledNotes ?? [], note] } };
            if (state != saved) WriteJsonAtomically(NoticePath, state);
            return state.Pending;
        }
    }

    public void AcknowledgeUpdateNotice(Version target)
    {
        lock (_noticeGate)
        {
            var state = ReadNoticeState();
            if (state?.Pending?.Target != target) return;
            WriteJsonAtomically(NoticePath, state with { LastAcknowledged = target, Pending = null });
            DiscardUpgradeNotesThrough(target);
        }
    }

    private NoticeState? ReadNoticeState()
    {
        try
        {
            var state = JsonSerializer.Deserialize<NoticeState>(File.ReadAllText(NoticePath), Options);
            if (state?.HighestRun is null || state.LastAcknowledged > state.HighestRun ||
                state.Pending is { } pending && (pending.Target != state.HighestRun || state.LastAcknowledged >= pending.Target ||
                    (pending.Source is null) == (pending.FirstKnownVersion is null) ||
                    pending.Source >= pending.Target || pending.FirstKnownVersion > pending.Target ||
                    pending.BundledNotes?.Any(n => n is not { Version: not null } || !pending.Includes(n.Version) ||
                        string.IsNullOrWhiteSpace(n.Body)) == true))
                throw new JsonException("通知范围无效。");
            return state;
        }
        catch (FileNotFoundException) { return null; }
        catch (JsonException ex) { throw new IOException("更新通知记录无法读取，原文件已保留。", ex); }
    }
}

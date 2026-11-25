using Paomian.Core;

namespace Paomian.Desktop;

internal enum NotesPhase { NotLoaded, Loading, Ready, Failed }
internal sealed record UpdateNotes(NotesPhase Phase, IReadOnlyList<ReleaseNote> Entries);

internal sealed partial class UpdateController
{
    internal event Action? ReleaseChecked, NotesChanged;
    internal UpdateNotes? Notes { get; private set; }
    private ReleaseNote? _notesTarget;
    private CancellationTokenSource? _notesRequest;
    private DateTimeOffset _notesValidUntil;

    private void OnReleaseChecked(AppRelease? release)
    {
        var target = release?.Note;
        if (target != _notesTarget)
        {
            _notesRequest?.Cancel();
            _notesRequest = null;
            _notesTarget = target;
            _notesValidUntil = default;
            Notes = target is null ? null : new(NotesPhase.NotLoaded, [target]);
        }
        ReleaseChecked?.Invoke();
    }

    internal async Task LoadNotesAsync(bool retry = false)
    {
        if (Available is not { } target || Notes is null || _notesRequest is not null || _lifetime.IsCancellationRequested ||
            !retry && DateTimeOffset.UtcNow < _notesValidUntil) return;

        // 请求属于更新器，不属于窗口；窗口关闭后结果仍可供下次打开复用。
        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _notesRequest = request;
        Notes = Notes with { Phase = NotesPhase.Loading };
        NotesChanged?.Invoke();
        try
        {
            var entries = await updater.GetNotesAsync(InstalledVersion, target, request.Token);
            if (request.IsCancellationRequested) return;
            Notes = new(NotesPhase.Ready, entries);
            SaveStagedNotes();
            _notesValidUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        }
        catch (Exception)
        {
            if (request.IsCancellationRequested) return;
            Notes = Notes with { Phase = NotesPhase.Failed };
            _notesValidUntil = DateTimeOffset.UtcNow.AddMinutes(1);
        }
        finally
        {
            if (ReferenceEquals(_notesRequest, request))
            {
                _notesRequest = null;
                if (!request.IsCancellationRequested) NotesChanged?.Invoke();
            }
        }
    }
}

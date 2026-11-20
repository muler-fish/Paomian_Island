using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Paomian.Core;

namespace Paomian.Desktop;

internal sealed class SettingsReleaseNotes : StackPanel
{
    private readonly Action<string> _open;
    private readonly Func<Task> _retry;
    private readonly StackPanel _versions = new(), _feedback = new();
    private DispatcherOperation? _batch;
    private int _rendered;
    private UpdateNotes? _shown;

    internal SettingsReleaseNotes(Action<string> open, Func<Task> retry)
    {
        _open = open; _retry = retry;
        Visibility = Visibility.Collapsed;
        Margin = new(0, 12, 0, 8);
        AutomationProperties.SetName(this, "更新内容");
        var heading = SettingsLayout.Text("更新内容"); heading.FontWeight = FontWeights.SemiBold; Children.Add(heading);
        Children.Add(_feedback); Children.Add(_versions);
        IsVisibleChanged += (_, _) => { if (IsVisible) FillVersions(); else { _batch?.Abort(); _batch = null; } };
    }

    internal void Display(UpdateNotes? notes)
    {
        if (ReferenceEquals(notes, _shown)) return;
        var old = _shown; _shown = notes; _feedback.Children.Clear();
        // 列表补齐时保留已经显示的目标版本及链接焦点。
        if (notes is null || old is null || !ReferenceEquals(notes.Entries, old.Entries))
        {
            _batch?.Abort(); _batch = null;
            var keepFirst = old is not null && notes is not null && _rendered == 1 && old.Entries[0] == notes.Entries[0];
            if (!keepFirst) { _versions.Children.Clear(); _rendered = 0; }
        }
        Visibility = notes is null ? Visibility.Collapsed : Visibility.Visible;
        if (notes is null) return;
        FillVersions();
        if (notes.Phase is not (NotesPhase.Loading or NotesPhase.Failed)) return;
        var status = SettingsLayout.Text(notes.Phase == NotesPhase.Failed ? "暂时无法获取更新内容" : "正在获取更新内容…", true);
        status.Margin = new(0, 4, 0, 0); _feedback.Children.Add(status);
        if (notes.Phase == NotesPhase.Failed)
        {
            var actions = new TextBlock { Margin = new(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
            actions.Inlines.Add(ReleaseMarkdown.Link("重试", async () => await _retry()));
            actions.Inlines.Add("    ");
            actions.Inlines.Add(ReleaseMarkdown.Link("在 GitHub 查看更新记录", () => _open(ReleaseUpdater.Repository + "/releases")));
            _feedback.Children.Add(actions);
        }
    }

    private void FillVersions()
    {
        if (!IsVisible || _batch is not null || _shown is not { } notes) return;
        var end = Math.Min(_rendered + 3, notes.Entries.Count);
        for (; _rendered < end; _rendered++)
        {
            var entry = notes.Entries[_rendered];
            var title = SettingsLayout.Text("v" + entry.Version); title.FontWeight = FontWeights.SemiBold;
            title.Margin = new(0, 12, 0, 2); _versions.Children.Add(title);
            if (string.IsNullOrWhiteSpace(entry.Body)) _versions.Children.Add(SettingsLayout.Text("此版本未提供更新说明", true));
            else ReleaseMarkdown.Append(_versions, entry, _open);
        }
        if (_rendered < notes.Entries.Count)
            _batch = Dispatcher.InvokeAsync(() => { _batch = null; FillVersions(); }, DispatcherPriority.Background);
    }
}

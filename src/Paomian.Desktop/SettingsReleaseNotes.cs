using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Paomian.Core;

namespace Paomian.Desktop;

internal sealed class SettingsReleaseNotes : StackPanel
{
    private readonly Action<string> _open;
    private readonly Func<Task> _retry;
    private readonly ReleaseNoteList _versions;
    private readonly StackPanel _feedback = new();
    private UpdateNotes? _shown;

    internal SettingsReleaseNotes(Action<string> open, Func<Task> retry)
    {
        _open = open; _retry = retry;
        _versions = new(open);
        Visibility = Visibility.Collapsed;
        Margin = new(0, 12, 0, 8);
        AutomationProperties.SetName(this, "更新内容");
        var heading = SettingsLayout.Text("更新内容"); heading.FontWeight = FontWeights.SemiBold; Children.Add(heading);
        Children.Add(_feedback); Children.Add(_versions);
    }

    internal void Display(UpdateNotes? notes)
    {
        if (ReferenceEquals(notes, _shown)) return;
        _shown = notes; _feedback.Children.Clear();
        _versions.Display(notes?.Entries ?? []);
        Visibility = notes is null ? Visibility.Collapsed : Visibility.Visible;
        if (notes is null) return;
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

}

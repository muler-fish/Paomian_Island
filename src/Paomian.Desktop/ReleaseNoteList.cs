using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Paomian.Core;

namespace Paomian.Desktop;

internal sealed class ReleaseNoteList : StackPanel
{
    private readonly Action<string> _open;
    private IReadOnlyList<ReleaseNote> _entries = [];
    private DispatcherOperation? _batch;

    internal ReleaseNoteList(Action<string> open)
    {
        _open = open;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Fill();
            else { _batch?.Abort(); _batch = null; }
        };
    }

    internal void Display(IReadOnlyList<ReleaseNote> entries)
    {
        if (ReferenceEquals(entries, _entries)) return;
        // 网络补齐旧版本时，保留已显示的更新说明及其中的键盘焦点。
        var keep = 0;
        while (keep < Children.Count && keep < entries.Count && _entries[keep] == entries[keep]) keep++;
        if (keep < Children.Count) Children.RemoveRange(keep, Children.Count - keep);
        _entries = entries;
        Fill();
    }

    private void Fill()
    {
        if (!IsVisible || _batch is not null) return;
        var end = Math.Min(Children.Count + 3, _entries.Count);
        while (Children.Count < end)
        {
            var entry = _entries[Children.Count];
            var group = new StackPanel { Margin = new(0, 12, 0, 0) };
            var title = SettingsLayout.Text("v" + entry.Version);
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new(0, 0, 0, 2);
            group.Children.Add(title);
            ReleaseMarkdown.Append(group, entry, _open);
            Children.Add(group);
        }
        if (Children.Count < _entries.Count)
            _batch = Dispatcher.InvokeAsync(() => { _batch = null; Fill(); }, DispatcherPriority.Background);
    }
}

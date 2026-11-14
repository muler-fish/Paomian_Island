using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Paomian.KeyboardPrototype;

internal sealed class ForumTree : TreeView
{
    private readonly Dictionary<TreeViewItem, Action> _actions = [];
    private bool _keepOffset;

    private ScrollViewer? View => Template?.FindName("PART_ScrollViewer", this) as ScrollViewer;
    internal double VerticalOffset => View?.VerticalOffset ?? 0;

    internal ForumTree()
    {
        AutomationProperties.SetName(this, "版块导航");
        SetResourceReference(StyleProperty, "ForumTree");
        InputMethod.SetIsInputMethodEnabled(this, false);
        // 切栏时恢复上次选择而不触发滚动。树中移动仍把新选中的行带入视口。
        AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) =>
        {
            if (_keepOffset) e.Handled = true;
        }));
        PreviewMouseLeftButtonDown += (_, e) =>
        {
            for (var source = e.OriginalSource as Visual; source is not null && source != this; source = VisualTreeHelper.GetParent(source) as Visual)
            {
                if (source is ScrollBar) return;
                if (source is TreeViewItem item)
                {
                    item.Focus();
                    return;
                }
            }
            FocusSelection();
        };
    }

    internal TreeViewItem AddItem(ItemsControl parent, string name, Action? action = null, bool expanded = false)
    {
        var item = new TreeViewItem { Header = name, IsExpanded = expanded };
        parent.Items.Add(item);
        if (action is not null)
        {
            _actions.Add(item, action);
            item.MouseLeftButtonUp += (_, e) =>
            {
                if (e.Handled || !item.IsKeyboardFocused || !item.IsMouseOver) return;
                e.Handled = true;
                action();
            };
        }
        return item;
    }

    internal void FocusSelection()
    {
        var visible = VisibleItems(this);
        var item = visible.FirstOrDefault(item => item.IsSelected) ?? visible.FirstOrDefault();
        if (item is null) { Focus(); return; }
        _keepOffset = true;
        try { item.Focus(); }
        finally { _keepOffset = false; }
    }

    internal void RestoreOffset(double offset) => View?.ScrollToVerticalOffset(offset);

    internal bool HandleKey(Key key, bool repeat)
    {
        if (key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Space)) return false;
        var items = VisibleItems(this).ToArray();
        if (items.Length == 0) return true;
        var index = Array.IndexOf(items, SelectedItem as TreeViewItem);
        var item = items[Math.Max(0, index)];
        switch (key)
        {
            case Key.Up or Key.Down:
                var next = items[Math.Clamp(index + (key == Key.Up ? -1 : 1), 0, items.Length - 1)];
                next.Focus();
                next.BringIntoView();
                break;
            case Key.Left when item.IsExpanded:
                item.IsExpanded = false;
                break;
            case Key.Left:
                if (ItemsControlFromItemContainer(item) is TreeViewItem parent) parent.Focus();
                break;
            case Key.Right when item.HasItems:
                if (item.IsExpanded && item.Items[0] is TreeViewItem child) child.Focus();
                else item.IsExpanded = true;
                break;
            case Key.Enter or Key.Space when !repeat:
                if (item.HasItems) item.IsExpanded = !item.IsExpanded;
                else if (_actions.TryGetValue(item, out var action)) action();
                break;
        }
        return true;
    }

    private static IEnumerable<TreeViewItem> VisibleItems(ItemsControl parent)
    {
        foreach (TreeViewItem item in parent.Items)
        {
            if (!item.IsVisible || !item.IsEnabled) continue;
            yield return item;
            if (!item.IsExpanded) continue;
            foreach (var child in VisibleItems(item)) yield return child;
        }
    }
}

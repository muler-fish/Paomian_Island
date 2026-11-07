using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tide.Core;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tide.Desktop;

/// <summary>依附编辑工具栏的颜文字菜单：最近使用／全部／自定义分类、自定义管理与大型图案预览；点击条目插入完整原文。</summary>
internal sealed class EmoticonMenu
{
    private const string RecentCategory = "recent", AllCategory = "all", CustomCategory = "custom", HiddenCategory = "hidden";
    // 同一次运行内记住上次查看的分类。
    private static string _category = AllCategory;
    private readonly Window _owner;
    private readonly Func<UserState> _state;
    private readonly Action _save;
    private readonly Action<string> _choose;
    private readonly Action _cancel;
    private readonly StackPanel _list = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new(8, 6, 8, 0), FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly Button _hiddenEntry;
    private readonly List<RadioButton> _tabs = [];
    private Border? _surface;
    private FrameworkElement? _previewAnchor;
    private bool _previewPinned;
    internal Popup Popup { get; } = new() { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
    // 预览放在菜单的逻辑树里：点击或滚动预览不算点到菜单外面，菜单不会因此关闭。
    internal Popup Preview { get; } = new() { StaysOpen = true, AllowsTransparency = true };
    internal string Category => _category;

    public EmoticonMenu(Window owner, FrameworkElement anchor, Func<UserState> state, Action save, Action<string> choose, Action cancel)
    {
        _owner = owner; _state = state; _save = save; _choose = choose; _cancel = cancel; Popup.PlacementTarget = anchor;
        owner.Closed += (_, _) => Close();
        owner.LocationChanged += (_, _) => Close();
        owner.SizeChanged += (_, _) => Close();
        Popup.Closed += (_, _) => { Preview.IsOpen = false; _previewPinned = false; };
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        _hiddenEntry = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(4, 6, 0, 0), FontSize = 12, MinHeight = 28, Padding = new(8, 2, 8, 2) };
        _hiddenEntry.SetResourceReference(FrameworkElement.StyleProperty, "Quiet");
        _hiddenEntry.Click += (_, _) => { SetCategory(_category == HiddenCategory ? AllCategory : HiddenCategory); };
    }

    public void Toggle()
    {
        if (Popup.IsOpen) { Close(); return; }
        Open();
    }

    private void Close() { Preview.IsOpen = false; Popup.IsOpen = false; }

    private void Open()
    {
        _message.Visibility = Visibility.Collapsed;
        var root = new DockPanel { Margin = new(8) };
        var panel = new MenuPanel(); AutomationProperties.SetName(panel, "颜文字菜单");
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var add = Tool(SymbolRegular.Add24, "新增", () => Edit(null)); DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal }; _tabs.Clear();
        foreach (var (key, label) in new[] { (RecentCategory, "最近使用"), (AllCategory, "全部"), (CustomCategory, "自定义") })
        {
            var tab = new RadioButton { Content = label, Tag = key, GroupName = "EmoticonCategory", Margin = new(0, 0, 4, 0) };
            tab.SetResourceReference(FrameworkElement.StyleProperty, "ReaderToggle");
            tab.Checked += (_, _) => { if (_category != key) SetCategory(key); };
            _tabs.Add(tab); tabs.Children.Add(tab);
        }
        header.Children.Add(tabs);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel(); Detach(_hiddenEntry); Detach(_message);
        footer.Children.Add(_hiddenEntry); footer.Children.Add(_message); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var scroll = new ScrollViewer {
            Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            // 固定高度：各分类内容多少不同，若随内容伸缩，弹出层会变高变矮，甚至被系统改放到按钮另一侧。
            Height = Math.Clamp(_owner.ActualHeight - 200, 160, 400)
        };
        Detach(_list); panel.Children.Add(_list); root.Children.Add(scroll);
        Detach(Preview); root.Children.Add(Preview);
        _surface = new Border { Child = root, BorderThickness = new(1), CornerRadius = UiMetrics.ControlCornerRadius, Width = Math.Clamp(_owner.ActualWidth - 40, 320, 680) };
        System.Windows.Documents.TextElement.SetFontFamily(_surface, AppFonts.Family);
        _surface.SetResourceReference(Border.BackgroundProperty, "Surface"); _surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        KeyboardNavigation.SetTabNavigation(_surface, KeyboardNavigationMode.Cycle);
        _surface.PreviewKeyDown += OnKeyDown;
        Render();
        Popup.Child = _surface; Popup.IsOpen = true;
        _surface.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (Popup.IsOpen) _list.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_previewPinned) { HidePreview(force: true); e.Handled = true; return; }
            Close(); _cancel(); e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down or Key.Left or Key.Right && Keyboard.FocusedElement is UIElement focused)
        {
            var direction = e.Key switch { Key.Up => FocusNavigationDirection.Up, Key.Down => FocusNavigationDirection.Down, Key.Left => FocusNavigationDirection.Left, _ => FocusNavigationDirection.Right };
            focused.MoveFocus(new TraversalRequest(direction)); e.Handled = true;
        }
    }

    private void SetCategory(string key) { _category = key; Render(); }

    private void Render()
    {
        HidePreview(force: true);
        var state = _state();
        foreach (var tab in _tabs) tab.IsChecked = Equals(tab.Tag, _category);
        _list.Children.Clear();
        var hiddenCount = state.HiddenEmoticons.Count(t => EmoticonCatalog.Entries.Any(e => e.Text == t));
        _hiddenEntry.Content = _category == HiddenCategory ? "‹ 返回全部" : $"已隐藏 {hiddenCount} 个内置颜文字";
        // 没有隐藏条目时只是不可见、仍占位，保持弹出层高度不变。
        _hiddenEntry.Visibility = hiddenCount > 0 || _category == HiddenCategory ? Visibility.Visible : Visibility.Hidden;
        switch (_category)
        {
            case RecentCategory:
                var recent = EmoticonCatalog.Recent(state); Heading("最近使用");
                if (recent.Count == 0) Empty("暂无。插入过的颜文字会显示在这里。"); else _list.Children.Add(Items(recent));
                break;
            case CustomCategory:
                var custom = EmoticonCatalog.Custom(state); Heading($"自定义（{custom.Count}）");
                if (custom.Count == 0) Empty("还没有自定义颜文字。点右上角「新增」添加。"); else _list.Children.Add(Items(custom));
                break;
            case HiddenCategory:
                var hidden = EmoticonCatalog.Hidden(state); Heading($"已隐藏的内置颜文字（{hidden.Count}）");
                if (hidden.Count == 0) Empty("没有隐藏的内置颜文字。");
                foreach (var entry in hidden) _list.Children.Add(HiddenRow(entry));
                break;
            default:
                var all = EmoticonCatalog.Visible(state); Heading($"全部颜文字（{all.Count}）"); _list.Children.Add(Items(all));
                break;
        }
    }

    private WrapPanel Items(IEnumerable<EmoticonEntry> entries)
    {
        var wrap = new WrapPanel();
        foreach (var entry in entries) wrap.Children.Add(Item(entry));
        return wrap;
    }

    private FrameworkElement Item(EmoticonEntry entry)
    {
        var label = new EmojiLabel { Text = entry.Display, FontFamily = AppFonts.Family, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis };
        // 右侧内边距留给叠放的「⋯」，显隐时宽度不变，换行位置不会跳动。
        var button = new Button { Content = label, MinWidth = 98, MaxWidth = 220, Height = 36, Padding = new(8, 5, 26, 5), Tag = entry };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Quiet");
        AutomationProperties.SetName(button, entry.Display);
        button.Click += (_, _) => { Close(); _choose(entry.Text); };
        var more = new Button { Content = new SymbolIcon(SymbolRegular.MoreHorizontal24) { FontSize = 14 }, Width = 24, Height = 28, MinHeight = 28, Padding = new(0), Margin = new(0, 0, 2, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ToolTip = "更多操作" };
        more.SetResourceReference(FrameworkElement.StyleProperty, "Quiet");
        AutomationProperties.SetName(more, "更多操作：" + entry.Display);
        var menu = Actions(entry); more.Click += (_, _) => { menu.PlacementTarget = more; menu.IsOpen = true; };
        button.ContextMenu = menu;
        var item = new Grid { Margin = new(2), Background = Brushes.Transparent };
        item.Children.Add(button); item.Children.Add(more);
        // 自定义条目常显「⋯」；内置条目在悬停或键盘聚焦时显示，避免两百个按钮同时出现。
        void UpdateMore() => more.Visibility = entry.IsCustom || item.IsMouseOver || item.IsKeyboardFocusWithin || menu.IsOpen ? Visibility.Visible : Visibility.Hidden;
        item.MouseEnter += (_, _) => UpdateMore(); item.MouseLeave += (_, _) => UpdateMore();
        item.IsKeyboardFocusWithinChanged += (_, _) => UpdateMore(); menu.Closed += (_, _) => UpdateMore(); UpdateMore();
        if (entry.Display != entry.Text)
        {
            button.MouseEnter += (_, _) => ShowPreview(entry, item, pinned: false);
            button.GotKeyboardFocus += (_, _) => ShowPreview(entry, item, pinned: false);
            button.MouseLeave += (_, _) => HidePreview(); button.LostKeyboardFocus += (_, _) => HidePreview();
        }
        return item;
    }

    private ContextMenu Actions(EmoticonEntry entry)
    {
        var menu = new ContextMenu();
        if (entry.Display != entry.Text) menu.Items.Add(Action("查看完整内容", () => ShowPreview(entry, (FrameworkElement?)menu.PlacementTarget ?? _surface!, pinned: true)));
        if (entry.IsCustom)
        {
            menu.Items.Add(Action("编辑", () => Edit(entry)));
            menu.Items.Add(Action("删除", () => Delete(entry)));
        }
        else menu.Items.Add(Action("隐藏", () => Change(state => EmoticonCatalog.Hide(state, entry.Text))));
        return menu;
        static MenuItem Action(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (_, _) => action(); return item; }
    }

    private FrameworkElement HiddenRow(EmoticonEntry entry)
    {
        var row = new DockPanel { Margin = new(4, 2, 4, 2) };
        var restore = Tool(SymbolRegular.Eye24, "恢复", () => Change(state => EmoticonCatalog.Restore(state, entry.Text)));
        AutomationProperties.SetName(restore, "恢复：" + entry.Display);
        DockPanel.SetDock(restore, Dock.Right); row.Children.Add(restore);
        var label = new EmojiLabel { Text = entry.Display, FontFamily = AppFonts.Family, FontSize = 15, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(label);
        return row;
    }

    // 修改后立即保存；写入失败时撤回内存状态并提示，界面与磁盘保持一致。
    private void Change(Action<UserState> change)
    {
        var state = _state();
        var hidden = state.HiddenEmoticons.ToList(); var custom = state.CustomEmoticons.ToList(); var recent = state.RecentEmoticons.ToList();
        try { change(state); _save(); Render(); }
        catch (Exception)
        {
            state.HiddenEmoticons = hidden; state.CustomEmoticons = custom; state.RecentEmoticons = recent; Render();
            _message.Text = "无法保存颜文字设置，请检查资料目录权限。"; _message.Visibility = Visibility.Visible;
        }
    }

    private void Edit(EmoticonEntry? entry)
    {
        // 模态窗口会夺走鼠标捕获使菜单关闭；编辑结束后按原分类重新打开，正文选区由编辑器保留。
        Close();
        var editor = new EmoticonEditWindow(_state(), _save, entry) { Owner = _owner };
        editor.ShowDialog();
        if (editor.Saved is not null) _category = CustomCategory;
        Open();
    }

    private void Delete(EmoticonEntry entry)
    {
        Close();
        var confirmed = ConfirmWindow.Ask(_owner, "删除颜文字", $"删除自定义颜文字「{entry.Display}」？最近使用中的对应记录也会一起移除。");
        Open();
        if (confirmed) Change(state => EmoticonCatalog.Delete(state, entry.CustomId));
    }

    internal void ShowPreview(EmoticonEntry entry, FrameworkElement anchor, bool pinned)
    {
        if (_surface is null || !Popup.IsOpen || (_previewPinned && !pinned)) return;
        _previewAnchor = anchor; _previewPinned = pinned;
        // 底部留出横向滚动条的高度，避免滚动条盖住最后一行图案。
        var text = new EmojiLabel { Text = entry.Text, FontSize = 16, LineHeight = 19, TextWrapping = TextWrapping.NoWrap, Margin = new(0, 0, 8, 14),
            FontFamily = entry.IsAsciiArt ? new FontFamily("Consolas, " + AppFonts.Family.Source) : AppFonts.Family };
        AutomationProperties.SetName(text, "颜文字完整内容");
        var scroll = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new(0, 4, 0, 0) };
        var header = new DockPanel { Margin = new(0, 0, 0, 6) };
        if (pinned)
        {
            var close = new Button { Content = new SymbolIcon(SymbolRegular.Dismiss24) { FontSize = 12 }, Width = 28, Height = 28, Padding = new(0) };
            close.SetResourceReference(FrameworkElement.StyleProperty, "Quiet"); AutomationProperties.SetName(close, "关闭预览");
            close.Click += (_, _) => HidePreview(force: true);
            var insert = Tool(SymbolRegular.Add24, "插入", () => { Close(); _choose(entry.Text); });
            DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(insert, Dock.Right); header.Children.Add(close); header.Children.Add(insert);
        }
        var title = new TextBlock { Text = entry.Display, FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); header.Children.Add(title);
        var content = new DockPanel(); DockPanel.SetDock(header, Dock.Top); content.Children.Add(header); content.Children.Add(scroll);
        var border = new Border { Child = content, BorderThickness = new(1), CornerRadius = UiMetrics.ControlCornerRadius, Padding = new(12, 10, 12, 12), IsHitTestVisible = pinned };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line");
        AutomationProperties.SetName(border, "颜文字预览");
        Place(border, anchor);
        Preview.Child = border; Preview.IsOpen = true;
    }

    // 优先放在菜单左右两侧空间较大的一边，尺寸受显示器工作区限制；两侧都放不下时贴在条目下方、限制在菜单宽度内。
    private void Place(Border border, FrameworkElement anchor)
    {
        var work = WindowPlacement.WorkArea(_owner);
        var source = PresentationSource.FromVisual(_surface!);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = toDip.Transform(_surface!.PointToScreen(new Point()));
        var right = work.Right - (topLeft.X + _surface.ActualWidth) - 8; var left = topLeft.X - work.Left - 8;
        var side = Math.Max(right, left);
        border.MaxHeight = Math.Max(120, Math.Min(480, work.Height - 24));
        if (side >= 240)
        {
            Preview.PlacementTarget = _surface; Preview.Placement = right >= left ? PlacementMode.Right : PlacementMode.Left;
            Preview.HorizontalOffset = right >= left ? 6 : -6;
            border.MaxWidth = Math.Min(560, side);
            Preview.VerticalOffset = Math.Max(0, anchor.TranslatePoint(new Point(), _surface).Y - 8);
        }
        else
        {
            Preview.PlacementTarget = anchor; Preview.Placement = PlacementMode.Bottom; Preview.HorizontalOffset = 0; Preview.VerticalOffset = 2;
            border.MaxWidth = Math.Max(200, _surface.ActualWidth - 16);
            border.MaxHeight = Math.Max(120, Math.Min(border.MaxHeight, _surface.ActualHeight * 0.6));
        }
    }

    private void HidePreview(bool force = false)
    {
        if (_previewPinned && !force) return;
        _previewPinned = false; _previewAnchor = null; Preview.IsOpen = false;
    }

    private void Heading(string title)
    {
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new(8, 4, 8, 8), FontSize = 13 };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); _list.Children.Add(heading);
    }
    private void Empty(string text)
    {
        var empty = new TextBlock { Text = text, Margin = new(8, 2, 8, 8), TextWrapping = TextWrapping.Wrap };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); _list.Children.Add(empty);
    }
    internal static Button Tool(SymbolRegular symbol, string label, Action action)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new SymbolIcon(symbol) { FontSize = 16, Margin = new(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content };
        button.SetResourceReference(FrameworkElement.StyleProperty, "EditorTool");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }
    // 重建菜单时复用同一控件实例，先从旧父级移除。
    private static void Detach(FrameworkElement element) { if (element.Parent is Panel parent) parent.Children.Remove(element); }
    private sealed class MenuPanel : StackPanel
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
}

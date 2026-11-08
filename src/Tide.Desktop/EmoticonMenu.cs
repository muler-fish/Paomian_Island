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

/// <summary>依附编辑工具栏的颜文字菜单：最近使用／全部／自定义分类、自定义管理与大型图案就近预览；点击条目插入完整原文。</summary>
internal sealed class EmoticonMenu
{
    private const string RecentCategory = "recent", AllCategory = "all", CustomCategory = "custom", HiddenCategory = "hidden";
    // 列表四列等宽；条目文字最多三行，超出部分省略，完整内容看预览。
    private const int Columns = 4, MaxItemLines = 3;
    private const double ItemLineHeight = 20, MinColumnWidth = 96, MaxSurfaceWidth = 472, MaxListHeight = 270, ScrollGutter = 10;
    // 同一次运行内记住上次查看的分类。
    private static string _category = AllCategory;
    private readonly Window _owner;
    private readonly Func<UserState> _state;
    private readonly Action _save;
    private readonly Action<string> _choose;
    private readonly Action _cancel;
    private readonly RowList _list = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, Margin = new(8, 6, 8, 0), FontSize = 12, Visibility = Visibility.Collapsed };
    private readonly Button _footerEntry;
    private readonly List<RadioButton> _tabs = [];
    private readonly DispatcherTimer _hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(320) };
    private readonly DispatcherTimer _closeTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private List<EmoticonEntry> _shown = [];
    private Border? _surface;
    private ScrollViewer? _scroll;
    private Button? _hoverTarget;
    private FrameworkElement? _previewAnchor;
    private bool _previewPinned, _managing;
    private int _columns = Columns;
    private string _renderedKey = "";
    internal Popup Popup { get; } = new() { Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
    // 预览放在菜单的逻辑树里：点击或滚动预览不算点到菜单外面，菜单不会因此关闭。
    internal Popup Preview { get; } = new() { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Custom };
    internal string Category => _category;
    /// <summary>当前分类列出的全部条目（列表按行虚拟化，屏幕外的条目没有控件）。</summary>
    internal IReadOnlyList<EmoticonEntry> Shown => _shown;
    internal int ColumnCount => _columns;
    internal int PreviewShownCount { get; private set; }

    public EmoticonMenu(Window owner, FrameworkElement anchor, Func<UserState> state, Action save, Action<string> choose, Action cancel)
    {
        _owner = owner; _state = state; _save = save; _choose = choose; _cancel = cancel; Popup.PlacementTarget = anchor;
        owner.Closed += (_, _) => Close();
        owner.LocationChanged += (_, _) => Close();
        owner.SizeChanged += (_, _) => Close();
        Popup.Closed += (_, _) => { HidePreview(force: true); _managing = false; };
        _message.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        _footerEntry = new Button { HorizontalAlignment = HorizontalAlignment.Left, Margin = new(4, 6, 0, 0), FontSize = 12, MinHeight = 28, Padding = new(8, 2, 8, 2) };
        _footerEntry.SetResourceReference(FrameworkElement.StyleProperty, "Quiet");
        _footerEntry.Click += (_, _) =>
        {
            if (_category == CustomCategory) { _managing = !_managing; Render(); }
            else SetCategory(_category == HiddenCategory ? AllCategory : HiddenCategory);
        };
        _list.Build = BuildRow;
        Preview.CustomPopupPlacementCallback = PlacePreview;
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); if (_hoverTarget is { Tag: EmoticonEntry entry } target) ShowPreview(entry, target, pinned: false, onlyWhenTrimmed: true); };
        _closeTimer.Tick += (_, _) =>
        {
            _closeTimer.Stop();
            if (!_previewPinned && Preview.Child?.IsMouseOver != true && _previewAnchor?.IsMouseOver != true) HidePreview();
        };
    }

    public void Toggle()
    {
        if (Popup.IsOpen) { Close(); return; }
        Open();
    }

    private void Close() { HidePreview(force: true); Popup.IsOpen = false; }

    private void Open()
    {
        _message.Visibility = Visibility.Collapsed;
        var surface = Fit();
        // 内容没变时沿用上次生成的行，重新打开只需重新布局。
        Render(force: false);
        Popup.IsOpen = true;
        surface.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (Popup.IsOpen) _list.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }));
    }

    /// <summary>编辑窗口空闲时预先建立弹出层并排版首屏条目，首次点开不必再等字体和控件初始化。</summary>
    internal void Prepare()
    {
        if (Popup.IsOpen) return;
        var surface = Fit();
        Render(force: false);
        surface.Measure(new Size(surface.Width, double.PositiveInfinity));
    }

    // 宽度按编辑窗口分配，正常尺寸下正好四列，过窄时减少列数；高度约为旧版的三分之二，并受显示器可用空间限制。
    private Border Fit()
    {
        var surface = EnsureSurface();
        surface.Width = Math.Clamp(_owner.ActualWidth - 40, 300, MaxSurfaceWidth);
        _list.Height = ListHeight();
        _columns = Math.Clamp((int)((surface.Width - 18 - ScrollGutter) / MinColumnWidth), 2, Columns);
        return surface;
    }

    // 弹出层外壳只建一次，之后每次打开只换列表数据。
    private Border EnsureSurface()
    {
        if (_surface is not null) return _surface;
        var root = new DockPanel { Margin = new(8) };
        AutomationProperties.SetName(_list, "颜文字菜单");
        var header = new DockPanel { Margin = new(0, 0, 0, 8) };
        var add = Tool(SymbolRegular.Add24, "新增", () => Edit(null)); DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, label) in new[] { (RecentCategory, "最近使用"), (AllCategory, "全部"), (CustomCategory, "自定义") })
        {
            var tab = new RadioButton { Content = label, Tag = key, GroupName = "EmoticonCategory", Margin = new(0, 0, 4, 0) };
            tab.SetResourceReference(FrameworkElement.StyleProperty, "ReaderToggle");
            tab.Checked += (_, _) => { if (_category != key) SetCategory(key); };
            _tabs.Add(tab); tabs.Children.Add(tab);
        }
        header.Children.Add(tabs);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel();
        footer.Children.Add(_footerEntry); footer.Children.Add(_message); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        // 按行虚拟化：只为可见附近的行创建控件；行高随该行最高的条目变化，所以按像素滚动。
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer), "Scroll");
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(UIElement.FocusableProperty, false);
        // 右侧留出浮动滚动条的宽度，最后一列和分类提示不被滚动条压住。
        var presenter = new FrameworkElementFactory(typeof(ItemsPresenter));
        presenter.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, ScrollGutter, 0));
        scroll.AppendChild(presenter);
        _list.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroll };
        _list.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Standard);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
        _list.Loaded += (_, _) =>
        {
            if (_scroll is not null) return;
            _scroll = _list.Template.FindName("Scroll", _list) as ScrollViewer;
            if (_scroll is not null) _scroll.ScrollChanged += (_, e) => { if (e.VerticalChange != 0) HidePreview(force: true); };
        };
        // Popup 不占位置；放在列表之前，让列表作为最后一个子元素填满剩余宽度。
        DockPanel.SetDock(Preview, Dock.Top); root.Children.Insert(0, Preview);
        root.Children.Add(_list);
        _surface = new Border { Child = root, BorderThickness = new(1), CornerRadius = UiMetrics.ControlCornerRadius };
        System.Windows.Documents.TextElement.SetFontFamily(_surface, AppFonts.Family);
        _surface.SetResourceReference(Border.BackgroundProperty, "Surface"); _surface.SetResourceReference(Border.BorderBrushProperty, "Line");
        KeyboardNavigation.SetTabNavigation(_surface, KeyboardNavigationMode.Cycle);
        _surface.PreviewKeyDown += OnKeyDown;
        Popup.Child = _surface;
        return _surface;
    }

    // 列表固定高度（切换分类时弹出层不变高变矮）；按钮上下两侧较大的一边扣除标题和底部操作后决定上限。
    private double ListHeight()
    {
        var height = Math.Clamp((_owner.ActualHeight - 200) * 0.675, 140, MaxListHeight);
        if (Popup.PlacementTarget is not FrameworkElement anchor || PresentationSource.FromVisual(anchor) is not { CompositionTarget: { } target }) return height;
        var work = WindowPlacement.WorkArea(_owner);
        var toDip = target.TransformFromDevice;
        var top = toDip.Transform(anchor.PointToScreen(new Point())).Y; var bottom = toDip.Transform(anchor.PointToScreen(new Point(0, anchor.ActualHeight))).Y;
        var space = Math.Max(top - work.Top, work.Bottom - bottom) - 110;
        return Math.Max(120, Math.Min(height, space));
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

    private void SetCategory(string key) { _category = key; _managing = false; Render(); }

    private void Render(bool force = true)
    {
        HidePreview(force: true);
        var state = _state();
        var key = RenderKey(state);
        if (!force && key == _renderedKey) { _scroll?.ScrollToTop(); return; }
        _renderedKey = key;
        foreach (var tab in _tabs) tab.IsChecked = Equals(tab.Tag, _category);
        var hiddenCount = EmoticonCatalog.HiddenCount(state);
        var rows = new List<object>();
        switch (_category)
        {
            case RecentCategory:
                _shown = EmoticonCatalog.Recent(state); rows.Add(new HeadingRow("最近使用", "右键可复制"));
                if (_shown.Count == 0) rows.Add(new EmptyRow("暂无。插入过的颜文字会显示在这里。"));
                break;
            case CustomCategory:
                _shown = EmoticonCatalog.Custom(state);
                rows.Add(new HeadingRow($"自定义（{_shown.Count}）", _managing ? "点击条目编辑、删除或复制" : "右键可复制、编辑或删除"));
                if (_shown.Count == 0) rows.Add(new EmptyRow("还没有自定义颜文字。点右上角「新增」添加。"));
                break;
            case HiddenCategory:
                _shown = EmoticonCatalog.Hidden(state); rows.Add(new HeadingRow($"已隐藏的内置颜文字（{_shown.Count}）", ""));
                if (_shown.Count == 0) rows.Add(new EmptyRow("没有隐藏的内置颜文字。"));
                rows.AddRange(_shown.Select(entry => new HiddenRow(entry)));
                break;
            default:
                _shown = EmoticonCatalog.Visible(state); rows.Add(new HeadingRow($"全部颜文字（{_shown.Count}）", "右键可复制或隐藏"));
                break;
        }
        if (_category != HiddenCategory) rows.AddRange(_shown.Chunk(_columns).Select(chunk => new ItemsRow(chunk)));
        // 底部入口：全部分类里进入已隐藏；自定义分类里切换整理模式。没有可显示的入口时只隐藏、仍占位，保持弹出层高度不变。
        _footerEntry.Content = _category switch
        {
            HiddenCategory => "‹ 返回全部",
            CustomCategory => _managing ? "完成整理" : "整理自定义颜文字",
            _ => $"已隐藏 {hiddenCount} 个内置颜文字"
        };
        _footerEntry.Visibility = _category is HiddenCategory || (_category == CustomCategory && _shown.Count > 0) || (_category == AllCategory && hiddenCount > 0) ? Visibility.Visible : Visibility.Hidden;
        AutomationProperties.SetName(_footerEntry, _footerEntry.Content.ToString());
        _list.ItemsSource = rows;
        _scroll?.ScrollToTop();
    }

    // 影响列表内容的全部状态；与上次一致时不重建行。
    private string RenderKey(UserState state) => string.Join("\u0002", _category, _columns, _managing,
        string.Join("\u0001", state.HiddenEmoticons), string.Join("\u0001", state.CustomEmoticons.Select(c => c.Id + "\u0003" + c.Name + "\u0003" + c.Text)), string.Join("\u0001", state.RecentEmoticons));

    private FrameworkElement BuildRow(object row) => row switch
    {
        ItemsRow items => Items(items.Entries),
        HiddenRow hidden => HiddenItem(hidden.Entry),
        HeadingRow heading => Heading(heading.Title, heading.Hint),
        EmptyRow empty => Empty(empty.Text),
        _ => new FrameworkElement()
    };

    // 一行等宽的 N 列；Grid 的自动行高取该行最高的条目，同行按钮一起拉伸到这个高度。
    private Grid Items(EmoticonEntry[] entries)
    {
        var grid = new Grid();
        for (var i = 0; i < _columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < entries.Length; i++) { var item = Item(entries[i]); Grid.SetColumn(item, i); grid.Children.Add(item); }
        return grid;
    }

    private Button Item(EmoticonEntry entry)
    {
        var label = new EmojiLabel { Text = entry.Display, FontFamily = AppFonts.Family, FontSize = 15, LineHeight = ItemLineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxHeight = ItemLineHeight * MaxItemLines };
        var button = new Button { Content = label, MinHeight = 36, Margin = new(2), Padding = new(6, 5, 6, 5), Tag = entry,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Quiet");
        AutomationProperties.SetName(button, entry.Display);
        var menu = Actions(entry); button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            // 整理模式下左键也打开管理菜单，不插入。
            if (_managing && entry.IsCustom) { menu.PlacementTarget = button; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; return; }
            Close(); _choose(entry.Text);
        };
        button.MouseEnter += (_, _) => HoverEnter(button);
        button.MouseLeave += (_, _) => HoverLeave(button);
        button.GotKeyboardFocus += (_, _) => { if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) ShowPreview(entry, button, pinned: false, onlyWhenTrimmed: true); };
        button.LostKeyboardFocus += (_, _) => { if (_previewAnchor == button) HidePreview(); };
        menu.Opened += (_, _) => HidePreview();
        return button;
    }

    private ContextMenu Actions(EmoticonEntry entry)
    {
        var menu = new ContextMenu();
        menu.Items.Add(Action("复制", () => Copy(entry)));
        if (entry.IsLarge || entry.Display != entry.Text) menu.Items.Add(Action("查看完整内容", () => ShowPreview(entry, (FrameworkElement?)menu.PlacementTarget ?? _surface!, pinned: true)));
        if (entry.IsCustom)
        {
            menu.Items.Add(Action("编辑", () => Edit(entry)));
            menu.Items.Add(Action("删除", () => Delete(entry)));
        }
        else menu.Items.Add(Action("隐藏", () => Change(state => EmoticonCatalog.Hide(state, entry.Text))));
        return menu;
        static MenuItem Action(string header, Action action) { var item = new MenuItem { Header = header }; item.Click += (_, _) => action(); return item; }
    }

    // 复制完整原文；剪贴板换行用 CRLF，与插入正文一致。
    private void Copy(EmoticonEntry entry)
    {
        try { Clipboard.SetText(entry.Text.Replace("\n", Environment.NewLine)); _message.Visibility = Visibility.Collapsed; }
        catch (Exception) { _message.Text = "无法写入剪贴板，请稍后再试。"; _message.Visibility = Visibility.Visible; }
    }

    private DockPanel HiddenItem(EmoticonEntry entry)
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
        var managing = _managing;
        Close();
        var editor = new EmoticonEditWindow(_state(), _save, entry) { Owner = _owner };
        editor.ShowDialog();
        if (editor.Saved is not null) _category = CustomCategory;
        _managing = managing && _category == CustomCategory;
        Open();
    }

    private void Delete(EmoticonEntry entry)
    {
        var managing = _managing;
        Close();
        var confirmed = ConfirmWindow.Ask(_owner, "删除颜文字", $"删除自定义颜文字「{entry.Display}」？最近使用中的对应记录也会一起移除。");
        _managing = managing;
        Open();
        if (confirmed) Change(state => EmoticonCatalog.Delete(state, entry.CustomId));
    }

    // 悬停短暂停留后才显示预览，鼠标快速划过一串条目时不会逐个弹出；已有预览时切换更快。
    private void HoverEnter(Button button)
    {
        _closeTimer.Stop();
        if (_previewPinned || (_previewAnchor == button && Preview.IsOpen)) return;
        _hoverTarget = button;
        _hoverTimer.Stop(); _hoverTimer.Interval = TimeSpan.FromMilliseconds(Preview.IsOpen ? 120 : 320); _hoverTimer.Start();
    }

    private void HoverLeave(Button button)
    {
        if (_hoverTarget == button) { _hoverTimer.Stop(); _hoverTarget = null; }
        if (Preview.IsOpen && !_previewPinned) _closeTimer.Start();
    }

    internal void ShowPreview(EmoticonEntry entry, FrameworkElement anchor, bool pinned, bool onlyWhenTrimmed = false)
    {
        if (_surface is null || !Popup.IsOpen || (_previewPinned && !pinned)) return;
        // 已在条目里完整显示的短颜文字不重复预览。
        if (onlyWhenTrimmed && !NeedsPreview(entry, anchor)) { HidePreview(); return; }
        _hoverTimer.Stop(); _closeTimer.Stop();
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
        // 预览可以接收鼠标，以便滚动超大的图案；鼠标从条目移进预览时不关闭。
        var border = new Border { Child = content, BorderThickness = new(1), CornerRadius = UiMetrics.ControlCornerRadius, Padding = new(12, 10, 12, 12) };
        border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line");
        border.MouseEnter += (_, _) => _closeTimer.Stop();
        border.MouseLeave += (_, _) => { if (!_previewPinned) _closeTimer.Start(); };
        AutomationProperties.SetName(border, "颜文字预览");
        var work = WindowPlacement.WorkArea(_owner);
        border.MaxWidth = Math.Max(200, Math.Min(520, work.Width - 16));
        border.MaxHeight = Math.Max(120, Math.Min(360, work.Height / 2 - 16));
        Preview.IsOpen = false;
        Preview.PlacementTarget = anchor; Preview.Child = border; Preview.IsOpen = true;
        PreviewShownCount++;
    }

    // 判断条目里显示的是否已是完整原文：简称、截断或超过三行都需要预览。
    private static bool NeedsPreview(EmoticonEntry entry, FrameworkElement anchor)
    {
        if (entry.Display != entry.Text) return true;
        if (anchor is not Button { Content: EmojiLabel label } || label.ActualWidth <= 0) return false;
        var probe = new EmojiLabel { Text = entry.Text, FontFamily = label.FontFamily, FontSize = label.FontSize, LineHeight = label.LineHeight, LineStackingStrategy = label.LineStackingStrategy, TextWrapping = TextWrapping.Wrap };
        probe.Measure(new Size(label.ActualWidth + 0.5, double.PositiveInfinity));
        return probe.DesiredSize.Height > label.ActualHeight + 0.5;
    }

    // 优先放在条目正上方并水平居中；上方放不下时放到下方。超出屏幕时由 Popup 沿水平方向移回工作区内。
    private static CustomPopupPlacement[] PlacePreview(Size popup, Size target, Point offset)
    {
        var x = (target.Width - popup.Width) / 2;
        return [new(new Point(x, -popup.Height - 4), PopupPrimaryAxis.Horizontal), new(new Point(x, target.Height + 4), PopupPrimaryAxis.Horizontal)];
    }

    private void HidePreview(bool force = false)
    {
        _hoverTimer.Stop(); _hoverTarget = null;
        if (_previewPinned && !force) return;
        _closeTimer.Stop();
        _previewPinned = false; _previewAnchor = null; Preview.IsOpen = false;
    }

    /// <summary>把条目滚动到可见区域并返回它的按钮（列表虚拟化后，屏幕外的条目需要先滚动才有控件）。</summary>
    internal Button? Realize(EmoticonEntry entry)
    {
        var index = _list.Items.Cast<object>().ToList().FindIndex(row => row is ItemsRow items && items.Entries.Contains(entry));
        if (index < 0) return null;
        if (FindPanel(_list) is VirtualizingStackPanel panel) panel.BringIndexIntoViewPublic(index);
        _list.UpdateLayout();
        return (_list.ItemContainerGenerator.ContainerFromIndex(index) as ContentControl)?.Content is Grid grid
            ? grid.Children.OfType<Button>().FirstOrDefault(b => Equals(b.Tag, entry)) : null;
        static VirtualizingStackPanel? FindPanel(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is VirtualizingStackPanel panel) return panel;
                if (FindPanel(child) is { } found) return found;
            }
            return null;
        }
    }

    private static DockPanel Heading(string title, string hint)
    {
        var row = new DockPanel { Margin = new(8, 4, 8, 8), LastChildFill = true };
        var hintText = new TextBlock { Text = hint, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); DockPanel.SetDock(hintText, Dock.Right);
        var heading = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        row.Children.Add(hintText); row.Children.Add(heading);
        return row;
    }
    private static TextBlock Empty(string text)
    {
        var empty = new TextBlock { Text = text, Margin = new(8, 2, 8, 8), TextWrapping = TextWrapping.Wrap };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return empty;
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

    private sealed record HeadingRow(string Title, string Hint);
    private sealed record EmptyRow(string Text);
    private sealed record ItemsRow(EmoticonEntry[] Entries);
    private sealed record HiddenRow(EmoticonEntry Entry);

    /// <summary>行列表：条目是行数据，容器在滚动到附近时才由代码生成内容。</summary>
    private sealed class RowList : ItemsControl
    {
        internal Func<object, FrameworkElement> Build { get; set; } = _ => new FrameworkElement();
        protected override DependencyObject GetContainerForItemOverride() => new ContentControl { Focusable = false, IsTabStop = false };
        protected override bool IsItemItsOwnContainerOverride(object item) => false;
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item) => ((ContentControl)element).Content = Build(item);
        protected override void ClearContainerForItemOverride(DependencyObject element, object item) => ((ContentControl)element).Content = null;
        // 让自动化树直接暴露行内的按钮，与原来的面板结构一致。
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
}

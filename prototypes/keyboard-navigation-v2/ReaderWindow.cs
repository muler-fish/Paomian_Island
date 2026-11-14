using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Desktop;

namespace Paomian.KeyboardPrototype;

internal sealed class ReaderWindow : Window
{
    private readonly ReadingRegion _forums = new("版块导航", "Sidebar");
    private readonly Dictionary<string, List<Button>> _forumRows = [];
    private readonly Dictionary<int, Button> _threadRows = [];
    private readonly StackPanel _threads = new();
    private readonly Button _schemeButton = new();
    private readonly TextBlock _instructions = new() { FontSize = 12 };
    private readonly TextBox _jumpBox = new() { Text = "710001", Width = 120, Height = UiMetrics.ControlHeight };
    private readonly ColumnDefinition _forumColumn = new() { MinWidth = 148, MaxWidth = 300 };
    private readonly ColumnDefinition _threadColumn = new() { MinWidth = 236 };
    private string _forum = "综合线";
    private DemoThread? _thread;
    private bool _laidOutColumns;

    internal ReadingRegion ThreadList { get; } = new("串列表", "ListSurface");
    internal ReadingRegion Reader { get; } = new("串阅读区", "Surface", footer: true);
    internal NavigationInput Navigation { get; }
    internal int? CurrentThread => _thread?.Number;
    internal TextBox JumpBox => _jumpBox;

    internal ReaderWindow()
    {
        Title = "泡面岛";
        Width = 1320;
        Height = 860;
        MinWidth = 960;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowPlacement.FitOnStartup(this);

        Navigation = new NavigationInput(this, [_forums, ThreadList, Reader], Scheme.Letters);
        BuildForums();
        BuildToolbars();
        SetForum("综合线", focusList: false);
        OpenThread(DemoContent.Threads[0]);
        UpdateSchemeHeader();

        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Canvas");
        root.RowDefinitions.Add(new() { Height = UiMetrics.MainTitleRow });
        root.RowDefinitions.Add(new());
        root.Children.Add(BuildHeader());
        var columns = BuildColumns();
        Grid.SetRow(columns, 1);
        root.Children.Add(columns);
        Content = root;
        Loaded += (_, _) =>
        {
            if (!_laidOutColumns)
            {
                var wide = ActualWidth >= 1500;
                _forumColumn.Width = new GridLength(wide ? 232 : 188);
                _threadColumn.Width = new GridLength(wide ? 520 : 378);
                _laidOutColumns = true;
            }
            ThreadList.View.Focus();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F8 || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            if (!e.IsRepeat) ToggleScheme();
        };
        var jump = new RoutedCommand();
        InputBindings.Add(new KeyBinding(jump, Key.L, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(jump, (_, _) => { _jumpBox.Focus(); _jumpBox.SelectAll(); }));
    }

    private UIElement BuildHeader()
    {
        var header = new DockPanel { LastChildFill = true, Margin = new(18, 0, 14, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/KeyboardNavigationV2;component/Assets/paomian.png")),
            Width = 22, Height = 22, Margin = new(0, 0, 9, 0)
        });
        brand.Children.Add(new TextBlock { Text = "泡面岛", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        brand.Children.Add(new TextBlock { Text = "X 岛匿名版", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new(10, 0, 0, 0) });
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(controls, Dock.Right);
        _schemeButton.ToolTip = "切换方案 · F8";
        _schemeButton.Margin = new(0, 0, 12, 0);
        _schemeButton.Click += (_, _) => ToggleScheme();
        AutomationProperties.SetAcceleratorKey(_schemeButton, "F8");
        controls.Children.Add(_schemeButton);

        AutomationProperties.SetName(_jumpBox, "串号");
        _jumpBox.ToolTip = "输入串号 · Ctrl+L";
        _jumpBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            Jump();
            e.Handled = true;
        };
        controls.Children.Add(_jumpBox);
        var jump = PlainButton("跳转", Jump);
        jump.Margin = new(5, 0, 0, 0);
        controls.Children.Add(jump);
        header.Children.Add(controls);

        _instructions.VerticalAlignment = VerticalAlignment.Center;
        _instructions.HorizontalAlignment = HorizontalAlignment.Center;
        _instructions.TextTrimming = TextTrimming.CharacterEllipsis;
        _instructions.Margin = new(8, 0, 8, 0);
        _instructions.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        header.Children.Add(_instructions);
        var border = new Border { Child = header, BorderThickness = new(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private Grid BuildColumns()
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(_forumColumn);
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(_threadColumn);
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new() { MinWidth = 280 });
        columns.Children.Add(_forums.Root);
        Grid.SetColumn(ThreadList.Root, 2);
        columns.Children.Add(ThreadList.Root);
        Grid.SetColumn(Reader.Root, 4);
        columns.Children.Add(Reader.Root);
        AddSplitter(1, "调整版块导航栏宽度");
        AddSplitter(3, "调整串列表区宽度");
        return columns;

        void AddSplitter(int column, string name)
        {
            var line = new Border();
            line.SetResourceReference(Border.BackgroundProperty, "Line");
            Grid.SetColumn(line, column);
            columns.Children.Add(line);
            var splitter = new GridSplitter
            {
                Width = 9, Margin = new Thickness(-4, 0, -4, 0), HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch, ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = Brushes.Transparent
            };
            AutomationProperties.SetName(splitter, name);
            Grid.SetColumn(splitter, column);
            Panel.SetZIndex(splitter, 2);
            columns.Children.Add(splitter);
        }
    }

    private void BuildForums()
    {
        var menu = new StackPanel { Margin = new Thickness(10, 8, 10, 12) };
        menu.Children.Add(ForumRow("综合线"));
        menu.Children.Add(PostView.Muted("收藏版块", new Thickness(8, 14, 0, 4)));
        menu.Children.Add(ForumRow("日常"));
        var (allButton, all) = Group(menu, "全部版块", expanded: true);
        allButton.Margin = new Thickness(0, 12, 0, 0);
        var (_, chat) = Group(all, "综合讨论", expanded: true);
        chat.Children.Add(ForumRow("日常"));
        chat.Children.Add(ForumRow("读书"));
        var (_, interests) = Group(all, "兴趣交流", expanded: true);
        interests.Children.Add(ForumRow("摄影"));
        interests.Children.Add(ForumRow("数码"));
        _forums.View.Content = menu;

        (Button Toggle, StackPanel Items) Group(Panel parent, string name, bool expanded)
        {
            var items = new StackPanel { Margin = new Thickness(12, 0, 0, 2), Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
            Button toggle = null!;
            toggle = _forums.ActionButton(name, () =>
            {
                items.Visibility = items.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                UpdateToggle();
            }, "RowButton");
            void UpdateToggle()
            {
                var isOpen = items.Visibility == Visibility.Visible;
                toggle.Content = (isOpen ? "▾  " : "▸  ") + name;
                AutomationProperties.SetName(toggle, (isOpen ? "收起" : "展开") + name);
            }
            UpdateToggle();
            parent.Children.Add(toggle);
            parent.Children.Add(items);
            return (toggle, items);
        }

        Button ForumRow(string name)
        {
            var button = _forums.ActionButton(name, () => SetForum(name), "RowButton");
            if (!_forumRows.TryGetValue(name, out var rows)) _forumRows[name] = rows = [];
            rows.Add(button);
            return button;
        }
    }

    private void BuildToolbars()
    {
        ThreadList.View.Content = _threads;
        ThreadList.ToolbarButton("版规", () => Navigation.ShowModal(new PreviewDialog("版规", Navigation.Scheme,
            text: "实验数据中的版块规则：发言请留在对应版块。这里可以试用 Tab 和 Esc，再返回列表原位置。")));
        ThreadList.ToolbarButton("发串", () => Navigation.ShowModal(new ComposeDialog("发串")), primary: true);
        Reader.ToolbarButton("回应", () => Navigation.ShowModal(new ComposeDialog("回应")), primary: true);
        Reader.ToolbarButton("回到顶部", () => Reader.View.ScrollToTop(), bottom: true);
        Reader.ToolbarButton("向下翻页", () => Reader.Scroll.Page(1), bottom: true);
    }

    private void SetForum(string forum, bool focusList = true)
    {
        _forum = forum;
        foreach (var (name, buttons) in _forumRows)
            foreach (var button in buttons) button.Tag = name == forum ? "Current" : null;
        _threads.Children.Clear();
        _threadRows.Clear();
        ThreadList.ClearScrollTargets();
        ThreadList.Heading.Text = forum;
        foreach (var thread in DemoContent.Threads.Where(thread => forum == "综合线" || thread.Forum == forum))
        {
            var button = ThreadList.ActionButton(thread.Title, () => OpenThread(thread, focusReader: true), "RowButton");
            button.Padding = new Thickness(16, 11, 14, 11);
            var summary = new StackPanel();
            summary.Children.Add(PostView.Muted($"{thread.Forum}  ·  No.{thread.Number}  ·  {thread.Posts.Length - 1} 条回应"));
            summary.Children.Add(new TextBlock { Text = thread.Title, FontWeight = FontWeights.SemiBold, Margin = new(0, 4, 0, 3) });
            summary.Children.Add(new TextBlock { Text = thread.Preview, TextWrapping = TextWrapping.Wrap, LineHeight = 21 });
            button.Content = summary;
            button.Tag = thread == _thread ? "Current" : null;
            _threadRows.Add(thread.Number, button);
            var row = new Border { Child = button, BorderThickness = new Thickness(0, 0, 0, 1) };
            row.SetResourceReference(Border.BorderBrushProperty, "Hairline");
            _threads.Children.Add(row);
        }
        ThreadList.View.ScrollToTop();
        if (focusList) ThreadList.View.Focus();
    }

    private void OpenThread(DemoThread thread, bool focusReader = false)
    {
        if (_thread != thread)
        {
            if (_thread is not null && _threadRows.TryGetValue(_thread.Number, out var old)) old.Tag = null;
            _thread = thread;
            if (_threadRows.TryGetValue(thread.Number, out var row)) row.Tag = "Current";
            Reader.Heading.Text = $"No.{thread.Number}";
            Reader.ClearScrollTargets();
            var posts = new StackPanel { Margin = new Thickness(20, 16, 20, 20), MaxWidth = UiMetrics.ReadingWidth };
            posts.Children.Add(new TextBlock
            {
                Text = thread.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            });
            foreach (var post in thread.Posts)
                posts.Children.Add(PostView.Build(Reader, Navigation, post, mainPost: post.Number == thread.Number));
            Reader.View.Content = posts;
            Reader.View.ScrollToTop();
        }
        if (focusReader) Reader.View.Focus();
    }

    private void Jump()
    {
        var text = _jumpBox.Text.Trim();
        if (text.StartsWith("No.", StringComparison.OrdinalIgnoreCase)) text = text[3..];
        var thread = int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? DemoContent.Threads.FirstOrDefault(item => item.Number == number)
            : null;
        if (thread is null)
        {
            Navigation.ShowModal(new PreviewDialog("跳转到串", Navigation.Scheme, text: "请输入模拟数据中的串号，例如 710001。"));
            return;
        }
        if (_forum != "综合线" && _forum != thread.Forum) SetForum("综合线", focusList: false);
        OpenThread(thread, focusReader: true);
    }

    private void ToggleScheme()
    {
        Navigation.SetScheme(Navigation.Scheme == Scheme.Letters ? Scheme.Enter : Scheme.Letters);
        UpdateSchemeHeader();
    }

    private void UpdateSchemeHeader()
    {
        var letters = Navigation.Scheme == Scheme.Letters;
        _schemeButton.Content = (letters ? "A · 字母提示" : "B · Enter 选择") + "  F8";
        _instructions.Text = letters ? "F6 切栏 · F 显示目标 · Tab 访问控件" : "F6 切栏 · Enter 选择 · Tab 访问控件";
    }

    private static Button PlainButton(string name, Action action)
    {
        var button = new Button { Content = name };
        button.Click += (_, _) => action();
        return button;
    }
}

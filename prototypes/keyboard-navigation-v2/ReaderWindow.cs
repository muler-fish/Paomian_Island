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
    private readonly StackPanel _headerControls = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly RegionPane _forumPane = new("版块导航", "Sidebar");
    private readonly Dictionary<int, Button> _threadRows = [];
    private readonly StackPanel _threads = new();
    private readonly TextBox _jumpBox = new() { Text = "710001", Width = 120, Height = UiMetrics.ControlHeight };
    private readonly ColumnDefinition _forumColumn = new() { MinWidth = 148, MaxWidth = 300 };
    private readonly ColumnDefinition _threadColumn = new() { MinWidth = 236 };
    private string _forum = "综合线";
    private DemoThread? _thread;
    private bool _laidOutColumns;

    internal ForumTree Forums { get; } = new();
    internal ReadingRegion ThreadList { get; } = new("串列表", "ListSurface");
    internal ReadingRegion Reader { get; } = new("串阅读区", "Surface", footer: true);
    internal NavigationInput Navigation { get; }
    internal int? CurrentThread => _thread?.Number;
    internal TextBox JumpBox => _jumpBox;
    internal bool ShowsForumFocus => _forumPane.ShowsFocus;

    internal ReaderWindow()
    {
        Title = "泡面岛";
        Width = 1320;
        Height = 860;
        MinWidth = 960;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowPlacement.FitOnStartup(this);

        Navigation = new NavigationInput(this, [ThreadList, Reader], Forums);
        BuildForums();
        BuildToolbars();
        SetForum("综合线", focusList: false);
        OpenThread(DemoContent.Threads[0]);

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

        var jump = new RoutedCommand();
        InputBindings.Add(new KeyBinding(jump, Key.L, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(jump, (_, _) => { _jumpBox.Focus(); _jumpBox.SelectAll(); }));
        ReadingRegion? returnTo = null;
        UIElement? previousBrowsingFocus = null;
        _headerControls.GotKeyboardFocus += (_, e) =>
        {
            if (e.OldFocus is UIElement old && !old.IsDescendantOf(_headerControls) && Window.GetWindow(old) == this)
            {
                previousBrowsingFocus = old;
                returnTo = old.IsDescendantOf(Reader.Root) ? Reader : ThreadList;
            }
        };
        _headerControls.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            if (!e.IsRepeat && (previousBrowsingFocus is not { IsVisible: true, IsEnabled: true, Focusable: true } || !previousBrowsingFocus.Focus()))
                (returnTo ?? ThreadList).View.Focus();
        };
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
        var name = new TextBlock { Text = "泡面岛", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        brand.Children.Add(name);
        brand.Children.Add(PostView.Muted("X 岛匿名版", new Thickness(10, 0, 0, 0)));
        DockPanel.SetDock(brand, Dock.Left);
        header.Children.Add(brand);

        DockPanel.SetDock(_headerControls, Dock.Right);

        AutomationProperties.SetName(_jumpBox, "串号");
        _jumpBox.ToolTip = "输入串号 · Ctrl+L";
        _jumpBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            Jump();
            e.Handled = true;
        };
        _headerControls.Children.Add(_jumpBox);
        var jump = PlainButton("跳转", Jump);
        jump.Margin = new(5, 0, 0, 0);
        _headerControls.Children.Add(jump);
        header.Children.Add(_headerControls);

        var instructions = PostView.Muted("Tab 切栏 · ↑ ↓ 浏览 · F 显示操作", new Thickness(8, 0, 8, 0));
        instructions.HorizontalAlignment = HorizontalAlignment.Center;
        instructions.TextTrimming = TextTrimming.CharacterEllipsis;
        header.Children.Add(instructions);
        var border = new Border { Child = header, BorderThickness = new(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "Line");
        return border;
    }

    private Grid BuildColumns()
    {
        var columns = new Grid();
        KeyboardNavigation.SetDirectionalNavigation(columns, KeyboardNavigationMode.None);
        columns.ColumnDefinitions.Add(_forumColumn);
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(_threadColumn);
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new() { MinWidth = 280 });
        _forumPane.Root.Children.Add(Forums);
        Grid.SetRow(Forums, 1);
        columns.Children.Add(_forumPane.Root);
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
                Background = Brushes.Transparent, Focusable = false, IsTabStop = false
            };
            KeyboardNavigation.SetTabNavigation(splitter, KeyboardNavigationMode.None);
            KeyboardNavigation.SetDirectionalNavigation(splitter, KeyboardNavigationMode.None);
            AutomationProperties.SetName(splitter, name);
            Grid.SetColumn(splitter, column);
            Panel.SetZIndex(splitter, 2);
            columns.Children.Add(splitter);
        }
    }

    private void BuildForums()
    {
        var home = Forums.AddItem(Forums, "综合线", () => SetForum("综合线"));
        var favorites = Forums.AddItem(Forums, "收藏版块", expanded: true);
        Forums.AddItem(favorites, "日常", () => SetForum("日常"));
        var all = Forums.AddItem(Forums, "全部版块", expanded: true);
        var chat = Forums.AddItem(all, "综合讨论", expanded: true);
        Forums.AddItem(chat, "日常", () => SetForum("日常"));
        Forums.AddItem(chat, "读书", () => SetForum("读书"));
        var interests = Forums.AddItem(all, "兴趣交流", expanded: true);
        Forums.AddItem(interests, "摄影", () => SetForum("摄影"));
        Forums.AddItem(interests, "数码", () => SetForum("数码"));
        Forums.AddItem(Forums, "设置", () => Navigation.ShowModal(new SettingsDialog()));
        Forums.AddItem(Forums, "饼干管理", () => Navigation.ShowModal(new FormDialog("饼干管理", PostView.Paragraph("暂无模拟饼干。"))));
        Forums.AddItem(Forums, "草稿箱", () => Navigation.ShowModal(new FormDialog("草稿箱", PostView.Paragraph("暂无模拟草稿。"))));
        home.IsSelected = true;
    }

    private void BuildToolbars()
    {
        ThreadList.View.Content = _threads;
        ThreadList.ToolbarButton("刷新", () =>
        {
            var offset = ThreadList.View.VerticalOffset;
            LoadForum(_forum);
            ThreadList.View.ScrollToVerticalOffset(offset);
            ThreadList.Feedback("列表已刷新");
        });
        ThreadList.ToolbarButton("版规", () => Navigation.ShowModal(new PreviewDialog("版规",
            text: "实验数据中的版块规则：发言请留在对应版块。这里可以试用 Tab 和 Esc，再返回列表原位置。")));
        ThreadList.ToolbarButton("发串", () => Navigation.ShowModal(new ComposeDialog("发串")), primary: true);
        Reader.ToolbarButton("回应", () => Navigation.ShowModal(new ComposeDialog("回应")), primary: true);
        var top = Reader.ToolbarButton("回到顶部", () =>
        {
            Reader.View.Focus();
            Reader.View.ScrollToTop();
        }, bottom: true);
        var next = Reader.ToolbarButton("向下翻页", () =>
        {
            Reader.View.Focus();
            Reader.Scroll.Page(1);
        }, bottom: true);
        void UpdatePaging()
        {
            top.IsEnabled = Reader.View.VerticalOffset > .01;
            next.IsEnabled = Reader.View.VerticalOffset < Reader.View.ScrollableHeight - .01;
        }
        Reader.View.ScrollChanged += (_, _) => UpdatePaging();
        UpdatePaging();
    }

    private void SetForum(string forum, bool focusList = true)
    {
        if (_threads.Children.Count > 0 && _forum == forum)
        {
            if (focusList) ThreadList.View.Focus();
            return;
        }
        LoadForum(forum);
        ThreadList.View.ScrollToTop();
        if (focusList) ThreadList.View.Focus();
    }

    private void LoadForum(string forum)
    {
        _forum = forum;
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
            var title = new TextBlock
            {
                Text = thread.Title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14),
                TextWrapping = TextWrapping.Wrap
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            posts.Children.Add(title);
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
            Navigation.ShowModal(new PreviewDialog("跳转到串", text: "请输入模拟数据中的串号，例如 710001。"));
            return;
        }
        if (_forum != "综合线" && _forum != thread.Forum) SetForum("综合线", focusList: false);
        OpenThread(thread, focusReader: true);
    }

    private static Button PlainButton(string name, Action action)
    {
        var button = new Button { Content = name };
        button.Click += (_, _) => action();
        return button;
    }
}

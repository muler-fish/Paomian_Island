using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Paomian.KeyboardPrototype;

internal static class PrototypeChecks
{
    internal static async Task RunAsync(ReaderWindow main, string output)
    {
        Directory.CreateDirectory(output);
        await Idle();
        var actualDpi = VisualTreeHelper.GetDpi(main).PixelsPerInchX;
        var list = main.ThreadList;
        var reader = main.Reader;
        var nav = main.Navigation;
        Require(main.IsActive && list.View.IsKeyboardFocused, "串列表没有初始键盘焦点");
        var splitters = Children<GridSplitter>(main).ToArray();
        Require(splitters.Length == 2, "分栏数量错误");
        var originalWidths = splitters.SelectMany(AdjacentWidths).ToArray();
        var splitterFocusCount = 0;
        main.AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) =>
        {
            if (e.NewFocus is GridSplitter) splitterFocusCount++;
        }), true);

        await CheckRegionsAndTree(main, output);
        await CheckForms(main, output);

        KeyStroke(Key.F);
        var siblings = reader.VisibleTargets().Select(item => item.Target.Button).ToHashSet();
        Require(nav.Hints.Count > 2 && nav.Hints.All(hint => !siblings.Contains(hint.Item.Target.Button)), "标签显示到了其他栏");
        var disabled = ButtonNamed(main, "版规");
        KeyStroke(Key.Escape);
        disabled.IsEnabled = false;
        KeyStroke(Key.F);
        Require(nav.Hints.All(hint => hint.Item.Target.Button != disabled), "禁用按钮获得标签");
        disabled.IsEnabled = true;
        KeyStroke(Key.Escape);
        KeyStroke(Key.F);
        PickHint(nav, "搪瓷杯还在用吗");
        await Idle();
        Require(main.CurrentThread == 710002 && reader.View.IsKeyboardFocused && nav.Hints.Count == 0, "字母提示未打开串并激活阅读区");
        Cycle(main, backwards: true);
        KeyStroke(Key.F);
        PickHint(nav, "沿河步道随手记");
        await Idle();
        Require(main.CurrentThread == DemoContent.LongThread && reader.View.IsKeyboardFocused, "字母提示未返回样例长串");

        var response = ButtonNamed(main, "回应");
        response.Focus();
        KeyStroke(Key.Left);
        KeyStroke(Key.Right);
        Require(response.IsKeyboardFocused && reader.ShowsFocus && response.Template.FindName("FocusOutline", response) is FrameworkElement { IsVisible: true }, "按钮焦点与视觉标记不一致");
        KeyStroke(Key.F);
        Require(response.IsKeyboardFocused && nav.Hints.Any(hint => hint.Item.Target.Button == response) && !Application.Current.Windows.OfType<ComposeDialog>().Any(), "按 F 改变焦点或触发原按钮");
        KeyStroke(Key.Enter);
        Require(nav.Hints.Count > 0 && response.IsKeyboardFocused, "提示中的 Enter 激活了焦点按钮");
        KeyStroke(Key.Escape);
        await AssertScrollTaps(main, response);

        var longPost = Post(main, 710103);
        var scroll = await MeasureScroll(reader.View, longPost);
        var dense = Post(main, 710102);
        await Position(reader.View, dense, 5);
        KeyStroke(Key.F);
        var hintCount = nav.Hints.Count;
        CheckHintGeometry(main, reader, requireMany: true);
        var denseOffset = reader.View.VerticalOffset;
        var boxes = nav.Hints.ToDictionary(label => label.Text, label => label.Bounds);
        var sample = nav.Hints[0].Text;
        KeyStroke(Letter(sample[0]));
        Require(reader.HintBounds.Count < hintCount && reader.HintBounds.All(boxes.Values.Contains) && Within(reader.View.VerticalOffset, denseOffset), "筛选前缀后标签移动或视口改变");
        Capture(main, Path.Combine(output, "light-prefix.png"));
        KeyStroke(Key.Z);
        Require(reader.HintBounds.Count < hintCount, "无效前缀误触发操作");
        KeyStroke(Key.Back);
        Require(reader.HintBounds.Count == hintCount, "Backspace 未恢复候选");
        KeyStroke(Key.Escape);
        Require(reader.HintBounds.Count == 0 && Within(reader.View.VerticalOffset, denseOffset), "Esc 未恢复提示前位置");

        foreach (var action in new[] { "沿河路线图", "河湾示意图" })
        {
            KeyStroke(Key.F);
            PickHint(nav, action);
            var preview = await Active<PreviewDialog>();
            Require(preview.Owner == main && preview.Title == action && preview.Region.View.IsKeyboardFocused, "链接或图片未打开并聚焦预览");
            await TakeHintClose(preview);
            Require(main.IsActive && reader.View.IsKeyboardFocused && Within(reader.View.VerticalOffset, denseOffset), "预览关闭未恢复阅读位置");
        }
        var background = new Window { Title = "验证用后台窗口", Width = 320, Height = 180 };
        background.Show();
        try
        {
            await Idle();
            background.Activate();
            Require(background.IsActive, "无法激活验证用 WPF 窗口");
            main.Activate();
            await Idle();
            Require(main.IsActive && reader.View.IsKeyboardFocused, "无法通过 WPF 恢复主窗焦点以开展测试");
            await CheckNestedQuotes(main, longPost, output);
            Require(!background.IsActive, "逐级关窗后激活了后台窗口");
        }
        finally { background.Close(); }
        await CheckEditorAndJump(main, output);

        Require(splitterFocusCount == 0 && splitters.SelectMany(AdjacentWidths).Zip(originalWidths, Within).All(equal => equal), "键盘导致分栏拖柄获取焦点或宽度改变");
        foreach (var splitter in splitters)
        {
            Require(!splitter.Focusable && !splitter.IsTabStop && !splitter.Focus(), "拖柄仍可获取键盘焦点");
            Require(splitter.IsHitTestVisible && main.InputHitTest(splitter.TranslatePoint(new Point(splitter.ActualWidth / 2, 20), main)) is DependencyObject hit &&
                (ReferenceEquals(splitter, hit) || splitter.IsAncestorOf(hit)), "鼠标无法命中分隔线");
            var before = AdjacentWidths(splitter);
            splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            splitter.RaiseEvent(new DragDeltaEventArgs(17, 0) { RoutedEvent = Thumb.DragDeltaEvent });
            splitter.RaiseEvent(new DragCompletedEventArgs(17, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            await Idle();
            var after = AdjacentWidths(splitter);
            Require(Math.Abs(after[0] - before[0]) >= 10 && splitterFocusCount == 0,
                $"WPF 分栏拖动路由未调整宽度或抢占焦点（{before[0]:F1} → {after[0]:F1} DIP）");
        }

        var layouts = await CheckLayouts(main, output);
        var report = new
        {
            ActualDpi = actualDpi,
            Passed = new[]
            {
                "Tab 与 Shift+Tab 循环三栏，活动栏提示和偏移恢复",
                "版块树可见项、键盘与鼠标开合、模拟功能窗口及原位返回",
                "中右栏实际滚动、原生工具焦点和分栏拖动路由",
                "当前栏 F 候选、禁用项、密集前缀、串与上下工具",
                "图片、链接与三级引用的直接归属和逐级关闭",
                "串号和编辑器的原生 Tab、换行、撤销与文本合成",
                "主题及尺寸/DPI 下的焦点反馈与标签避让"
            },
            SplitterUnexpectedFocus = splitterFocusCount,
            InitialDenseHintCount = hintCount,
            Scroll = scroll,
            Layouts = layouts
        };
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private static async Task CheckRegionsAndTree(ReaderWindow main, string output)
    {
        var list = main.ThreadList;
        var reader = main.Reader;
        var tree = main.Forums;
        await SetOffset(list.View, 45);
        await SetOffset(reader.View, 90);
        var listOffset = list.View.VerticalOffset;
        var readOffset = reader.View.VerticalOffset;
        var thread = main.CurrentThread;
        Cycle(main);
        Require(reader.View.IsKeyboardFocused && reader.ShowsFocus && !list.ShowsFocus, "Tab 未激活右栏");
        Cycle(main);
        Require(tree.IsKeyboardFocusWithin && main.ShowsForumFocus && !reader.ShowsFocus, "Tab 未循环回左栏");
        Require((tree.SelectedItem as TreeViewItem)?.Header as string == "综合线", "左栏初始导航项错误");
        Cycle(main);
        Require(list.View.IsKeyboardFocused && list.ShowsFocus && !main.ShowsForumFocus, "Tab 未返回中栏");
        Cycle(main, backwards: true);
        Require(tree.IsKeyboardFocusWithin && main.ShowsForumFocus, "Shift+Tab 未到左栏");
        Cycle(main, backwards: true);
        Require(reader.View.IsKeyboardFocused && reader.ShowsFocus, "Shift+Tab 未从左栏回右栏");
        Cycle(main, backwards: true);
        Require(list.View.IsKeyboardFocused && main.CurrentThread == thread && Within(list.View.VerticalOffset, listOffset) && Within(reader.View.VerticalOffset, readOffset), "三栏切换改变串或视口");
        Cycle(main, backwards: true);

        KeyStroke(Key.Down);
        var favorites = Selected(tree, "收藏版块");
        KeyStroke(Key.Right);
        Selected(tree, "日常");
        KeyStroke(Key.Left);
        Require(Selected(tree, "收藏版块") == favorites, "左键未回到上一级");
        KeyStroke(Key.Left);
        Require(!favorites.IsExpanded, "左键未收起分组");
        KeyStroke(Key.Down);
        var all = Selected(tree, "全部版块");
        KeyStroke(Key.Right);
        Selected(tree, "综合讨论");
        KeyStroke(Key.Right);
        Selected(tree, "日常");
        KeyStroke(Key.Down);
        Selected(tree, "读书");
        KeyStroke(Key.Down);
        var interests = Selected(tree, "兴趣交流");
        KeyStroke(Key.Right);
        Selected(tree, "摄影");
        await Idle();
        var selected = (TreeViewItem)tree.SelectedItem;
        var treeOffset = tree.VerticalOffset;
        Cycle(main);
        Cycle(main, backwards: true);
        Require(selected.IsKeyboardFocused && Within(tree.VerticalOffset, treeOffset), "切栏未保留树的位置");
        KeyStroke(Key.Enter);
        await Idle();
        Require(list.View.IsKeyboardFocused && list.Heading.Text == "摄影" && main.CurrentThread == thread, "版块动作未打开列表或提前换串");

        Cycle(main, backwards: true);
        Require(selected.IsKeyboardFocused, "返回左栏后丢失摄影选择");
        await InvokeToggle(interests);
        Require(Selected(tree, "兴趣交流") == interests && !interests.IsExpanded && !selected.IsVisible, "收起树分支后还停在不可见子项");
        KeyStroke(Key.Down);
        Selected(tree, "设置");
        KeyStroke(Key.Up);
        KeyStroke(Key.Left);
        Require(Selected(tree, "全部版块") == all, "左键未回到全部版块");
        KeyStroke(Key.Up);
        KeyStroke(Key.Up);
        Selected(tree, "综合线");
        Capture(main, Path.Combine(output, "light-tree.png"));
        KeyStroke(Key.Enter);
        await Idle();
        Require(list.View.IsKeyboardFocused && list.Heading.Text == "综合线", "综合线无法进入列表");

        KeyStroke(Key.F);
        Require(main.Navigation.Hints.Count > 0, "鼠标与键盘交替检查前没有提示");
        var header = (FrameworkElement)all.Template.FindName("PART_Header", all);
        header.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent, Source = header
        });
        Require(main.Navigation.Hints.Count == 0 && Selected(tree, "全部版块") == all && main.ShowsForumFocus, "鼠标按下后没有切换到左栏树");
        KeyStroke(Key.Down);
        Selected(tree, "综合讨论");
        Cycle(main);
    }

    private static async Task CheckForms(ReaderWindow main, string output)
    {
        var tree = main.Forums;
        var app = (App)Application.Current;
        Cycle(main, backwards: true);
        for (var i = 0; i < 12 && (tree.SelectedItem as TreeViewItem)?.Header as string != "设置"; i++) KeyStroke(Key.Down);
        var settings = Selected(tree, "设置");
        var offset = tree.VerticalOffset;
        KeyStroke(Key.Enter);
        var form = await Active<SettingsDialog>();
        Require(Keyboard.FocusedElement is CheckBox toggle && toggle.IsKeyboardFocused && toggle.ActualHeight <= 38 && toggle.FocusVisualStyle is not null, "设置初始焦点或焦点尺寸异常");
        MoveTab();
        Require(ButtonNamed(form, "完成").IsKeyboardFocused, "设置内 Tab 未遍历控件");
        MoveTab(backwards: true);
        KeyStroke(Key.Space);
        KeyStroke(Key.Space, up: true);
        Require(app.IsDark, "设置复选框不能启用深色");
        Capture(form, Path.Combine(output, "dark-form.png"));
        KeyStroke(Key.Escape);
        await Idle();
        Require(settings.IsKeyboardFocused && Within(tree.VerticalOffset, offset) && main.ShowsForumFocus, "设置完成后树焦点或偏移丢失");

        foreach (var title in new[] { "饼干管理", "草稿箱" })
        {
            KeyStroke(Key.Down);
            var row = Selected(tree, title);
            KeyStroke(Key.Enter);
            var popup = await Active<FormDialog>();
            Require(popup.Title == title && popup.Owner == main && ButtonNamed(popup, "完成").IsKeyboardFocused, "模拟功能窗无初始焦点");
            KeyStroke(Key.Enter);
            await Idle();
            Require(row.IsKeyboardFocused, "功能窗关闭后没有回到原导航项");
        }
        app.SetTheme(dark: false);
        Cycle(main);
    }

    private static async Task AssertScrollTaps(ReaderWindow main, Button response)
    {
        var reader = main.Reader;
        var list = main.ThreadList;
        var offset = reader.View.VerticalOffset;
        KeyStroke(Key.Down);
        KeyStroke(Key.Down, up: true);
        await Task.Delay(160);
        Require(reader.View.IsKeyboardFocused && !response.IsKeyboardFocused && Within(reader.View.VerticalOffset - offset, 28), "从按钮开始的方向键未恢复当前视口滚动");
        Cycle(main, backwards: true);
        offset = list.View.VerticalOffset;
        KeyStroke(Key.Down);
        KeyStroke(Key.Down, up: true);
        await Task.Delay(160);
        Require(list.View.IsKeyboardFocused && Within(list.View.VerticalOffset - offset, 28), "中栏短按滚动错误");
        KeyStroke(Key.F);
        PickHint(main.Navigation, "刷新");
        await Idle();
        Require(Within(list.View.VerticalOffset - offset, 28) && main.CurrentThread == DemoContent.LongThread, "刷新改变位置或当前串");
        KeyStroke(Key.F);
        PickHint(main.Navigation, "版规");
        var rules = await Active<PreviewDialog>();
        Require(rules.Title == "版规" && rules.Region.View.IsKeyboardFocused, "工具栏提示无法打开版规");
        KeyStroke(Key.Escape);
        await Idle();
        Cycle(main);
    }

    private static async Task CheckNestedQuotes(ReaderWindow main, Border longPost, string output)
    {
        var reader = main.Reader;
        var nav = main.Navigation;
        var reference = Children<Button>(longPost).First(button => AutomationProperties.GetName(button) == DemoContent.DensePostFirstQuote);
        await Position(reader.View, reference, 145);
        var readingOffset = reader.View.VerticalOffset;
        KeyStroke(Key.F);
        PickHint(nav, DemoContent.DensePostFirstQuote);
        var first = await Active<PreviewDialog>();
        await SetOffset(first.Region.View, 38);
        var offset = first.Region.View.VerticalOffset;
        KeyStroke(Key.F);
        PickHint(first.Navigation, ">>No.720201");
        var second = await Active<PreviewDialog>();
        Require(second.Owner == first && second.Region.View.IsKeyboardFocused, "子预览 Owner 或初始焦点错误");
        KeyStroke(Key.F);
        PickHint(second.Navigation, ">>No.720301");
        var third = await Active<PreviewDialog>();
        Require(third.Owner == second && third.Region.View.IsKeyboardFocused, "第三层预览没有直接归属");
        Capture(third, Path.Combine(output, "nested-quote.png"));
        KeyStroke(Key.F);
        KeyStroke(Key.Escape);
        Require(third.IsActive && third.Navigation.Hints.Count == 0, "Esc 跳过提示取消，关闭了窗口");
        KeyStroke(Key.Escape);
        await Idle();
        Require(second.IsActive && second.Region.View.IsKeyboardFocused, "第三层关闭后焦点未回第二层");
        MoveTab();
        var target = Keyboard.FocusedElement as Button;
        Require(target is not null && Window.GetWindow(target) == second, "预览 Tab 没有使用窗口自身的焦点顺序");
        KeyStroke(Key.Escape);
        await Idle();
        Require(first.IsActive && first.Region.View.IsKeyboardFocused && Within(first.Region.View.VerticalOffset, offset), "嵌套关闭丢失父引用位置");
        KeyStroke(Key.Escape);
        await Idle();
        Require(main.IsActive && reader.View.IsKeyboardFocused && Within(reader.View.VerticalOffset, readingOffset) && reader.ShowsFocus, "长楼中的位置或活动栏未恢复");
    }

    private static async Task CheckEditorAndJump(ReaderWindow main, string output)
    {
        var response = ButtonNamed(main, "回应");
        var offset = main.Reader.View.VerticalOffset;
        KeyStroke(Key.F);
        PickHint(main.Navigation, "回应");
        var compose = await Active<ComposeDialog>();
        var editor = compose.Editor;
        // 点击式操作由窗口内的 WPF 自动化按钮和实际焦点路由验证。
        Require(editor.IsKeyboardFocused && InputMethod.GetIsInputMethodEnabled(editor), "输入框未聚焦或禁用了输入法");
        Require(!KeyStroke(Key.F, previewOnly: true) && !KeyStroke(Key.Up, previewOnly: true) && !KeyStroke(Key.Escape, previewOnly: true), "编辑框被接管");
        Require(!WithModifier(ModifierKeys.Control, () => KeyStroke(Key.C, previewOnly: true)) &&
            !WithModifier(ModifierKeys.Control, () => KeyStroke(Key.V, previewOnly: true)), "复制或粘贴组合键被拦截");
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, "fabc 中文"));
        KeyStroke(Key.Enter);
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, "下一行"));
        Require(editor.Text == "fabc 中文\r\n下一行", "WPF 文本合成或 Enter 换行失效");
        var caret = editor.CaretIndex;
        KeyStroke(Key.Left);
        Require(editor.CaretIndex < caret, "编辑器方向键未移动光标");
        WithModifier(ModifierKeys.Control, () => KeyStroke(Key.A));
        Require(editor.SelectedText == editor.Text, "原生全选失败");
        WithModifier(ModifierKeys.Control, () => KeyStroke(Key.Z));
        Require(editor.Text != "fabc 中文\r\n下一行", "原生撤销失败");
        WithModifier(ModifierKeys.Control, () => KeyStroke(Key.Y));
        Require(editor.Text == "fabc 中文\r\n下一行", "原生重做失败");
        Capture(compose, Path.Combine(output, "native-editor.png"));
        MoveTab();
        Require(ButtonNamed(compose, "关闭").IsKeyboardFocused, "编辑器 Tab 未进入操作区");
        MoveTab(backwards: true);
        Require(editor.IsKeyboardFocused, "编辑器 Shift+Tab 未回到原输入框");
        MoveTab();
        KeyStroke(Key.Enter);
        await Idle();
        Require(main.IsActive && main.Reader.View.IsKeyboardFocused && Within(main.Reader.View.VerticalOffset, offset), "编辑窗口关闭后阅读焦点或偏移丢失");
        await Invoke(response);
        compose = await Active<ComposeDialog>();
        MoveTab();
        KeyStroke(Key.Escape);
        await Idle();
        Require(main.IsActive && response.IsKeyboardFocused && Within(main.Reader.View.VerticalOffset, offset), "从工具按钮打开表单后未恢复实际按钮焦点");

        main.Reader.View.Focus();
        KeyStroke(Key.F);
        PickHint(main.Navigation, "向下翻页");
        await Idle();
        Require(main.Reader.View.VerticalOffset > offset, "底部翻页标签没有执行");
        KeyStroke(Key.F);
        PickHint(main.Navigation, "回到顶部");
        await Idle();
        Require(Within(main.Reader.View.VerticalOffset, 0), "底部回到顶部标签没有执行");
        WithModifier(ModifierKeys.Control, () => KeyStroke(Key.L));
        Require(main.JumpBox.IsKeyboardFocused && main.JumpBox.SelectedText == main.JumpBox.Text, "Ctrl+L 命令未聚焦串号框");
        Require(!KeyStroke(Key.F, previewOnly: true) && main.Navigation.Hints.Count == 0, "串号中的字母拦截错误");
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, main.JumpBox, "710002"));
        MoveTab();
        Require(ButtonNamed(main, "跳转").IsKeyboardFocused, "串号框 Tab 没有进入跳转按钮");
        MoveTab(backwards: true);
        Require(main.JumpBox.IsKeyboardFocused, "串号框 Shift+Tab 返还错误");
        KeyStroke(Key.Enter);
        await Idle();
        Require(main.CurrentThread == 710002 && main.Reader.View.IsKeyboardFocused, "串号无法按 Enter 跳转");
        WithModifier(ModifierKeys.Control, () => KeyStroke(Key.L));
        KeyStroke(Key.Escape);
        Require(main.Reader.View.IsKeyboardFocused, "跳转框 Esc 没有返回原栏");
    }

    private static async Task<IReadOnlyList<object>> CheckLayouts(ReaderWindow main, string output)
    {
        var cases = new List<object>();
        var app = (App)Application.Current;
        main.JumpBox.Text = "710001";
        main.JumpBox.Focus();
        KeyStroke(Key.Enter);
        await Idle();
        foreach (var dark in new[] { false, true })
        {
            app.SetTheme(dark);
            foreach (var width in new[] { 1320, 960 })
            foreach (var dpi in new[] { 96, 120, 144 })
            {
                SetDpi(main, dpi, width, 730);
                await Idle();
                Require(Math.Abs(VisualTreeHelper.GetDpi(main).PixelsPerInchX - dpi) <= .1, $"WPF 未采用模拟 DPI {dpi}");
                main.Reader.View.Focus();
                await Position(main.Reader.View, Post(main, 710102), 0);
                KeyStroke(Key.F);
                CheckHintGeometry(main, main.Reader, requireMany: true);
                var name = $"{(dark ? "dark" : "light")}-{width}-{dpi}";
                Capture(main, Path.Combine(output, name + "-hints.png"));
                var count = main.Navigation.Hints.Count;
                KeyStroke(Key.Escape);
                Cycle(main);
                Require(main.ShowsForumFocus && !main.Reader.ShowsFocus, "主题或 DPI 变化后活动栏标记失效");
                if (width == 960) Capture(main, Path.Combine(output, name + "-tree.png"));
                cases.Add(new { Theme = dark ? "Dark" : "Light", WidthDip = width, Dpi = dpi, Hints = count });
            }
        }
        return cases;
    }

    private static async Task<object> MeasureScroll(ScrollViewer view, Border longPost)
    {
        await Position(view, longPost, 0);
        Require(longPost.ActualHeight >= 3 * view.ViewportHeight, "长楼没有超过三屏");
        var timer = Stopwatch.StartNew();
        var changes = new List<double>();
        ScrollChangedEventHandler sample = (_, e) => { if (Math.Abs(e.VerticalChange) > .01) changes.Add(timer.Elapsed.TotalMilliseconds); };
        view.ScrollChanged += sample;
        var before = view.VerticalOffset;
        KeyStroke(Key.Down);
        for (var i = 0; i < 10; i++) { await Task.Delay(75); KeyStroke(Key.Down, repeat: true); }
        KeyStroke(Key.Down, up: true);
        var releaseAt = timer.Elapsed.TotalMilliseconds;
        var releasePosition = view.VerticalOffset;
        await Task.Delay(160);
        view.ScrollChanged -= sample;
        var distance = view.VerticalOffset - before;
        var afterRelease = view.VerticalOffset - releasePosition;
        Require(distance > 265 && changes.Count >= 16 && afterRelease <= 24, $"保持方向键的滚动异常（{distance:F1} DIP，{changes.Count} 次更新，松手后 {afterRelease:F1} DIP）");

        KeyStroke(Key.Down);
        await Task.Delay(150);
        var beforeReverse = view.VerticalOffset;
        timer.Restart();
        KeyStroke(Key.Up);
        while (view.VerticalOffset >= beforeReverse - 1 && timer.ElapsedMilliseconds < 160) await Task.Delay(1);
        var reverseMs = timer.Elapsed.TotalMilliseconds;
        Require(view.VerticalOffset < beforeReverse - 1, "反向键未及时生效");
        KeyStroke(Key.Down, up: true);
        KeyStroke(Key.Up, up: true);
        await Task.Delay(120);
        var beforePage = view.VerticalOffset;
        KeyStroke(Key.PageDown);
        await Idle();
        var pageDistance = view.VerticalOffset - beforePage;
        Require(Within(pageDistance, view.ViewportHeight * .92), "PgDn 未按视口滚动");
        KeyStroke(Key.PageUp);
        await Idle();
        Require(Within(view.VerticalOffset, beforePage), "PgUp 未返回原位置");
        var intervals = changes.Zip(changes.Skip(1), (a, b) => b - a).Order().ToArray();
        return new
        {
            ViewportDip = Math.Round(view.ViewportHeight, 1), LongPostDip = Math.Round(longPost.ActualHeight, 1),
            HoldDurationMs = Math.Round(releaseAt, 1), HoldDistanceDip = Math.Round(distance, 1), OffsetUpdates = changes.Count,
            MedianUpdateIntervalMs = Math.Round(intervals[intervals.Length / 2], 1), AfterReleaseDip = Math.Round(afterRelease, 1),
            ReverseMs = Math.Round(reverseMs, 1), PageDip = Math.Round(pageDistance, 1)
        };
    }

    private static void CheckHintGeometry(ReaderWindow main, ReadingRegion region, bool requireMany)
    {
        var all = main.Navigation.Hints;
        var boxes = region.HintBounds;
        Require(!requireMany || all.Count > 12, "样例视口内多字符标签数量不足");
        Require(all.Count == region.VisibleTargets().Length && all.Select(item => item.Text).Distinct().Count() == all.Count, "可见目标和可执行标签不一致");
        Require(boxes.Count == all.Count && !boxes.Where((box, i) => boxes.Take(i).Any(box.IntersectsWith)).Any(), "标签重叠");
        var frame = new Rect(region.Root.RenderSize);
        Require(boxes.All(frame.Contains), "标签超出当前栏");
    }

    private static async Task TakeHintClose(PreviewDialog window)
    {
        KeyStroke(Key.F);
        PickHint(window.Navigation, "关闭");
        await Idle();
    }

    private static void Cycle(ReaderWindow main, bool backwards = false)
    {
        var handled = backwards ? WithModifier(ModifierKeys.Shift, () => KeyStroke(Key.Tab)) : KeyStroke(Key.Tab);
        Require(handled, "浏览时 Tab 未由三栏循环处理");
    }

    private static void MoveTab(bool backwards = false)
    {
        if (!backwards)
        {
            Require(!KeyStroke(Key.Tab), "表单或预览拦截了原生 Tab");
            return;
        }
        Require(!WithModifier(ModifierKeys.Shift, () => KeyStroke(Key.Tab)), "表单或预览拦截了原生 Shift+Tab");
    }

    private static void PickHint(NavigationInput navigation, string name)
    {
        var hint = navigation.Hints.FirstOrDefault(item => item.Item.Target.Name == name);
        Require(hint is not null, $"找不到可见目标「{name}」");
        foreach (var letter in hint!.Text) KeyStroke(Letter(letter));
    }

    private static Key Letter(char letter) => Key.A + (letter - 'A');

    private static bool WithModifier(ModifierKeys modifier, Func<bool> useKey)
    {
        // 修饰键状态只写入测试线程的输入队列，不注入系统按键。
        var saved = new byte[256];
        Require(GetKeyboardState(saved), "无法读取测试线程键盘状态");
        var updated = (byte[])saved.Clone();
        var shift = modifier == ModifierKeys.Shift;
        updated[shift ? 0x10 : 0x11] |= 0x80;
        updated[shift ? 0xa0 : 0xa2] |= 0x80;
        Require(SetKeyboardState(updated), "无法设置测试线程修饰键");
        try
        {
            Require((Keyboard.Modifiers & modifier) == modifier, $"WPF 未读取测试线程修饰键 {modifier}");
            return useKey();
        }
        finally { _ = SetKeyboardState(saved); }
    }

    private static bool KeyStroke(Key key, bool up = false, bool previewOnly = false, bool repeat = false)
    {
        if (Keyboard.FocusedElement is not UIElement target) throw new InvalidOperationException($"没有 WPF 键盘焦点（{key}）");
        var source = PresentationSource.FromVisual(target);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent
        };
        if (previewOnly) target.RaiseEvent(args);
        else
        {
            if (!repeat && !up) InputManager.Current.ProcessInput(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.None)
            {
                RoutedEvent = Keyboard.PreviewKeyUpEvent
            });
            InputManager.Current.ProcessInput(args);
        }
        return args.Handled;
    }

    private static async Task Invoke(Button button)
    {
        button.Focus();
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)!).Invoke();
        await Idle();
    }

    private static async Task InvokeToggle(TreeViewItem item)
    {
        item.Focus();
        var toggle = Children<ToggleButton>(item).First();
        ((IToggleProvider)new ToggleButtonAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)!).Toggle();
        await Idle();
    }

    private static TreeViewItem Selected(ForumTree tree, string name)
    {
        var item = tree.SelectedItem as TreeViewItem;
        Require(item?.Header as string == name && item is { IsVisible: true, IsKeyboardFocused: true }, $"预期导航项「{name}」，实际「{item?.Header}」");
        return item!;
    }

    private static Button ButtonNamed(Window window, string name) => Children<Button>(window).First(button =>
        (button.Content as string == name || AutomationProperties.GetName(button) == name) && button.IsVisible);

    private static Border Post(ReaderWindow main, int number) => Children<Border>(main.Reader.View).Single(border => border.Tag is int id && id == number);

    private static double[] AdjacentWidths(GridSplitter splitter)
    {
        var grid = (Grid)splitter.Parent;
        var column = Grid.GetColumn(splitter);
        return [grid.ColumnDefinitions[column - 1].ActualWidth, grid.ColumnDefinitions[column + 1].ActualWidth];
    }

    private static async Task<T> Active<T>() where T : Window
    {
        for (var i = 0; i < 22; i++)
        {
            await Idle();
            if (Application.Current.Windows.OfType<T>().SingleOrDefault(window => window.IsActive) is { } active) return active;
        }
        throw new InvalidOperationException($"没有活动的 {typeof(T).Name}");
    }

    private static Task Position(ScrollViewer view, Visual item, double top) =>
        SetOffset(view, item.TransformToVisual(view).Transform(new Point()).Y + view.VerticalOffset - top);

    private static async Task SetOffset(ScrollViewer view, double point)
    {
        view.ScrollToVerticalOffset(point);
        await Idle();
    }

    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }

    private static void SetDpi(Window window, int dpi, int width, int height)
    {
        var rect = new NativeRect { Right = width * dpi / 96, Bottom = height * dpi / 96 };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            Marshal.StructureToPtr(rect, memory, false);
            _ = SendMessage(new WindowInteropHelper(window).Handle, 0x02E0, new nint(dpi << 16 | dpi), memory);
            window.Width = width;
            window.Height = height;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetKeyboardState(byte[] state);

    private static void Capture(Window window, string path)
    {
        window.UpdateLayout();
        var visual = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(visual);
        var size = visual.RenderSize;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * dpi.DpiScaleX), (int)Math.Ceiling(size.Height * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        png.Save(file);
    }

    private static async Task Idle()
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(45);
    }

    private static bool Within(double actual, double expected) => Math.Abs(actual - expected) <= .6;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static partial class KeyboardReadingChecks
{
    internal static int Run(string mode, string directory, Func<ResourceDictionary> resources, Action<Window, string> capture)
    {
        var output = Path.GetFullPath(directory);
        Directory.CreateDirectory(output);
        var app = new Application { Resources = resources(), ShutdownMode = ShutdownMode.OnMainWindowClose };
        App.ApplyTheme("light");
        var fixture = new KeyboardReadingFixture();
        var store = new LocalStore(Path.Combine(output, "profile-" + Guid.NewGuid().ToString("N")));
        var model = new MainViewModel(new XIslandApi(new HttpClient(fixture)), store);
        model.State.AutoCheckUpdates = false;
        model.State.HideSpoilers = true;
        model.State.ReaderImages = ImageModes.Auto;
        model.AppearanceChanged();
        KeyboardReadingFixture.SeedImages(store.Cache);
        var window = new MainWindow(model, _ => throw new InvalidOperationException("离线验收未打开外部网站。"));
        app.MainWindow = window;
        EventHandler? verify = null;
        verify = async (_, _) =>
        {
            window.ContentRendered -= verify;
            try
            {
                await model.InitializeAsync();
                if (mode == "--keyboard-demo")
                {
                    await model.OpenThreadAsync(KeyboardReadingFixture.Thread);
                    Find<FrameworkElement>(window, "ListPane").Focus();
                }
                else
                {
                    if (mode == "--scroll-stability") await CheckStability(window);
                    else await CheckAsync(window, fixture, output, capture);
                    app.Shutdown(0);
                }
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                Console.Error.WriteLine(error);
                app.Shutdown(1);
            }
        };
        window.ContentRendered += verify;
        return app.Run(window);
    }

    private static async Task CheckAsync(MainWindow window, KeyboardReadingFixture fixture, string output, Action<Window, string> capture)
    {
        var navigation = Find<FrameworkElement>(window, "NavigationPane");
        var middle = Find<FrameworkElement>(window, "ListPane");
        var right = Find<FrameworkElement>(window, "ReaderPane");
        var overlay = Find<ReadingKeyboardOverlay>(window, "ReadingKeyboardLayer");
        var list = Find<ListBox>(window, "ThreadList");
        var reader = Find<ListBox>(window, "ReaderList");
        var view = Descendants<ScrollViewer>(list).First();
        await Idle();
        Require(window.IsActive && middle.Focus(), "测试主窗没有活动的中栏焦点");
        Press(Key.Tab);
        Require(right.IsKeyboardFocused && overlay.ActivePane == right, "Tab 没有激活右栏");
        Press(Key.Tab);
        Require(navigation.IsKeyboardFocusWithin && overlay.ActivePane == navigation, "Tab 没有循环到左栏");
        Press(Key.Tab);
        Require(middle.IsKeyboardFocused && overlay.ActivePane == middle, "Tab 没有循环到中栏");
        Press(Key.Tab, ModifierKeys.Shift);
        var group = Descendants<Expander>(navigation).Single(item => Equals(item.Header, "兴趣交流"));
        var header = (ToggleButton)group.Template.FindName("HeaderSite", group);
        for (var i = 0; i < 18 && !header.IsKeyboardFocused; i++) Press(Key.Down);
        Require(header.IsKeyboardFocused && !group.IsExpanded, "方向导航没有按顺序落到折叠分组");
        Press(Key.Right);
        Require(group.IsExpanded && header.IsKeyboardFocused, "右键没有展开分组");
        await Idle();
        Press(Key.Right);
        var forum = (Button)Keyboard.FocusedElement;
        Require(forum.Tag is Forum { Name: "日常" }, "右键没有进入第一个版块");
        Press(Key.Left);
        Require(header.IsKeyboardFocused, "左键没有回到父分组");
        Press(Key.Left);
        await Idle();
        Require(!group.IsExpanded && !forum.IsVisible, "折叠后子项仍可见");
        Press(Key.Right);
        await Idle();
        Press(Key.Right);
        Press(Key.Enter);
        await Idle();
        Require(window.Model.ActiveForum?.Name == "日常", "Enter 未执行真实版块操作");
        capture(window, Path.Combine(output, "navigation.png"));
        Press(Key.Tab);
        Require(middle.IsKeyboardFocused, "左栏之后没有切到中栏");
        Press(Key.Tab, ModifierKeys.Shift);
        Require(forum.IsKeyboardFocused, "左栏记忆没有保留");
        Press(Key.Tab);
        var keyboardNative = Press(Key.Tab, ModifierKeys.Control);
        Require(!keyboardNative && Keyboard.FocusedElement != middle, "Ctrl+Tab 未访问原生焦点顺序");
        middle.Focus();
        var positioned = view.VerticalOffset;
        var listScroll = await Measure(view);
        Require(list.SelectedItem is null && view.VerticalOffset > positioned, "中栏滚动改变了串选中项或未移动");
        var listOffset = view.VerticalOffset;
        Press(Key.Tab);
        Press(Key.Tab, ModifierKeys.Shift);
        Require(middle.IsKeyboardFocused && Near(view.VerticalOffset, listOffset), "中栏切回后跳动");

        if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines != 0)
        {
            view.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
            {
                RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = view
            });
            Require(InertialScroll.IsRunning(list), "滚轮没有启动中栏惯性");
            Press(Key.Tab);
            Require(right.IsKeyboardFocused && !InertialScroll.IsRunning(list), "切栏键未停止旧栏的滚轮惯性");
            Press(Key.Tab, ModifierKeys.Shift);
        }

        Press(Key.F);
        var settings = Find<FrameworkElement>(window, "HomeTimelineButton");
        settings.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent, Source = settings
        });
        Require(overlay.Hints.Count == 0, "鼠标操作未清除键盘提示");
        settings.Focus();
        Press(Key.Down);
        Require(navigation.IsKeyboardFocusWithin && overlay.ActivePane == navigation, "从鼠标侧栏返回方向键时失去栏位");
        Press(Key.Tab);
        Require(middle.IsKeyboardFocused, "键鼠混用后无法切栏");

        Press(Key.F);
        Require(overlay.Hints.Any(h => h.Target.Source == Find<FrameworkElement>(window, "RefreshListButton")) &&
            overlay.Hints.Any(h => h.Target.Source == Find<FrameworkElement>(window, "NewThreadButton")), "缺少真实中栏工具");
        var stale = overlay.Hints.First(h => h.Target.Context is PostCard);
        var refreshCount = fixture.ListRequests;
        Pick(overlay, h => h.Target.Source == Find<FrameworkElement>(window, "RefreshListButton"));
        await Idle();
        Require(fixture.ListRequests > refreshCount && overlay.Hints.Count == 0 && window.Model.Original is null, "刷新命令未执行或列表重置后仍保留旧目标");
        Type(stale.Code);
        Require(window.Model.Original is null, "使用了重置前的串容器");
        Press(Key.Escape);
        view.ScrollToTop();
        await Idle();
        Press(Key.F);
        Pick(overlay, h => h.Target.Context is PostCard card && card.Post.Id == KeyboardReadingFixture.Thread);
        await Idle();
        Require(window.Model.Original?.Id == KeyboardReadingFixture.Thread && right.IsKeyboardFocused, "字母提示没有通过现有选中路径打开真实串");

        var readerView = Descendants<ScrollViewer>(reader).First();
        var dense = await PositionPost(reader, readerView, KeyboardReadingFixture.ImageReply, 0);
        var denseOffset = readerView.VerticalOffset;
        Press(Key.F);
        ValidateHints(overlay, right);
        var count = overlay.Hints.Count;
        Require(count > 12 && overlay.Hints.All(h => h.Code.Length == 2), "密集正文没有得到多字符标签");
        var visibleLinks = overlay.Hints.Count(h => h.Target.Source is Hyperlink);
        Require(visibleLinks >= 16 && overlay.Hints.All(h => h.Target.Source is not Hyperlink link || !LinkText(link).Contains("729999")),
            $"可见链接或剧透过滤错误（{visibleLinks} 个链接）");
        Require(overlay.Hints.Any(h => h.Target.Source is SpoilerSpan) && overlay.Hints.Any(h => h.Target.Source == Find<FrameworkElement>(window, "ReplyButton")) &&
            overlay.Hints.Any(h => h.Target.Source == Find<FrameworkElement>(window, "PageBox")), "缺少剧透或真实工具目标");
        capture(window, Path.Combine(output, "light-hints.png"));
        var prefix = overlay.Hints[0].Code[..1];
        Type(prefix);
        Press(Key.Back);
        Press(Key.Escape);
        Require(overlay.Hints.Count == 0 && Near(denseOffset, readerView.VerticalOffset), "取消提示改变了阅读位置");

        Press(Key.F);
        Pick(overlay, h => h.Target.Source is SpoilerSpan);
        await Idle();
        Press(Key.F);
        Require(overlay.Hints.Any(h => h.Target.Source is Hyperlink link && LinkText(link).Contains("729999")), "揭开后没有重新取得可见链接");
        Pick(overlay, h => h.Target.Source is Hyperlink link && LinkText(link) == "沿河路线图");
        var threadPreview = await Active<ThreadLinkPreviewWindow>();
        Require(threadPreview.Owner == window && overlay.Hints.Count == 0, "链接未走真实串预览路径");
        Press(Key.Escape);
        await Idle();
        Require(window.IsActive && right.IsKeyboardFocused && Near(denseOffset, readerView.VerticalOffset),
            $"关闭串链接预览后焦点或偏移改变 active={window.IsActive} focus={(Keyboard.FocusedElement as FrameworkElement)?.Name} offset={denseOffset:F1}->{readerView.VerticalOffset:F1}");

        Press(Key.F);
        Pick(overlay, h => AutomationProperties.GetName(h.Target.Source).Contains("的图片", StringComparison.Ordinal));
        var image = await Active<ImageWindow>();
        Require(image.Owner == window, "图片标签没有打开真实看图窗");
        Press(Key.Escape);
        await Idle();
        Require(window.IsActive && right.IsKeyboardFocused, "看图窗关闭未恢复阅读区");

        var beforeReference = fixture.ReferenceRequests;
        Press(Key.F);
        Pick(overlay, h => h.Target.Source is Hyperlink link && LinkText(link).Contains("720101"));
        var quote = await Active<TextWindow>();
        Require(quote.Owner == window && fixture.ReferenceRequests == beforeReference + 1, "引用标签未请求真实引用预览");
        var inner = Descendants<PostBody>(quote).Single().Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Hyperlink>().Single();
        inner.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, inner));
        var child = await Active<TextWindow>();
        Require(child.Owner == quote && child != quote, "嵌套引用没有直接父窗");
        Press(Key.Escape);
        await Idle();
        Require(quote.IsActive, "关闭嵌套引用没有恢复父窗");
        Press(Key.Escape);
        await Idle();
        Require(window.IsActive && right.IsKeyboardFocused && Near(denseOffset, readerView.VerticalOffset), "关闭引用没有恢复长串阅读位置");

        // 原控件以同步模态方式打开：先排测试的编辑路径，再触发标签。
        var editing = window.Dispatcher.InvokeAsync(async () =>
        {
            var editorWindow = await Active<ComposerWindow>();
            try
            {
                var text = Descendants<EmojiEditor>(editorWindow).Single();
                text.Focus();
                await Idle();
                Require(!Press(Key.F) && text.IsKeyboardFocused, "编辑器字母 F 被主窗接管");
                await ComposerInputChecks.Type(text, "f中文");
                Require(text.Text.Contains("f中文", StringComparison.Ordinal), "文本合成失败");
                Press(Key.Tab);
                Require(!text.IsKeyboardFocused && editorWindow.IsKeyboardFocusWithin, "输入后的 Tab 未留在表单");
            }
            finally { editorWindow.Close(); }
        }, DispatcherPriority.Background).Task.Unwrap();
        Press(Key.F);
        Pick(overlay, h => h.Target.Source == Find<FrameworkElement>(window, "ReplyButton"));
        await editing;
        Require(right.IsKeyboardFocused && overlay.Hints.Count == 0, "回应窗口关闭后焦点未归位");

        var body = Descendants<PostBody>(dense).Single();
        Press(Key.F);
        var oldHtml = body.Html;
        body.SetCurrentValue(PostBody.HtmlProperty, oldHtml + "<br>更新后的模拟正文");
        await Idle();
        Require(overlay.Hints.Count == 0, "正文重建后仍保留旧内联目标");
        body.SetCurrentValue(PostBody.HtmlProperty, oldHtml);
        await Idle();
        body.Focus();
        Press(Key.L, ModifierKeys.Control);
        var jump = Find<TextBox>(window, "JumpBox");
        Require(jump.IsKeyboardFocused && !Press(Key.F), "Ctrl+L 冲突或串号框拦截字母");
        Press(Key.Tab);
        Require(!jump.IsKeyboardFocused && !right.IsKeyboardFocused, "串号框没有继续使用原生 Tab");
        right.Focus();
        var tall = await PositionPost(reader, readerView, KeyboardReadingFixture.LongReply, 0);
        Require(tall.ActualHeight >= 3 * readerView.ViewportHeight, "长回应不足三屏");
        var longScroll = await Measure(readerView);
        Require(window.Model.Original?.Id == KeyboardReadingFixture.Thread && right.IsKeyboardFocused, "滚动跳到了其他串");
        var reverseStart = readerView.VerticalOffset;
        var reverse = Stopwatch.StartNew();
        Down(Key.Up);
        await Until(() => readerView.VerticalOffset < reverseStart - 1, "反向没有生效");
        Up(Key.Up);
        var reverseMs = reverse.Elapsed.TotalMilliseconds;
        await Idle();
        var beforePage = readerView.VerticalOffset;
        Press(Key.PageDown);
        await Idle();
        Require(Near(readerView.VerticalOffset - beforePage, readerView.ViewportHeight * .92, 2), "长回应翻动距离异常");
        Press(Key.PageUp);
        await Idle();
        Require(Near(readerView.VerticalOffset, beforePage, 2), "翻回后正文位置异常");
        var longHeight = tall.ActualHeight;
        var actualViewport = readerView.ViewportHeight;

        Press(Key.F);
        Pick(overlay, h => h.Target.Source == Find<FrameworkElement>(window, "PageBox"));
        var pageBox = Find<TextBox>(window, "PageBox");
        Require(pageBox.IsKeyboardFocused && !Press(Key.F), "从 F 进入页码框后未恢复原生输入");
        pageBox.Text = "2";
        Press(Key.Tab);
        Require(Find<Button>(window, "JumpPageButton").IsKeyboardFocused, "页码框不能以原生 Tab 到达跳转按钮");
        Press(Key.Enter);
        await Idle();
        Require(window.Model.ReadingPosts.Any(item => item.Page == 2) && window.Model.ReaderStartsAfterFirstPage, "底部工具未跳到回复页");
        right.Focus();
        readerView.ScrollToTop();
        await Idle();
        Press(Key.Up);
        await Until(() => window.Model.ReadingPosts.Any(item => item.Page == 1), "键盘滚动没有解除跳页后的续载等待");

        var splitter = Find<GridSplitter>(window, "ListSplitter");
        Require(!splitter.IsTabStop && !splitter.Focusable && !splitter.Focus(), "分栏拖柄仍可意外得到焦点");
        var columns = Find<Grid>(window, "ContentColumns");
        var width = columns.ColumnDefinitions[2].ActualWidth;
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(14, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(14, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
        await Idle();
        Require(Math.Abs(width - columns.ColumnDefinitions[2].ActualWidth) > 5 && !splitter.IsKeyboardFocused, "鼠标拖动路由退化或导致焦点跳转");

        var appearances = new List<object>();
        foreach (var theme in new[] { "light", "dark" })
        foreach (var size in new[] { 1180d, 940d })
        {
            App.ApplyTheme(theme);
            window.Width = size;
            await Idle();
            await PositionPost(reader, readerView, KeyboardReadingFixture.ImageReply, 0);
            right.Focus();
            Press(Key.F);
            ValidateHints(overlay, right);
            appearances.Add(new { theme, Width = window.ActualWidth, Dpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX, Labels = overlay.Hints.Count });
            capture(window, Path.Combine(output, $"{theme}-{size:F0}.png"));
            Press(Key.Escape);
        }
        await CheckMouseAndRefresh(window, fixture, list, view, readerView, middle, right, overlay);
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            Passed = true, DenseTargets = count, VisibleInlineLinks = visibleLinks, ListScroll = listScroll, LongScroll = longScroll,
            LongReplyDip = longHeight, ViewportDip = actualViewport, ReverseMs = reverseMs, Appearances = appearances,
            fixture.ListRequests, fixture.ThreadRequests, fixture.ReferenceRequests, ReplyPages = fixture.ReplyPages
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine("PASS: keyboard browsing, pixel scrolling, live hints, editor and owned-window focus");
    }

    private static async Task CheckMouseAndRefresh(MainWindow window, KeyboardReadingFixture fixture, ListBox list,
        ScrollViewer listView, ScrollViewer readerView, FrameworkElement middle, FrameworkElement right, ReadingKeyboardOverlay overlay)
    {
        middle.Focus();
        readerView.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
        {
            RoutedEvent = Mouse.MouseEnterEvent, Source = readerView
        });
        Require(overlay.ActivePane == middle, "鼠标经过右栏时抢走了中栏焦点");
        var listRequests = fixture.ListRequests;
        var threadRequests = fixture.ThreadRequests;
        Press(Key.F5);
        await Until(() => fixture.ListRequests > listRequests, "中栏 F5 没有刷新列表");
        await Idle();
        Require(fixture.ThreadRequests == threadRequests, "中栏 F5 同时刷新了正文");

        readerView.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = readerView
        });
        Require(right.IsKeyboardFocused && overlay.ActivePane == right, "鼠标滚动右栏时没有切换活动栏");
        listRequests = fixture.ListRequests;
        Press(Key.F5);
        await Until(() => fixture.ThreadRequests > threadRequests, "右栏 F5 没有刷新正文");
        await Idle();
        Require(fixture.ListRequests == listRequests, "右栏 F5 刷新了串列表");

        Press(Key.Tab);
        threadRequests = fixture.ThreadRequests;
        Press(Key.F5);
        Find<TextBox>(window, "JumpBox").Focus();
        Press(Key.F5);
        await Idle();
        Require(fixture.ListRequests == listRequests && fixture.ThreadRequests == threadRequests, "左栏或串号框拦截了 F5 刷新");
        right.Focus();
        listView.ScrollToTop();
        await Idle();
        var card = list.Items.OfType<PostCard>().First(c => c.Post.Id != window.Model.ReadingThreadId);
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(card);
        foreach (var route in new[] { Mouse.PreviewMouseDownEvent, Mouse.MouseDownEvent })
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = route, Source = row
            });
        await Until(() => window.Model.Original?.Id == card.Post.Id, "行的鼠标点击没有打开串");
        await Until(() => right.IsKeyboardFocused && overlay.ActivePane == right, "鼠标点击打开串后没有把操作交给右栏");

        listView.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
        {
            RoutedEvent = Mouse.PreviewMouseWheelEvent, Source = listView
        });
        Require(middle.IsKeyboardFocused && overlay.ActivePane == middle, "滚动串列表后没有回到中栏");
        threadRequests = fixture.ThreadRequests;
        Press(Key.R, ModifierKeys.Control);
        await Until(() => fixture.ThreadRequests > threadRequests, "活动栏改变破坏了直接刷新正文的 Ctrl+R");
        await Idle();

        Require(window.Model.Shortcuts.TrySet(ShortcutId.RefreshList, new(Key.F9)) is null, "无法修改现有刷新配置");
        listRequests = fixture.ListRequests;
        Press(Key.F5);
        await Idle();
        Require(fixture.ListRequests == listRequests, "自定义刷新键后仍固定占用 F5");
        Press(Key.F9);
        await Until(() => fixture.ListRequests > listRequests, "自定义键没有执行当前栏刷新");
        window.Model.Shortcuts.Restore(ShortcutId.RefreshList);
    }

    private static async Task<object> Measure(ScrollViewer view)
    {
        var start = view.VerticalOffset;
        Press(Key.Down);
        await Until(() => view.VerticalOffset >= start + 24, "短按没有到达小幅滚动位置");
        await Idle();
        var tap = view.VerticalOffset - start;
        Require(tap is >= 20 and <= 35, $"短按距离异常（{tap:F1} DIP）");
        var frames = new List<double>();
        var timer = Stopwatch.StartNew();
        ScrollChangedEventHandler changed = (_, e) => { if (e.OriginalSource == view && Math.Abs(e.VerticalChange) > .1) frames.Add(timer.Elapsed.TotalMilliseconds); };
        view.ScrollChanged += changed;
        start = view.VerticalOffset;
        Down(Key.Down);
        for (var i = 0; i < 10; i++) { await Task.Delay(75); KeyEvent(Key.Down, Keyboard.PreviewKeyDownEvent); }
        Up(Key.Down);
        var release = view.VerticalOffset;
        await Task.Delay(160);
        view.ScrollChanged -= changed;
        var distance = view.VerticalOffset - start;
        var tail = view.VerticalOffset - release;
        Require(distance > 260 && frames.Count >= 20 && Math.Abs(tail) <= 20, $"连续滚动异常（{distance:F1} DIP，{frames.Count} 次，松键后 {tail:F1} DIP）");
        var intervals = frames.Zip(frames.Skip(1), (first, second) => second - first).Order().ToArray();
        return new { TapDip = tap, HoldDip = distance, Updates = frames.Count, MedianMs = intervals[intervals.Length / 2], ReleaseDip = tail };
    }

    private static async Task<ListBoxItem> PositionPost(ListBox reader, ScrollViewer view, long id, double top)
    {
        var item = reader.Items.OfType<ReadPost>().Single(entry => entry.Post.Id == id);
        reader.ScrollIntoView(item);
        await Idle();
        var row = (ListBoxItem)reader.ItemContainerGenerator.ContainerFromItem(item);
        for (var i = 0; i < 4; i++)
        {
            var difference = row.TranslatePoint(new Point(), view).Y - top;
            if (Math.Abs(difference) < 1) break;
            view.ScrollToVerticalOffset(view.VerticalOffset + difference);
            await Idle();
        }
        return row;
    }

    private static void ValidateHints(ReadingKeyboardOverlay overlay, FrameworkElement pane)
    {
        var bounds = pane.TransformToVisual(overlay).TransformBounds(new Rect(pane.RenderSize));
        var placed = overlay.Hints;
        Require(placed.Count > 0 && placed.All(hint => bounds.Contains(hint.Label)), "标签越出当前栏位或没有出现");
        Require(!placed.Where((hint, index) => placed.Take(index).Any(first => first.Label.IntersectsWith(hint.Label))).Any(), "标签重叠");
    }

    private static void Pick(ReadingKeyboardOverlay overlay, Func<ReadingHint, bool> match)
    {
        var hint = overlay.Hints.FirstOrDefault(match);
        Require(hint is not null, "未发现期望的提示目标");
        Type(hint!.Code);
    }

    private static void Type(string letters) { foreach (var letter in letters) Press(Key.A + (letter - 'A')); }
    private static string LinkText(Hyperlink link) => new TextRange(link.ContentStart, link.ContentEnd).Text;
    private static T Find<T>(Window window, string name) where T : DependencyObject => (T)window.FindName(name);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => ReadingAnchor.Children<T>(root);
    private static bool Near(double first, double second, double tolerance = 1) => Math.Abs(first - second) <= tolerance;
    private static async Task Idle() { await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); await Task.Delay(45); }
    private static async Task Until(Func<bool> check, string error)
    {
        for (var attempt = 0; attempt < 28 && !check(); attempt++) await Task.Delay(15);
        Require(check(), error);
    }
    private static async Task<T> Active<T>() where T : Window
    {
        await Until(() => Application.Current.Windows.OfType<T>().Any(window => window.IsActive), $"找不到活动窗口 {typeof(T).Name}");
        await Idle();
        return Application.Current.Windows.OfType<T>().Single(window => window.IsActive);
    }

    private static bool Press(Key key, ModifierKeys modifier = ModifierKeys.None)
    {
        if (modifier == ModifierKeys.None) { var handled = Down(key); Up(key); return handled; }
        var saved = new byte[256];
        Require(GetKeyboardState(saved), "无法读取 WPF 线程键盘状态");
        var keys = (byte[])saved.Clone();
        keys[modifier == ModifierKeys.Shift ? 0x10 : 0x11] |= 0x80;
        keys[modifier == ModifierKeys.Shift ? 0xa0 : 0xa2] |= 0x80;
        Require(SetKeyboardState(keys), "无法设置 WPF 线程修饰键");
        try { var handled = Down(key); Up(key); return handled; }
        finally { _ = SetKeyboardState(saved); }
    }

    private static bool Down(Key key) => KeyEvent(key, Keyboard.PreviewKeyDownEvent);
    private static void Up(Key key) => KeyEvent(key, Keyboard.PreviewKeyUpEvent);
    private static bool KeyEvent(Key key, RoutedEvent route)
    {
        var source = Keyboard.FocusedElement as DependencyObject;
        var window = source is null ? null : Window.GetWindow(source);
        var input = window is null ? null : PresentationSource.FromVisual(window);
        Require(input is not null, $"没有 WPF 输入窗口（{key}）");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, input, Environment.TickCount, key) { RoutedEvent = route };
        InputManager.Current.ProcessInput(args);
        return args.Handled;
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetKeyboardState([Out] byte[] state);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetKeyboardState(byte[] state);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}

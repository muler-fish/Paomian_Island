using System.Diagnostics;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Paomian.KeyboardPrototype;

internal static class PrototypeChecks
{
    internal static async Task RunAsync(ReaderWindow main, string output)
    {
        Directory.CreateDirectory(output);
        var passed = new List<string>();
        var nav = main.Navigation;
        var list = main.ThreadList;
        var reader = main.Reader;
        await Idle();
        var initialSize = new { main.ActualWidth, main.ActualHeight };
        Require(main.IsActive && list.View.IsKeyboardFocused, "启动时的实际键盘焦点未落在串列表视口");
        KeyStroke(Key.F6);
        Require(reader.View.IsKeyboardFocused, "F6 未进入阅读视口");
        KeyStroke(Key.F6);
        Require(AutomationProperties.GetName((DependencyObject)Keyboard.FocusedElement) == "版块导航", "F6 未循环到导航栏");
        KeyStroke(Key.F6);
        Require(list.View.IsKeyboardFocused, "F6 未返回串列表");
        nav.HandleKey(Key.F6, ModifierKeys.Shift);
        Require(AutomationProperties.GetName((DependencyObject)Keyboard.FocusedElement) == "版块导航", "Shift+F6 未逆向切栏");
        KeyStroke(Key.F);
        PickHint(nav, "兴趣交流");
        await Idle();
        Require(!Children<Button>(main).Any(button => button.IsVisible && AutomationProperties.GetName(button) == "摄影"), "展开组未收起子版块");
        KeyStroke(Key.F);
        PickHint(nav, "兴趣交流");
        await Idle();
        KeyStroke(Key.F);
        PickHint(nav, "摄影");
        await Idle();
        Require(list.Heading.Text == "摄影" && list.View.IsKeyboardFocused, "打开子版块未更新列表与焦点");
        nav.HandleKey(Key.F6, ModifierKeys.Shift);
        KeyStroke(Key.F);
        PickHint(nav, "综合线");
        await Idle();
        passed.Add("实际键盘焦点、F6 双向切栏与多层版块开合");

        Capture(main, Path.Combine(output, "start.png"));
        KeyStroke(Key.F);
        Require(nav.HintLabels.Length > 0, "串列表未显示标签");
        PickHint(nav, "搪瓷杯还在用吗");
        await Idle();
        Require(main.CurrentThread == 710002 && reader.View.IsKeyboardFocused && nav.HintLabels.Length == 0, "方案 A 未打开串并进入阅读视口");
        KeyStroke(Key.F6);
        KeyStroke(Key.F6);
        KeyStroke(Key.F8);
        KeyStroke(Key.Enter);
        Require(nav.SelectedTarget?.Name == "沿河步道随手记", "方案 B 未从首个可见串开始");
        KeyStroke(Key.Down);
        Require(nav.SelectedTarget?.Name == "搪瓷杯还在用吗", "方案 B 未选相邻串");
        KeyStroke(Key.Up);
        KeyStroke(Key.Enter);
        await Idle();
        Require(main.CurrentThread == DemoContent.LongThread && reader.View.IsKeyboardFocused, "方案 B 未打开长串");
        passed.Add("同一串列表的 A/B 选串和切换");

        KeyStroke(Key.F6);
        KeyStroke(Key.F6);
        KeyStroke(Key.Down);
        KeyStroke(Key.Down, up: true);
        await Task.Delay(120);
        var listOffset = list.View.VerticalOffset;
        Require(Within(listOffset, 28) && main.CurrentThread == DemoContent.LongThread, "中栏方向键没有独立滚动");
        KeyStroke(Key.F6);
        Require(reader.View.IsKeyboardFocused && Within(list.View.VerticalOffset, listOffset), "切栏改变了串列表视口");

        var longPost = Children<Border>(reader.View).Single(border => border.Tag is 710103);
        var metrics = await MeasureScroll(reader.View, longPost);
        passed.Add("长楼实际滚动偏移、短按、长按、反向、松手与翻页");

        var densePost = Children<Border>(reader.View).Single(border => border.Tag is 710102);
        await Position(reader.View, densePost, 6);
        var denseOffset = reader.View.VerticalOffset;
        KeyStroke(Key.F8);
        KeyStroke(Key.F);
        var labels = nav.HintLabels;
        Require(labels.Length > 12 && labels.All(label => label.Length == 2) && labels.Distinct().Count() == labels.Length, "密集目标未得到无歧义的双字母标签");
        var boxes = reader.HintBounds;
        Require(boxes.Count == labels.Length && !boxes.Where((box, i) => boxes.Take(i).Any(box.IntersectsWith)).Any(), "密集目标的标签重叠");
        Capture(main, Path.Combine(output, "scheme-a.png"));
        KeyStroke(Letter(labels[0][0]));
        Require(reader.HintBounds.Count < labels.Length && Within(reader.View.VerticalOffset, denseOffset), "前缀筛选后应保留原视口");
        KeyStroke(Key.Z);
        Require(reader.HintBounds.Count < labels.Length, "无效前缀误触发或重置标签");
        KeyStroke(Key.Back);
        Require(reader.HintBounds.Count == labels.Length, "Backspace 未恢复候选");
        var beforeSwitch = reader.View.VerticalOffset;
        KeyStroke(Key.F8);
        Require(nav.Scheme == Scheme.Enter && reader.HintBounds.Count == 0 && Within(reader.View.VerticalOffset, beforeSwitch), "提示时切换方案改变了视口或残留标签");
        KeyStroke(Key.F8);
        KeyStroke(Key.F);
        KeyStroke(Key.Escape);
        Require(reader.HintBounds.Count == 0 && Within(reader.View.VerticalOffset, denseOffset), "Esc 未清理提示");
        foreach (var action in new[] { "沿河路线图", "河湾示意图" })
        {
            KeyStroke(Key.F);
            PickHint(nav, action);
            var preview = await Active<PreviewDialog>();
            Require(preview.Owner == main && preview.Title == action && preview.Region.View.IsKeyboardFocused, "链接或图片缺少预览和明确的初始焦点");
            KeyStroke(Key.Escape);
            await Idle();
            Require(reader.View.IsKeyboardFocused && Within(reader.View.VerticalOffset, denseOffset) && nav.HintLabels.Length == 0, "关闭 A 预览未恢复原视口");
        }
        passed.Add("密集标签避让、两字符前缀、取消、模拟链接和图片");

        KeyStroke(Key.F);
        PickHint(nav, DemoContent.DensePostFirstQuote);
        var firstA = await Active<PreviewDialog>();
        KeyStroke(Key.F);
        PickHint(firstA.Navigation, ">>No.720201");
        var secondA = await Active<PreviewDialog>();
        Require(secondA.Owner == firstA && secondA.Region.View.IsKeyboardFocused, "A 嵌套引用未归直接父窗所有");
        KeyStroke(Key.Escape);
        await Idle();
        Require(firstA.Region.View.IsKeyboardFocused && firstA.Navigation.HintLabels.Length == 0, "A 嵌套返回残留标签或丢失焦点");
        KeyStroke(Key.Escape);
        await Idle();
        Require(reader.View.IsKeyboardFocused && Within(reader.View.VerticalOffset, denseOffset), "A 未回到主窗引用位置");

        var midQuote = Children<Button>(longPost).First(button => AutomationProperties.GetName(button) == DemoContent.DensePostFirstQuote);
        await Position(reader.View, midQuote, 146);
        KeyStroke(Key.F8);
        var readingOffset = reader.View.VerticalOffset;
        KeyStroke(Key.Enter);
        Require(nav.SelectedTarget?.Button == midQuote && reader.View.IsKeyboardFocused && Within(reader.View.VerticalOffset, readingOffset), "在长楼中按 Enter 造成焦点或位置跳跃");
        KeyStroke(Key.Down);
        Require(nav.SelectedTarget?.Name == ">>No.720102" && Within(reader.View.VerticalOffset, readingOffset), "选择相邻引用时视口改变");
        Capture(main, Path.Combine(output, "scheme-b-long-post.png"));
        KeyStroke(Key.Escape);
        Require(nav.SelectedTarget is null && Within(reader.View.VerticalOffset, readingOffset), "Esc 未精确恢复 B 的起始位置");
        KeyStroke(Key.Enter);
        KeyStroke(Key.Enter);
        var firstB = await Active<PreviewDialog>();
        Require(firstB.Owner == main && firstB.Region.View.IsKeyboardFocused, "首层 B 引用初始焦点错误");
        firstB.Region.View.ScrollToVerticalOffset(34);
        await Idle();
        var parentOffset = firstB.Region.View.VerticalOffset;
        KeyStroke(Key.Enter);
        Require(firstB.Navigation.SelectedTarget?.Name == ">>No.720201" && parentOffset > 0, "父引用的中间视口没有可选的嵌套引用");
        KeyStroke(Key.Enter);
        var secondB = await Active<PreviewDialog>();
        KeyStroke(Key.Enter);
        KeyStroke(Key.Enter);
        var thirdB = await Active<PreviewDialog>();
        Require(secondB.Owner == firstB && thirdB.Owner == secondB && thirdB.Title.EndsWith("720301", StringComparison.Ordinal), "B 没有逐级创建三层引用");
        Capture(thirdB, Path.Combine(output, "quote-level-3.png"));
        KeyStroke(Key.Enter);
        Require(thirdB.Navigation.SelectedTarget?.Name == "关闭", "无正文操作时底部关闭按钮不可选");
        thirdB.Navigation.HandleKey(Key.Escape, ModifierKeys.None, repeat: true);
        Require(thirdB.Navigation.SelectedTarget?.Name == "关闭", "Esc 自动重复导致意外退层");
        KeyStroke(Key.Escape);
        Require(thirdB.IsVisible && thirdB.Navigation.SelectedTarget is null, "首个 Esc 应退出第三层的选择");
        KeyStroke(Key.Escape);
        await Idle();
        Require(secondB.Region.View.IsKeyboardFocused && secondB.Navigation.SelectedTarget?.Name == ">>No.720301", "第三层关闭后未恢复第二层目标");
        KeyStroke(Key.Escape);
        Require(secondB.IsVisible && secondB.Navigation.SelectedTarget is null, "第一个 Esc 应先退回自由阅读");
        KeyStroke(Key.Escape);
        await Idle();
        Require(firstB.Region.View.IsKeyboardFocused && firstB.Navigation.SelectedTarget?.Name == ">>No.720201" && Within(firstB.Region.View.VerticalOffset, parentOffset), "第二层关闭后未恢复父窗的焦点、目标和偏移");
        KeyStroke(Key.Escape);
        KeyStroke(Key.Escape);
        await Idle();
        Require(reader.View.IsKeyboardFocused && nav.SelectedTarget?.Button == midQuote && Within(reader.View.VerticalOffset, readingOffset), "首层关闭后未恢复长楼中的选择");
        KeyStroke(Key.Escape);
        passed.Add("A 双层、B 三层引用的直接 Owner、初始焦点、选择状态与视口恢复");

        MoveTab(FocusNavigationDirection.Previous);
        var response = Keyboard.FocusedElement as Button;
        Require(response?.Content as string == "回应", "阅读视口的 Shift+Tab 未到上方工具栏");
        KeyStroke(Key.Enter);
        var compose = await Active<ComposeDialog>();
        var editor = compose.Editor;
        Require(editor.IsKeyboardFocused && InputMethod.GetIsInputMethodEnabled(editor), "编辑器未获得真实焦点，或原生输入法已禁用");
        Require(!KeyStroke(Key.F, previewOnly: true) && !KeyStroke(Key.Up, previewOnly: true) && !KeyStroke(Key.Escape, previewOnly: true), "编辑框的原生按键被拦截");
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, "fabc 中文"));
        KeyStroke(Key.Enter);
        TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, editor, "下一行"));
        Require(editor.Text == "fabc 中文\r\n下一行", "WPF 文本合成或 Enter 换行失效");
        ApplicationCommands.SelectAll.Execute(null, editor);
        Require(editor.SelectedText == editor.Text, "原生全选命令失败");
        ApplicationCommands.Undo.Execute(null, editor);
        Require(editor.Text != "fabc 中文\r\n下一行", "原生撤销失败");
        ApplicationCommands.Redo.Execute(null, editor);
        Require(editor.Text == "fabc 中文\r\n下一行", "原生撤销或重做失败");
        Capture(compose, Path.Combine(output, "native-editor.png"));
        MoveTab(FocusNavigationDirection.Next);
        Require((Keyboard.FocusedElement as Button)?.Content as string == "关闭", "编辑器 Tab 未到关闭按钮");
        KeyStroke(Key.Space);
        KeyStroke(Key.Space, up: true);
        await Idle();
        Require(!compose.IsVisible && response!.IsKeyboardFocused && Within(reader.View.VerticalOffset, readingOffset),
            $"关闭编辑窗后未恢复原工具按钮或视口（可见 {compose.IsVisible}，偏移 {reader.View.VerticalOffset:F1}/{readingOffset:F1}）");
        reader.View.Focus();
        KeyStroke(Key.F8);
        Require(!nav.HandleKey(Key.F, ModifierKeys.Control) && nav.HintLabels.Length == 0, "Ctrl+F 被当成普通 F");
        KeyStroke(Key.F);
        PickHint(nav, "回到顶部");
        await Idle();
        Require(Within(reader.View.VerticalOffset, 0), "底部工具按钮没有响应提示");
        var jumpShortcut = main.InputBindings.OfType<KeyBinding>().Single(binding => binding.Key == Key.L && binding.Modifiers == ModifierKeys.Control);
        ((RoutedCommand)jumpShortcut.Command).Execute(null, reader.View);
        Require(main.JumpBox.IsKeyboardFocused && main.JumpBox.SelectedText == main.JumpBox.Text, "Ctrl+L 未聚焦并选中串号框");
        Require(!KeyStroke(Key.F, previewOnly: true) && nav.HintLabels.Length == 0, "跳转框普通字符被提示模式处理");
        main.JumpBox.Text = "710002";
        KeyStroke(Key.Enter);
        await Idle();
        Require(main.CurrentThread == 710002 && reader.View.IsKeyboardFocused, "跳转框自身的 Enter 行为失效");
        passed.Add("原生 Tab 目标和按钮、上下工具栏、WPF 编辑命令和中英文文本合成");

        main.Width = 960;
        main.UpdateLayout();
        await Idle();
        main.JumpBox.Text = DemoContent.LongThread.ToString(System.Globalization.CultureInfo.InvariantCulture);
        main.JumpBox.Focus();
        KeyStroke(Key.Enter);
        await Idle();
        var narrowPost = Children<Border>(reader.View).Single(border => border.Tag is 710102);
        await Position(reader.View, narrowPost, 6);
        KeyStroke(Key.F);
        var narrow = new { main.ActualWidth, ReaderWidth = reader.Root.ActualWidth, HintCount = reader.HintBounds.Count };
        var narrowBounds = reader.HintBounds;
        var frame = new Rect(reader.Root.RenderSize);
        Require(narrow.HintCount > 12 && narrowBounds.All(frame.Contains) && !narrowBounds.Where((box, i) => narrowBounds.Take(i).Any(box.IntersectsWith)).Any(),
            "窄窗密集标签越界或重叠");
        Capture(main, Path.Combine(output, "scheme-a-narrow.png"));
        KeyStroke(Key.Escape);
        passed.Add("窄窗标签避让与视口裁切");

        var report = new
        {
            Passed = passed,
            WindowDip = initialSize,
            NarrowWindow = narrow,
            Dpi = VisualTreeHelper.GetDpi(main).PixelsPerInchX,
            DenseHintCount = labels.Length,
            RestoreToleranceDip = .5,
            Scroll = metrics
        };
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private static async Task<object> MeasureScroll(ScrollViewer view, Border longPost)
    {
        await Position(view, longPost, 0);
        Require(longPost.ActualHeight >= 3 * view.ViewportHeight, "长楼没有超过三屏");
        var start = view.VerticalOffset;
        KeyStroke(Key.Down);
        KeyStroke(Key.Down, up: true);
        await Task.Delay(210);
        var tapDip = view.VerticalOffset - start;
        Require(Within(tapDip, 28), $"短按偏移 {tapDip:F1} DIP");

        var timer = Stopwatch.StartNew();
        var changes = new List<(double Ms, double Offset)>();
        ScrollChangedEventHandler sample = (_, e) =>
        {
            if (Math.Abs(e.VerticalChange) > .01) changes.Add((timer.Elapsed.TotalMilliseconds, e.VerticalOffset));
        };
        view.ScrollChanged += sample;
        var holdStart = view.VerticalOffset;
        KeyStroke(Key.Down);
        for (var i = 0; i < 10; i++)
        {
            await Task.Delay(75);
            KeyStroke(Key.Down);
        }
        KeyStroke(Key.Down, up: true);
        var releaseAt = timer.Elapsed.TotalMilliseconds;
        var releaseOffset = view.VerticalOffset;
        await Task.Delay(180);
        var final = view.VerticalOffset;
        view.ScrollChanged -= sample;
        var holdDistance = final - holdStart;
        var afterRelease = final - releaseOffset;
        var stopMs = Math.Max(0, changes.LastOrDefault().Ms - releaseAt);
        var intervals = changes.Zip(changes.Skip(1), (a, b) => b.Ms - a.Ms).Order().ToArray();
        Require(holdDistance > 270 && changes.Count >= 16 && afterRelease <= 24 && stopMs < 70, $"长按偏移 {holdDistance:F1} DIP，{changes.Count} 次偏移更新，松手后 {afterRelease:F1} DIP / {stopMs:F1} ms");

        KeyStroke(Key.Down);
        await Task.Delay(160);
        var beforeReverse = view.VerticalOffset;
        timer.Restart();
        KeyStroke(Key.Up);
        while (view.VerticalOffset >= beforeReverse - 1 && timer.ElapsedMilliseconds < 160) await Task.Delay(1);
        var reverseMs = timer.Elapsed.TotalMilliseconds;
        Require(view.VerticalOffset < beforeReverse - 1 && reverseMs < 160, "反向没有及时生效");
        KeyStroke(Key.Down, up: true);
        KeyStroke(Key.Up, up: true);
        await Task.Delay(160);
        var beforePage = view.VerticalOffset;
        KeyStroke(Key.PageDown);
        await Idle();
        var pageDistance = view.VerticalOffset - beforePage;
        Require(Within(pageDistance, view.ViewportHeight * .92), "PgDn 未按视口滚动");
        KeyStroke(Key.PageUp);
        await Idle();
        Require(Within(view.VerticalOffset, beforePage), "PgUp 没有回到翻屏前位置");
        return new
        {
            ViewportDip = view.ViewportHeight, LongPostDip = longPost.ActualHeight, TapDip = Math.Round(tapDip, 1),
            HoldDurationMs = Math.Round(releaseAt, 1), HoldDip = Math.Round(holdDistance, 1), OffsetUpdates = changes.Count,
            MedianUpdateIntervalMs = Math.Round(intervals[intervals.Length / 2], 1),
            P95UpdateIntervalMs = Math.Round(intervals[(int)((intervals.Length - 1) * .95)], 1),
            MaxUpdateIntervalMs = Math.Round(intervals[^1], 1),
            AfterReleaseDip = Math.Round(afterRelease, 1), StopMs = Math.Round(stopMs, 1), ReverseMs = Math.Round(reverseMs, 1), PageDip = Math.Round(pageDistance, 1)
        };
    }

    private static void PickHint(NavigationInput navigation, string name)
    {
        var index = Array.FindIndex(navigation.Candidates, item => item.Target.Name == name);
        Require(index >= 0, $"找不到可见目标 {name}");
        foreach (var letter in navigation.HintLabels[index]) KeyStroke(Letter(letter));
    }

    private static Key Letter(char letter) => Key.A + (letter - 'A');

    private static bool KeyStroke(Key key, bool up = false, bool previewOnly = false)
    {
        if (Keyboard.FocusedElement is not UIElement target) throw new InvalidOperationException($"没有 WPF 键盘焦点（{key}）");
        var source = PresentationSource.FromVisual(target);
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = up ? Keyboard.PreviewKeyUpEvent : Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
        if (!args.Handled && !previewOnly)
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
            {
                RoutedEvent = up ? Keyboard.KeyUpEvent : Keyboard.KeyDownEvent
            });
        return args.Handled;
    }

    private static void MoveTab(FocusNavigationDirection direction)
    {
        Require(!KeyStroke(Key.Tab, previewOnly: true), "原型拦截了 Tab 路由");
        Require(((UIElement)Keyboard.FocusedElement).MoveFocus(new TraversalRequest(direction)), "WPF Tab 顺序没有下一个焦点");
    }

    private static async Task<T> Active<T>() where T : Window
    {
        for (var i = 0; i < 30; i++)
        {
            await Idle();
            if (Application.Current.Windows.OfType<T>().SingleOrDefault(window => window.IsActive) is { } active) return active;
        }
        throw new InvalidOperationException($"没有活动的 {typeof(T).Name}");
    }

    private static async Task Position(ScrollViewer view, Visual item, double top)
    {
        var transform = item.TransformToVisual(view);
        var y = transform.Transform(new Point()).Y + view.VerticalOffset - top;
        view.ScrollToVerticalOffset(y);
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
        await Task.Delay(35);
    }

    private static bool Within(double actual, double expected) => Math.Abs(actual - expected) <= .5;

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

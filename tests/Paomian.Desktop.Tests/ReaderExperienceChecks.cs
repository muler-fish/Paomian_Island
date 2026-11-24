using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ReaderExperienceChecks
{
    private const string Html = "中文  English (ﾟ∀ﾟ) 😀<br><s>删线</s> <font color='#cc0000'>红色强调</font> <span style='color:#789922'>绿色公告</span> <span style='color:rgb(73, 113, 207)'>蓝色正文</span> <span style='color:#07090A'>深色文字</span> <span style='color:#abc'>浅色文字</span><br>  /\\_/\\<br> ( o.o )<br>&gt;&gt;No.12345678 后文<br>[h]未展开的示例[/h]";
    private const string LinkHtml = "<span style='color:#789922'><a href='https://example.com/color' style='color:#d02090'>链接指定色<span style='color:#ee55aa'>链接局部色</span></a> <a href='https://example.com/inherit'>链接继承色</a> &gt;&gt;No.23456789 https://example.com/auto</span> <a href='https://example.com/plain'>普通链接</a>";
    internal static XIslandApi CreateApi() => new(new HttpClient(new Fixture()));

    internal static async Task RunAsync(MainWindow main, string output, Action<Window, string> capture, bool colorsOnly, bool realInput)
    {
        using var input = realInput ? new FocusDesktopInput() : null;
        var handle = new WindowInteropHelper(main).Handle;
        if (realInput) await FocusDesktopInput.ActivateAsync(handle);
        var vm = main.Model;
        vm.State.ReaderImages = ImageModes.Click;
        vm.State.HideSpoilers = true;
        vm.AppearanceChanged();
        await vm.OpenThreadAsync(100);
        await Layout();
        var body = All<PostBody>(main).First(b => b.DataContext is ReadPost { Post.Id: 100 });
        var reply = All<PostBody>(main).First(b => b.DataContext is ReadPost { Post.Id: 101 });
        var heading = (TextBox)main.FindName("ReaderHeading");
        var first = Runs(body).First(r => r.Text.Contains("中文"));
        var link = InlineTree(body.Document.Blocks.OfType<Paragraph>().Single().Inlines).OfType<Hyperlink>().Single();
        var selected = EmojiText.Read(body.Document);
        Require(selected.Contains("中文  English (ﾟ∀ﾟ) 😀\r\n") && selected.Contains("  /\\_/\\\r\n ( o.o )") &&
            selected.Contains("[隐藏内容]") && !selected.Contains("未展开的示例"), "复制破坏正文格式或隐藏内容");
        Require(All<PostImage>(main).Any(p => p.Post is { HasImage: true }), "缺少图片混排测试入口");
        Require(heading.Text == "No.100" && heading.IsReadOnly, "串号不支持只读选择");
        CheckLegacyColorSettings(output);
        foreach (var theme in new[] { "light", "dark" })
        {
            vm.State.Theme = theme; App.ApplyTheme(theme);
            body.Focus(); body.Selection.Select(first.ContentStart, first.ContentEnd);
            await Layout();
            Require(Foreground(body, "红色强调") == Color.FromRgb(204, 0, 0) &&
                Foreground(body, "绿色公告") == Color.FromRgb(120, 153, 34) &&
                Foreground(body, "蓝色正文") == Color.FromRgb(73, 113, 207) &&
                Foreground(body, "深色文字") == Color.FromRgb(7, 9, 10) &&
                Foreground(body, "浅色文字") == Color.FromRgb(170, 187, 204), $"主题 {theme} 改写了帖子原色");
            var fixedLink = Color.FromRgb(72, 130, 169);
            var inherited = Color.FromRgb(120, 153, 34);
            Require(((SolidColorBrush)first.Foreground).Color == Brush("BodyText"), "普通正文失去主题前景色");
            Require(((SolidColorBrush)link.Foreground).Color == fixedLink &&
                Foreground(reply, "普通链接") == fixedLink, $"主题 {theme} 改写了默认链接色");
            Require(Foreground(reply, "链接指定色") == Color.FromRgb(208, 32, 144) &&
                Foreground(reply, "链接局部色") == Color.FromRgb(238, 85, 170) &&
                Foreground(reply, "链接继承色") == inherited &&
                Foreground(reply, ">>No.23456789") == inherited &&
                Foreground(reply, "https://example.com/auto") == inherited, $"主题 {theme} 改写了链接或引用的正文原色");
            var ink = ((SolidColorBrush)body.SelectionBrush).Color;
            var highlight = Personalization.Mix(Brush("Surface"), ink, body.SelectionOpacity);
            var text = Personalization.Mix(Brush("BodyText"), ink, body.SelectionOpacity);
            Require(Personalization.Contrast(highlight, Brush("Surface")) > 1.3 &&
                Personalization.Contrast(highlight, text) >= 4.5 && body.IsInactiveSelectionHighlightEnabled, "选区与背景或文字的对比不足");
            capture(main, System.IO.Path.Combine(output, "selection-" + theme + ".png"));
        }

        var settings = new SettingsWindow(vm) { Owner = main };
        settings.Show();
        try
        {
            var nav = Named<ListBox>(settings, "设置分类");
            nav.SelectedItem = "外观";
            await Layout();
            Require(!All<Expander>(settings).Any(x => Equals(x.Header, "正文彩色文字")) &&
                All<Expander>(settings).Any(x => Equals(x.Header, "自定义标识颜色")), "外观仍有正文调色入口，或误删标识颜色");
            if (!colorsOnly) await CheckShortcuts(main, settings, nav, realInput);
        }
        finally { settings.Close(); }

        if (realInput) await CheckRealSelection(main, body, first, link, heading);
        vm.State.Theme = "light"; App.ApplyTheme("light");
        Console.WriteLine("PASS original post colors in both themes, selection styling, and legacy profile compatibility" +
            (colorsOnly ? "." : realInput ? ", plus real desktop selection and shortcut routing." : ", plus shortcut scopes and settings."));
    }

    private static void CheckLegacyColorSettings(string output)
    {
        var store = new LocalStore(Path.Combine(output, "legacy-colors"));
        var statePath = Path.Combine(store.Root, "state.json");
        File.WriteAllText(statePath, """{"Theme":"dark","AccentColor":"#123456","PostColors":{"Mode":"vivid","Red":"#000000","Green":"#FFFFFF"},"ShortcutBindings":{"RefreshList":"None:F9"}}""");
        var state = store.Load();
        Require(store.LoadWarning is null && state.Theme == "dark" && state.AccentColor == "#123456" &&
            state.ShortcutBindings.GetValueOrDefault("RefreshList") == "None:F9", "旧正文调色字段影响了其他设置的读取");
        store.Save(state);
        using var saved = JsonDocument.Parse(File.ReadAllText(statePath));
        Require(!saved.RootElement.TryGetProperty("PostColors", out _), "已移除的正文调色配置仍被保存");
    }

    private static async Task CheckShortcuts(MainWindow main, SettingsWindow settings, ListBox nav, bool realInput)
    {
        var shortcuts = main.Model.Shortcuts;
        shortcuts.RestoreAll();
        nav.SelectedItem = "快捷键";
        await Layout();
        Require(AppShortcuts.Commands.Count(c => c.Scope != ShortcutScope.Global) == 13 && main.InputBindings.Count == 6, "缺少现有快捷键");
        Require(shortcuts.TrySet(ShortcutId.Jump, new(Key.F5))?.Contains("刷新当前内容栏") == true, "主窗口冲突未拒绝");
        Require(shortcuts.TrySet(ShortcutId.RotateImage, new(Key.F5)) is null, "不同窗口作用域被误判冲突");
        Require(shortcuts.TrySet(ShortcutId.Back, new(Key.C, ModifierKeys.Control)) is not null &&
            shortcuts.TrySet(ShortcutId.Back, new(Key.F4, ModifierKeys.Alt)) is not null &&
            shortcuts.TrySet(ShortcutId.Back, new(Key.LeftCtrl)) is not null, "编辑键、系统键或单修饰键未保留");
        var edit = Named<Button>(settings, "聚焦串号或链接输入框：修改");
        var dialog = settings.Dispatcher.InvokeAsync(async () =>
        {
            var recorder = Application.Current.Windows.OfType<DialogWindow>().Single(w => w.Title == "修改快捷键");
            try
            {
                Require(ReferenceEquals(recorder.Owner, settings), "录入窗口 Owner 错误");
                if (realInput)
                {
                    var handle = new WindowInteropHelper(recorder).Handle;
                    FocusDesktopInput.Key(handle, 0x74);
                    await Task.Delay(90);
                    Require(recorder.IsVisible && All<TextBlock>(recorder).Any(t => t.Text.Contains("已用于")), "录入冲突无提示");
                    FocusDesktopInput.Key(handle, 0x78);
                    await Task.Delay(90);
                }
                else Press(recorder, Key.F9, Keyboard.PreviewKeyDownEvent);
                Require(!recorder.IsVisible, "录入成功后窗口未关闭");
            }
            finally { if (recorder.IsVisible) recorder.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        Click(edit);
        await await dialog.Task;
        Require(main.Model.JumpPlaceholder.EndsWith("F9") && new AppShortcuts(main.Model.Store.Load()).Label(ShortcutId.Jump) == "F9",
            "录入未实时更新提示或持久化");
        var clear = Named<Button>(settings, "聚焦串号或链接输入框：清除");
        Click(clear);
        Require(new AppShortcuts(main.Model.Store.Load()).Get(ShortcutId.Jump).Count == 0, "清除后重启恢复了默认键");
        Click(Named<Button>(settings, "聚焦串号或链接输入框：恢复默认"));
        Require(main.Model.JumpPlaceholder.EndsWith("Ctrl+L"), "单项恢复默认失败");
        Click(All<Button>(settings).Single(b => b.IsVisible && Equals(b.Content, "全部恢复默认")));
        Require(main.Model.Store.Load().ShortcutBindings.Count == 0, "全部恢复默认后仍有覆盖项");
        Require(shortcuts.TrySet(ShortcutId.Jump, new(Key.F9)) is null, "重绑失败");
        settings.Close();
        main.Activate();
        await Layout();
        var body = All<PostBody>(main).First(); body.Focus();
        if (realInput) FocusDesktopInput.Key(new WindowInteropHelper(main).Handle, 0x78);
        else Press(main, Key.F9, Keyboard.KeyDownEvent);
        await Layout();
        Require(((TextBox)main.FindName("JumpBox")).IsKeyboardFocusWithin, "重绑后的快捷键未执行");
        shortcuts.RestoreAll();
        if (realInput)
        {
            body.Focus();
            FocusDesktopInput.Chord(new WindowInteropHelper(main).Handle, 0x11, 0x4C);
            await Layout();
            Require(((TextBox)main.FindName("JumpBox")).IsKeyboardFocusWithin, "从正文聚焦串号输入框的默认 Ctrl+L 未执行");
        }
    }

    private static async Task CheckRealSelection(MainWindow main, PostBody body, Run first, Hyperlink link, TextBox heading)
    {
        var handle = new WindowInteropHelper(main).Handle;
        await FocusDesktopInput.ActivateAsync(handle);
        var scope = All<FrameworkElement>(body).First(e => e.GetType().Name == "FlowDocumentView");
        Point PointAt(TextPointer position)
        {
            var rect = position.GetCharacterRect(LogicalDirection.Forward);
            return new(rect.Left + 1, rect.Top + rect.Height / 2);
        }
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme); await Layout();
            await FocusDesktopInput.Drag(scope, PointAt(first.ContentStart), PointAt(link.ContentEnd), handle);
            var selection = body.SelectedText();
            Require(selection.Contains("English") && selection.Contains("绿色公告") && selection.Contains("o.o") && !body.Selection.IsEmpty, "真实拖选未覆盖跨行、彩色文字或 ASCII 图案");
            FocusDesktopInput.Chord(handle, 0x11, 0x43);
            await Task.Delay(100);
            Require(Clipboard.GetText() == selection, "真实 Ctrl+C 未复制所选正文");
            FocusDesktopInput.RightClick(scope, PointAt(body.Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward)), handle);
            await Task.Delay(100);
            Require(body.ContextMenu.IsOpen && body.SelectedText() == selection, "右键改变了选区");
            body.ContextMenu.IsOpen = false;
        }
        body.Selection.Select(body.Document.ContentStart, body.Document.ContentStart);
        await Layout();
        await FocusDesktopInput.Drag(scope, PointAt(link.ContentStart.GetPositionAtOffset(3)!), PointAt(first.ContentStart), handle);
        Require(!main.OwnedWindows.OfType<TextWindow>().Any(), "从引用开始拖选误触发预览");
        Require(!body.Selection.IsEmpty, "从引用开始拖选没有形成选区");
        await Task.Delay(650);
        FocusDesktopInput.ClickAt(scope, PointAt(link.ContentStart.GetPositionAtOffset(3)!), handle);
        await Task.Delay(120);
        var quote = main.OwnedWindows.OfType<TextWindow>().SingleOrDefault();
        Require(quote is not null, "引用单击无法打开预览");
        quote!.Close();
        await Layout();
        var left = heading.GetRectFromCharacterIndex(0);
        var right = heading.GetRectFromCharacterIndex(heading.Text.Length - 1, trailingEdge: true);
        var y = left.Top + left.Height / 2;
        await FocusDesktopInput.Drag(heading, new(left.Left + 1, y), new(Math.Min(heading.ActualWidth - 1, right.Left - 1), y), handle);
        FocusDesktopInput.Chord(handle, 0x11, 0x43);
        await Task.Delay(100);
        Require(heading.SelectionLength > 0 && Clipboard.GetText() == heading.SelectedText,
            $"顶部串号真实输入失败：选中长度={heading.SelectionLength}，键盘焦点={heading.IsKeyboardFocusWithin}");
    }

    private static void Press(Window w, Key key, RoutedEvent route) => w.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), 0, key) { RoutedEvent = route });
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static T Named<T>(DependencyObject root, string name) where T : FrameworkElement => All<T>(root).Single(e => AutomationProperties.GetName(e) == name);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static IEnumerable<Inline> InlineTree(InlineCollection inlines) => inlines.SelectMany(i => i is Span s ? new[] { i }.Concat(InlineTree(s.Inlines)) : [i]);
    private static IEnumerable<Run> Runs(PostBody body) => body.Document.Blocks.OfType<Paragraph>().SelectMany(p => InlineTree(p.Inlines)).OfType<Run>();
    private static Color Foreground(PostBody body, string text) => ((SolidColorBrush)Runs(body).Single(run => run.Text == text).Foreground).Color;
    private static Color Brush(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
    private static async Task Layout() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.Method == HttpMethod.Get, "专项禁止发送请求");
            var reference = request.RequestUri!.AbsolutePath.EndsWith("/ref", StringComparison.Ordinal);
            var json = reference ? """{"id":12345678,"content":"引用示例"}""" : JsonSerializer.Serialize(new
            {
                id = 100, fid = 4, content = Html, ReplyCount = 1,
                Replies = new[] { new { id = 101, fid = 4, content = LinkHtml, img = "fixture/image", ext = ".png" } }
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}

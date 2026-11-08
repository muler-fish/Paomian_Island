using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>自定义颜文字：新增／编辑／删除、隐藏与恢复内置、大型图案原样保存与插入、预览位置、持久化与缓存清理。只用隔离资料和模拟接口。</summary>
internal static class EmoticonCustomChecks
{
    // 行首空格、行尾空格、反斜杠与空行都必须原样保留。
    private const string Art = "   /\\_/\\   \n  ( o.o )  \n   > ^ <\n\n  __|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__  ";

    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var root = Path.Combine(output, "custom-profile");
        var store = new LocalStore(root);
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        vm.State.Forums = [new(4, "综合版1", "综合")];
        vm.State.Cookies = [new("test", "测试饼干", SecretProtection.Protect("synthetic-cookie"))]; vm.State.ActiveCookieId = "test";
        vm.State.CustomEmoticons = []; vm.State.HiddenEmoticons = []; vm.State.RecentEmoticons = []; vm.Save();
        var composer = new ComposerWindow(vm, 0) { Owner = owner, ShowInTaskbar = false };
        composer.Show(); await Pause();
        try
        {
            var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single(e => AutomationProperties.GetName(e) == "发言正文");
            var menu = composer.Emoticons;
            capture(composer, Path.Combine(output, "composer-toolbar-light.png"));
            foreach (var tool in new[] { "颜文字", "防剧透", "骰子", "添加图片" })
            {
                var button = ComposerInputChecks.Tool(composer, tool);
                Require(button.Height == 32 && button.Background is SolidColorBrush { Color.A: 0 }, "工具按钮规格不统一：" + tool);
            }

            // 切换分类时弹出层尺寸和位置不变（全部 197 条、最近使用和自定义为空），不跳到按钮另一侧。
            await Open(composer);
            Rect? first = null;
            foreach (var tab in new[] { "全部", "最近使用", "自定义", "全部" })
            {
                await Category(menu, tab);
                var bounds = ScreenRect((FrameworkElement)menu.Popup.Child);
                first ??= bounds;
                Require(Math.Abs(bounds.Top - first.Value.Top) < 0.5 && Math.Abs(bounds.Left - first.Value.Left) < 0.5 && Math.Abs(bounds.Height - first.Value.Height) < 0.5 && Math.Abs(bounds.Width - first.Value.Width) < 0.5,
                    $"切换到「{tab}」后弹出层尺寸或位置变化：{bounds} / {first}");
            }
            // 空的自定义分类
            await Category(menu, "自定义");
            Require(Texts(menu).Any(t => t.StartsWith("还没有自定义颜文字")), "空的自定义分类没有提示");
            Snapshot(menu.Popup, Path.Combine(output, "custom-empty-light.png"));

            // 通过界面新增：大型 ASCII Art，不填简称
            await WithDialog<EmoticonEditWindow>(async dialog =>
            {
                Field(dialog, "颜文字内容").Text = Art.Replace("\n", "\r\n");
                capture(dialog, Path.Combine(output, "edit-new-light.png"));
                App.ApplyTheme("dark"); await Pause(); capture(dialog, Path.Combine(output, "edit-new-dark.png")); App.ApplyTheme("light");
                Click(dialog, "保存");
            }, () => ComposerInputChecks.Invoke(Add(menu)));
            Require(vm.State.CustomEmoticons.Single().Text == Art && store.Load().CustomEmoticons.Single().Text == Art, "大型颜文字的空格、换行或反斜杠没有原样保存");
            Require(menu.Popup.IsOpen && menu.Category == "custom", "保存后没有回到自定义分类");
            var artEntry = Entry(menu, Art);
            Require(artEntry.Display.Length <= 24 && artEntry.Display.Contains("行") && artEntry.IsAsciiArt, "无简称的大型颜文字没有截断显示：" + artEntry.Display);
            // 空内容不能保存
            await WithDialog<EmoticonEditWindow>(async dialog =>
            {
                Field(dialog, "颜文字内容").Text = "  \r\n ";
                Click(dialog, "保存"); await Pause();
                Require(dialog.IsVisible && UiRefinementChecks.Descendants<TextBlock>(dialog).Any(t => t.Text == "请填写颜文字内容。"), "空内容可以保存");
                Field(dialog, "颜文字内容").Text = "(｀・ω・´)ゞ"; Field(dialog, "简称或备注").Text = "  敬礼  ";
                Click(dialog, "保存");
            }, () => ComposerInputChecks.Invoke(Add(menu)));
            Require(vm.State.CustomEmoticons[1] is { Text: "(｀・ω・´)ゞ", Name: "敬礼" }, "简称没有去掉首尾空白或内容被改动");

            // 大量自定义条目：布局不撑破、全部可见
            for (var i = 0; i < 60; i++) EmoticonCatalog.Save(vm.State, null, $"自定义{i}号 (・∀・)" + new string('长', i % 7 * 10), i % 3 == 0 ? "" : $"第{i}个");
            vm.Save(); composer.Emoticons.Toggle(); await Open(composer); await Category(menu, "自定义");
            var surface = (FrameworkElement)menu.Popup.Child;
            var realized = Buttons(menu);
            Require(menu.Shown.Count == 62 && realized.Count > 0 && realized.All(b => b.ActualWidth <= surface.ActualWidth / menu.ColumnCount) && surface.ActualWidth <= composer.ActualWidth, "大量或超长自定义条目撑破了选择器");
            Snapshot(menu.Popup, Path.Combine(output, "custom-many-light.png"));
            App.ApplyTheme("dark"); await Pause(); Snapshot(menu.Popup, Path.Combine(output, "custom-many-dark.png")); App.ApplyTheme("light");

            // 预览：固定后可滚动，位置在显示器工作区内，不压住主要操作按钮
            await MenuAction(menu, Art, "查看完整内容");
            Require(menu.Preview.IsOpen && menu.Preview.Child is Border previewBorder && previewBorder.IsHitTestVisible, "固定预览没有打开");
            var preview = (Border)menu.Preview.Child;
            var shown = UiRefinementChecks.Descendants<EmojiLabel>(preview).Single(l => AutomationProperties.GetName(l) == "颜文字完整内容");
            Require(shown.Text == Art && shown.TextWrapping == TextWrapping.NoWrap && shown.FontFamily.Source.StartsWith("Consolas"), "预览改动了图案、自动折行或没有使用等宽字体");
            var work = WindowPlacement.WorkArea(composer); var rect = ScreenRect(preview);
            Require(rect.Left >= work.Left - 1 && rect.Right <= work.Right + 1 && rect.Top >= work.Top - 1 && rect.Bottom <= work.Bottom + 1, $"预览超出屏幕工作区：{rect} / {work}");
            var anchor = ScreenRect(menu.Realize(Entry(menu, Art))!);
            Require((rect.Bottom <= anchor.Top + 1 || rect.Top >= anchor.Bottom - 1) && rect.Left < anchor.Right && rect.Right > anchor.Left, $"预览没有贴在条目上方或下方：{rect} / {anchor}");
            var send = UiRefinementChecks.Descendants<Button>(composer).Single(b => Equals(b.Content, "预览并发送"));
            Require(!rect.IntersectsWith(ScreenRect(send)), "预览压住了「预览并发送」");
            Snapshot(menu.Preview, Path.Combine(output, "preview-pinned-light.png"));
            App.ApplyTheme("dark"); await Pause(); Snapshot(menu.Preview, Path.Combine(output, "preview-pinned-dark.png")); App.ApplyTheme("light");
            // 从固定预览插入：替换选区，光标在插入内容之后
            menu.Toggle(); editor.Text = "前后"; editor.Select(1, 0); await Pause();
            await Open(composer); await Category(menu, "自定义"); await MenuAction(menu, Art, "查看完整内容");
            Click(UiRefinementChecks.Descendants<Button>(menu.Preview.Child).Single(b => AutomationProperties.GetName(b) == "插入")); await Pause();
            var inserted = "前" + Art.Replace("\n", "\r\n");
            Require(editor.Text == inserted + "后" && editor.CaretIndex == inserted.Length, "完整内容插入错误或光标不在插入内容之后");
            Require(vm.State.RecentEmoticons.FirstOrDefault() == Art, "插入自定义颜文字没有记入最近使用");

            // 编辑：修改内容和简称，最近使用跟着更新
            await Open(composer); await Category(menu, "自定义");
            await WithDialog<EmoticonEditWindow>(async dialog =>
            {
                Require(Field(dialog, "颜文字内容").Text == Art.Replace("\n", "\r\n"), "编辑窗口没有载入原文");
                Field(dialog, "颜文字内容").Text = Art.Replace("o.o", "^.^").Replace("\n", "\r\n"); Field(dialog, "简称或备注").Text = "开心猫";
                await Pause(); Click(dialog, "保存");
            }, () => MenuAction(menu, Art, "编辑"));
            var edited = Art.Replace("o.o", "^.^");
            Require(vm.State.CustomEmoticons[0] is { Name: "开心猫" } c && c.Text == edited && vm.State.RecentEmoticons[0] == edited, "编辑没有更新内容、简称或最近使用");
            Require(Entry(menu, edited).Display == "开心猫", "有简称的大型颜文字没有显示简称");

            // 删除：需确认；取消不删除，确认后同时清掉最近使用
            await WithDialog<ConfirmWindow>(async confirm => { await Pause(); Click(confirm, "取消"); }, () => MenuAction(menu, edited, "删除"));
            Require(vm.State.CustomEmoticons.Any(e => e.Text == edited), "取消删除仍删除了条目");
            await WithDialog<ConfirmWindow>(async confirm => { await Pause(); Click(confirm, "确定"); }, () => MenuAction(menu, edited, "删除"));
            Require(!vm.State.CustomEmoticons.Any(e => e.Text == edited) && !vm.State.RecentEmoticons.Contains(edited) && !store.Load().RecentEmoticons.Contains(edited), "删除没有移除条目或最近使用记录");
            await Category(menu, "最近使用");
            Require(!menu.Shown.Any(e => e.Text == edited), "最近使用里仍有已删除的条目");

            // 隐藏与恢复内置条目
            var builtin = EmoticonCatalog.Entries.First(e => !e.IsLarge);
            await Category(menu, "全部"); await MenuAction(menu, builtin.Text, "隐藏");
            Require(store.Load().HiddenEmoticons.SequenceEqual(new[] { builtin.Text }) && !menu.Shown.Any(e => e.Text == builtin.Text), "隐藏内置颜文字失败");
            var hiddenEntry = UiRefinementChecks.Descendants<Button>(menu.Popup.Child).Single(b => Equals(b.Content, "已隐藏 1 个内置颜文字"));
            Click(hiddenEntry); await Pause();
            Snapshot(menu.Popup, Path.Combine(output, "hidden-light.png"));
            Click(UiRefinementChecks.Descendants<Button>(menu.Popup.Child).Single(b => AutomationProperties.GetName(b) == "恢复：" + builtin.Display)); await Pause();
            Require(store.Load().HiddenEmoticons.Count == 0 && EmoticonCatalog.Visible(vm.State).Any(e => e.Text == builtin.Text), "恢复内置颜文字失败");
            menu.Toggle();

            // 重启与清理缓存：自定义内容在 state.json，不在缓存目录
            var customBefore = JsonSerializer.Serialize(vm.State.CustomEmoticons);
            store.Cache.Clear(); vm.ClearTemporaryCache();
            var reloaded = new LocalStore(root).Load();
            Require(JsonSerializer.Serialize(reloaded.CustomEmoticons) == customBefore && reloaded.CustomEmoticons.Count == 61, "重启或清理缓存后自定义颜文字丢失");
            Require(!Directory.EnumerateFiles(Path.Combine(root, "cache"), "*", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("(｀・ω・´)ゞ")), "自定义颜文字写进了缓存目录");
        }
        finally { composer.Close(); App.ApplyTheme("light"); }

        // 损坏与兼容：单条无效只丢弃该条；旧资料没有新字段时照常读取，原最近使用保留
        var damaged = Path.Combine(output, "damaged-profile"); Directory.CreateDirectory(damaged);
        File.WriteAllText(Path.Combine(damaged, "state.json"), """
            { "RecentEmoticons": ["(=ﾟωﾟ)="], "CustomEmoticons": [null, { "Id": "", "Text": "无 Id" }, { "Id": "a", "Text": "" }, { "Id": "b", "Text": "  保留  ", "Name": null }, { "Id": "b", "Text": "重复 Id" }], "HiddenEmoticons": [null, "", "(=ﾟωﾟ)="] }
            """);
        var loaded = new LocalStore(damaged).Load();
        Require(loaded.CustomEmoticons.Count == 1 && loaded.CustomEmoticons[0] is { Id: "b", Text: "  保留  ", Name: "" } && loaded.HiddenEmoticons.SequenceEqual(new[] { "(=ﾟωﾟ)=" }), "损坏条目处理不正确");
        var legacy = Path.Combine(output, "legacy-profile"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "state.json"), """{ "RecentEmoticons": ["(=ﾟωﾟ)="] }""");
        var old = new LocalStore(legacy).Load();
        Require(old.CustomEmoticons.Count == 0 && old.HiddenEmoticons.Count == 0 && EmoticonCatalog.Recent(old).Single().Text == "(=ﾟωﾟ)=", "旧资料的最近使用不兼容");
        Console.WriteLine("PASS: custom emoticons add/edit/delete(confirm)/recent cleanup, hide/restore built-in, 60+ entries layout, large ASCII art preserved/inserted with caret after, pinned preview within work area and clear of actions, restart + cache clear, damaged/legacy state, light/dark.");
    }

    private static async Task Open(ComposerWindow composer) { if (!composer.Emoticons.Popup.IsOpen) await ComposerInputChecks.Invoke(ComposerInputChecks.Tool(composer, "颜文字")); Require(composer.Emoticons.Popup.IsOpen, "颜文字菜单未打开"); }
    private static async Task Category(EmoticonMenu menu, string label)
    { UiRefinementChecks.Descendants<RadioButton>(menu.Popup.Child).Single(r => Equals(r.Content, label)).IsChecked = true; await Pause(); }
    private static Button Add(EmoticonMenu menu) => UiRefinementChecks.Descendants<Button>(menu.Popup.Child).Single(b => AutomationProperties.GetName(b) == "新增");
    private static List<Button> Buttons(EmoticonMenu menu) => UiRefinementChecks.Descendants<Button>(menu.Popup.Child).Where(b => b.Tag is EmoticonEntry).ToList();
    private static EmoticonEntry Entry(EmoticonMenu menu, string text) => menu.Shown.Single(e => e.Text == text);
    private static IEnumerable<string> Texts(EmoticonMenu menu) => UiRefinementChecks.Descendants<TextBlock>(menu.Popup.Child).Select(t => t.Text);
    private static async Task MenuAction(EmoticonMenu menu, string text, string action)
    {
        var button = menu.Realize(Entry(menu, text))!;
        var item = button.ContextMenu!.Items.OfType<MenuItem>().Single(i => Equals(i.Header, action));
        button.ContextMenu.PlacementTarget = button;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Pause();
    }
    // 模态窗口会进入嵌套消息循环；先排队操作对话框的步骤，再触发打开。
    private static async Task WithDialog<T>(Func<T, Task> steps, Func<Task> open) where T : Window
    {
        var done = new TaskCompletionSource();
        _ = Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
        {
            try { await Pause(); var dialog = Application.Current.Windows.OfType<T>().Single(w => w.IsVisible); await steps(dialog); done.TrySetResult(); }
            catch (Exception ex) { done.TrySetException(ex); foreach (var w in Application.Current.Windows.OfType<T>().ToList()) w.Close(); }
        });
        await open(); await done.Task; await Pause();
    }
    private static TextBox Field(Window window, string name) => UiRefinementChecks.Descendants<TextBox>(window).Single(t => AutomationProperties.GetName(t) == name);
    private static void Click(Window window, string text) => Click(UiRefinementChecks.Descendants<Button>(window).Single(b => Equals(b.Content, text)));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    private static Rect ScreenRect(FrameworkElement element)
    {
        var toDip = PresentationSource.FromVisual(element)!.CompositionTarget!.TransformFromDevice;
        return new Rect(toDip.Transform(element.PointToScreen(new Point())), toDip.Transform(element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight))));
    }
    // Popup 有独立 HWND，直接渲染已布局的内容。
    private static void Snapshot(Popup popup, string path)
    {
        var surface = (FrameworkElement)popup.Child;
        Require(popup.IsOpen && surface.ActualWidth > 50 && surface.ActualHeight > 50, "弹出层不可见：" + Path.GetFileName(path));
        var dpi = VisualTreeHelper.GetDpi(surface);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static Task Pause() => Task.Delay(120);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

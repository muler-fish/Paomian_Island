using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class EmoticonChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var entries = EmoticonCatalog.Entries;
        Require(entries.Count == 197 && entries.All(e => !e.Source.Contains("Gboard")), "资料来源或数量不符合新要求。");
        foreach (var name in new[] { "neko2", "neko3", "冰封王座3", "N98", "冰箱先生3D", "全角空格", "零宽空格" })
            Require(entries.Any(e => e.Name == name), "缺少网页扩展条目：" + name);
        Require(entries.All(e => e.Text.Trim().Length > 1 || e.Text is "　" or "\u200b"), "仍含无意义的单个符号。");
        var large = entries.Single(e => e.Name == "F5欧拉");
        Require(large.Text == "　σ　σ\nσ(　´ρ`)σ[F5]\n　σ　σ", "图案空格换行被修改。");
        var store = new LocalStore(Path.Combine(output, "emoticon-profile"));
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        vm.State.Forums = [new(4, "综合版1", "综合")];
        vm.State.Cookies = [new("test", "测试饼干", SecretProtection.Protect("synthetic-cookie"))]; vm.State.ActiveCookieId = "test";
        ComposerWindow? composer = null;
        try
        {
            composer = new ComposerWindow(vm, 0) { Owner = owner, ShowInTaskbar = false }; composer.Show(); await Task.Delay(100);
            var options = Find<Expander>(composer, "可选项");
            Require(!options.IsExpanded && !UiRefinementChecks.Descendants<TextBox>(composer).Any(t => t.IsVisible && AutomationProperties.GetName(t) is "可选标题" or "可选名称"), "标题/名称未默认收起。");
            capture(composer, Path.Combine(output, "composer-optional-collapsed.png"));
            options.IsExpanded = true; await Task.Delay(60);
            var title = Find<TextBox>(composer, "可选标题"); var name = Find<TextBox>(composer, "可选名称"); var content = Find<EmojiEditor>(composer, "发言正文");
            Require(title.TranslatePoint(new(), composer).Y < name.TranslatePoint(new(), composer).Y && name.TranslatePoint(new(), composer).Y < content.TranslatePoint(new(), composer).Y, "顺序不是标题、名称、正文。");
            title.Text = "标题<&>"; name.Text = "名称<&>"; options.IsExpanded = false;
            content.Text = "前文替换后文"; content.Focus(); content.Select(2, 2);
            await Open(composer); var surface = composer.Emoticons.Popup.Child;
            Require(!UiRefinementChecks.Descendants<ComboBox>(surface).Any() && !UiRefinementChecks.Descendants<TextBox>(surface).Any(), "菜单仍有分类或搜索框。");
            Require(UiRefinementChecks.Descendants<TextBlock>(surface).Any(t => t.Text == "最近使用") && EmoticonButtons(composer).Count == entries.Count, "最近/全部区域不完整。");
            CapturePopup(composer, Path.Combine(output, "emoticon-dropdown-light.png"));
            await Choose(composer, "(=ﾟωﾟ)=");
            Require(content.Text == "前文(=ﾟωﾟ)=后文" && content.CaretIndex == 9 && content.IsKeyboardFocused, "选区替换或焦点错误。");
            content.Undo(); Require(content.Text == "前文替换后文", "不能一次撤销插入。");
            Require(vm.State.RecentEmoticons.SequenceEqual(new[] { "(=ﾟωﾟ)=" }), "成功插入没有记入最近使用。");

            // 用真实菜单点击 21 个不同条目，检查上限、去重与最新优先。
            var batch = entries.Where(e => !e.Text.Contains('\n')).Take(21).ToList();
            foreach (var entry in batch) { content.Text = ""; await Open(composer); await Choose(composer, entry.Text); }
            Require(vm.State.RecentEmoticons.Count == 20 && vm.State.RecentEmoticons[0] == batch[^1].Text && !vm.State.RecentEmoticons.Contains(batch[0].Text), "最近使用未限制为 20 项。");
            await Open(composer); await Choose(composer, batch[5].Text);
            Require(vm.State.RecentEmoticons.Count == 20 && vm.State.RecentEmoticons[0] == batch[5].Text, "重选没有去重置顶。");
            App.ApplyTheme("dark"); await Open(composer); await Task.Delay(70); CapturePopup(composer, Path.Combine(output, "emoticon-dropdown-dark-recent.png"));
            var original = content.Text;
            RaiseEscape(EmoticonButtons(composer).First()); await Task.Delay(50);
            Require(!composer.Emoticons.Popup.IsOpen && content.Text == original, "Esc 没有原样取消。");
            App.ApplyTheme("light");

            content.Text = "开头结尾"; content.Select(2, 0); await Open(composer); await Choose(composer, large.Text);
            var expected = "开头" + large.Text.Replace("\n", Environment.NewLine) + "结尾";
            Require(content.Text == expected, "长颜文字插入不完整。");
            content.Select(content.Text.Length, 0); await Invoke(Button(composer, "骰子"));
            expected += "[1,100]"; Require(content.Text == expected, "骰子格式错误。");
            await Invoke(Button(composer, "预览并发送")); await Task.Delay(80);
            var preview = Application.Current.Windows.OfType<TextWindow>().Single(w => w.Title == "发送预览");
            var body = UiRefinementChecks.Descendants<PostBody>(preview).Single();
            var previewText = new TextRange(body.Document.ContentStart, body.Document.ContentEnd).Text;
            Require(previewText.Contains("标题<&>") && previewText.Contains("名称<&>"), "预览丢失标题或名称。");
            preview.Close(); composer.Close(); composer = null;
            var draft = store.Load().Drafts.Single(d => d.ThreadId == 0);
            Require(draft.Content == expected && draft.Title == "标题<&>" && draft.Name == "名称<&>", "草稿未保存可选项或正文。");
            Require(store.Load().RecentEmoticons.SequenceEqual(vm.State.RecentEmoticons), "最近使用未持久化。");

            composer = new ComposerWindow(vm, 123) { Owner = owner, ShowInTaskbar = false }; composer.Show(); await Task.Delay(100);
            Require(UiRefinementChecks.Descendants<TextBlock>(composer).Any(t => t.Text == "No.123" && t.FontSize >= 20), "回复串号仍然过小。");
            content = Find<EmojiEditor>(composer, "发言正文"); content.Text = new string('a', 9999); content.CaretIndex = content.Text.Length;
            var recentBefore = vm.State.RecentEmoticons.ToArray(); await Open(composer); await Choose(composer, large.Text);
            Require(content.Text.Length == 9999 && vm.State.RecentEmoticons.SequenceEqual(recentBefore), "失败插入截断正文或错误记录最近。");
            content.Text = "回复"; content.Select(2, 0); await Open(composer); await Choose(composer, "(=ﾟωﾟ)=");
            capture(composer, Path.Combine(output, "reply-number.png"));
            composer.Close(); composer = null;
            Require(store.Load().Drafts.Single(d => d.ThreadId == 123).Content == "回复(=ﾟωﾟ)=", "回复入口插入失败。");
            File.WriteAllText(Path.Combine(output, "emoticons-results.json"), JsonSerializer.Serialize(new {
                Entries = entries.Count, Multiline = entries.Count(e => e.Text.Contains('\n')), Dropdown = true, NoSearchOrCategories = true,
                RecentLimit = 20, RecentDeduplicatedAndPersisted = true, SelectionAndUndo = true, WhitespacePreserved = true,
                OptionalTitleAndName = true, PreviewAndDraft = true, Dice = "[1,100]", LargerReplyNumber = true, LengthLimit = true, LivePosts = false
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: dropdown, recent-20 dedup/persistence, source filtering, multiline, selection/undo, optional name/title, preview/drafts, dice, reply number, length limit.");
        }
        finally { composer?.Close(); App.ApplyTheme("light"); }
    }
    private static List<Button> EmoticonButtons(ComposerWindow composer) => UiRefinementChecks.Descendants<Button>(composer.Emoticons.Popup.Child).Where(b => b.Tag is EmoticonEntry).ToList();
    internal static void CapturePopup(ComposerWindow composer, string path)
    {
        // Popup 有独立 HWND，主窗口截图不包含它，直接保存已布局的弹出层。
        var surface = (FrameworkElement)composer.Emoticons.Popup.Child;
        Require(surface.IsVisible && surface.ActualWidth > 100 && surface.ActualHeight > 100, "弹出层不可见。");
        var dpi = VisualTreeHelper.GetDpi(surface);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static async Task Open(ComposerWindow composer) { await Invoke(Button(composer, "颜文字")); Require(composer.Emoticons.Popup.IsOpen, "颜文字下拉未打开。"); }
    private static async Task Choose(ComposerWindow composer, string text) { await Invoke(EmoticonButtons(composer).First(b => ((EmoticonEntry)b.Tag).Text == text)); Require(!composer.Emoticons.Popup.IsOpen, "点击后菜单未关闭。"); }
    private static T Find<T>(Window window, string name) where T : DependencyObject => UiRefinementChecks.Descendants<T>(window).Single(t => AutomationProperties.GetName(t) == name);
    private static Button Button(Window window, string text) => UiRefinementChecks.Descendants<Button>(window).Single(b => b.Content?.ToString() == text);
    private static async Task Invoke(Button button) { ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(45); }
    private static void RaiseEscape(UIElement target) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

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
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class SpoilerChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var vm = owner.Model;
        const string html = "普通正文。[h]<b>秘密😀</b><br>第二行 <a href=\"https://www.nmbxd1.com/t/123\">链接</a>[/h] 后续正文。<br>[H]另一个隐藏内容[/H]";
        var window = new TextWindow("防剧透检查", html) { Owner = owner, ShowInTaskbar = false, Width = 600, Height = 400 };
        SettingsWindow? settings = null;
        try
        {
            vm.State.HideSpoilers = true; vm.AppearanceChanged();
            await vm.OpenThreadAsync(101);
            vm.ReadingPosts.Clear(); vm.ReadingPosts.Add(new(new Post { Id = 101, Content = html }, true, true));
            window.Show(); await Pause();
            var body = UiRefinementChecks.Descendants<PostBody>(window).Single();
            var readerBody = UiRefinementChecks.Descendants<PostBody>((ListBox)owner.FindName("ReaderList")).First();
            var bars = HiddenLinks(body).ToArray();
            Require(bars.Length == 2 && bars.All(b => b.Focusable), "多个防剧透片段或键盘入口丢失");
            Require(!EmojiText.Read(body.Document).Contains("秘密") && !EmojiText.Read(body.Document).Contains("链接"), "未展开内容提前进入可读文档");
            Require(EmojiChecks.ColorPixels(body) == 0, "隐藏 emoji 提前显色");
            body.SelectAll(); var data = new DataObject(); body.RaiseEvent(new DataObjectCopyingEventArgs(data, false));
            Require(!(data.GetData(DataFormats.UnicodeText) as string ?? "").Contains("秘密"), "选区复制泄露尚未展开内容");
            body.Selection.Select(body.Document.ContentStart, body.Document.ContentStart);
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); await Pause();
                Require(bars.All(b => b.Background is SolidColorBrush bg && bg.Color == Colors.Black), "黑条随主题丢失");
                capture(window, Path.Combine(output, "spoiler-" + theme + ".png"));
            }
            App.ApplyTheme("light");
            Require(bars[0].Focus(), "黑条无法键盘聚焦");
            bars[0].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Pause();
            Require(EmojiText.Read(body.Document).Contains("秘密😀\r\n第二行 链接") && !EmojiText.Read(body.Document).Contains("另一个隐藏内容"), "键盘展开或多片段独立性错误");
            var shown = body.Document.Blocks.OfType<Paragraph>().Single().Inlines.OfType<Span>().First(s => s is not Hyperlink);
            Require(shown.Inlines.OfType<Hyperlink>().Count() == 1 && shown.Inlines.OfType<Span>().Any(s => s.FontWeight == FontWeights.SemiBold), "展开后链接或加粗格式丢失");
            Require(EmojiChecks.ColorPixels(body) > 30, "展开后 emoji 没有恢复彩色");
            vm.State.FontSize = 20; vm.AppearanceChanged(); App.ApplyTheme("dark"); await Pause();
            Require(EmojiText.Read(body.Document).Contains("秘密😀"), "普通外观设置收起了已展开内容");
            bars[1].RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window)!, 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Pause(); Require(!HiddenLinks(body).Any(), "Enter 不能展开黑条");
            capture(window, Path.Combine(output, "spoiler-revealed.png"));

            // 设置既影响已打开的正文，又影响稍后新建的引用/发送预览，并跨资料重载保存。
            var card = new PostCard(new Post { Id = 123, Title = "[h]秘密[/h]", Content = "正文[h]摘要秘密[/h]" }, "测试"); vm.Posts.Add(card);
            settings = new SettingsWindow(vm) { Owner = window, ShowInTaskbar = false }; settings.Show(); await Pause();
            var checkbox = UiRefinementChecks.Descendants<CheckBox>(settings).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "启用防剧透");
            var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)!;
            toggle.Toggle(); await Pause();
            Require(!vm.Store.Load().HideSpoilers && !body.HideSpoilers, "设置未保存或没有同步已打开的正文");
            Require(!readerBody.HideSpoilers && EmojiText.Read(readerBody.Document).Contains("[h]秘密😀"), "设置未同步主阅读区");
            Require(EmojiText.Read(body.Document).Contains("[h]秘密😀") && EmojiText.Read(body.Document).Contains("[H]另一个隐藏内容[/H]"), "关闭后未保留原标签");
            Require(card.Title.Contains("[h]秘密[/h]") && card.Excerpt.Contains("[h]摘要秘密[/h]"), "关闭后列表仍强制隐藏");
            var preview = new TextWindow("新建预览检查", "[h]新建预览秘密[/h]") { Owner = window, ShowInTaskbar = false };
            preview.Show(); await Pause();
            Require(EmojiText.Read(UiRefinementChecks.Descendants<PostBody>(preview).Single().Document).Contains("[h]新建预览秘密[/h]"), "新建预览未读取关闭设置"); preview.Close();
            capture(window, Path.Combine(output, "spoiler-disabled.png"));
            capture(settings, Path.Combine(output, "spoiler-settings-dark.png"));
            toggle.Toggle(); await Pause();
            Require(vm.Store.Load().HideSpoilers && HiddenLinks(body).Count() == 2 && !card.Title.Contains("秘密"), "重新开启未重新遮盖正文或列表");
            App.ApplyTheme("light"); await Pause(); capture(settings, Path.Combine(output, "spoiler-settings-light.png")); settings.Close(); settings = null;

            body.Html = "长文本：[h]" + string.Concat(Enumerable.Repeat("一段很长的隐藏内容abcdef。", 16)) + "[/h]<br>[h][/h]<br>[h]没有闭合";
            window.Width = 480; await Pause();
            Require(HiddenLinks(body).Count() == 2 && EmojiText.Read(body.Document).Contains("[h]没有闭合"), "空段或不完整标签处理错误");
            var longMask = HiddenLinks(body).First().Inlines.OfType<Run>().Single();
            Require(longMask.ContentStart.GetPositionAtOffset(80)!.GetCharacterRect(LogicalDirection.Forward).Y > longMask.ContentStart.GetCharacterRect(LogicalDirection.Forward).Y, "长黑条没有随窄窗口换行");
            capture(window, Path.Combine(output, "spoiler-narrow.png"));
            body.Html = "新的普通正文"; Require(!HiddenLinks(body).Any() && EmojiText.Read(body.Document) == "新的普通正文", "控件复用遗留旧遮罩");
            File.WriteAllText(Path.Combine(output, "spoiler-results.json"), JsonSerializer.Serialize(new { Themes = true, HiddenBeforeReveal = true, ClipboardMasked = true, EmojiMasked = true, KeyboardSpaceAndEnter = true, IndependentSegments = true, HtmlAndLinksRestored = true, SettingPersists = true, ExistingAndNewWindows = true, LiteralTagsWhenDisabled = true, SummariesFollowSetting = true, NarrowAndRebind = true }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: black bars, keyboard reveal, masked copying/emoji, formatting, global setting, persistence, original tags, summaries and rebind.");
        }
        finally { settings?.Close(); window.Close(); vm.State.HideSpoilers = false; vm.State.FontSize = 16; vm.AppearanceChanged(); App.ApplyTheme("light"); }
    }
    private static IEnumerable<SpoilerSpan> HiddenLinks(PostBody body) => body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<SpoilerSpan>().Where(h => h.Hidden);
    private static Task Pause() => Task.Delay(120);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

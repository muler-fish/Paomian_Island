using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class DialogContentChecks
{
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static async Task Layout(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        await Task.Delay(45);
    }

    internal static async Task RunAsync(MainWindow main, MainViewModel vm, string output, Action<Window, string> capture)
    {
        var log = new List<string>();
        foreach (var theme in new[] { "dark", "light" })
        {
            App.ApplyTheme(theme);
            vm.State.Cookies.Clear();
            vm.State.Drafts.Clear();
            var draft = new DraftWindow(vm, (_, _) => { }) { Owner = main };
            var cookie = new CookieWindow(vm) { Owner = main };
            var records = new MyPostsWindow(vm) { Owner = main };
            draft.Show(); cookie.Show(); records.Show();
            try
            {
                foreach (var scale in new[] { 1.25, 1, 1.5 })
                {
                    UiCopyChecks.SetDpi(draft, scale, scale == 1 ? 460 : 640, scale == 1.25 ? 322 : 460);
                    UiCopyChecks.SetDpi(cookie, scale, scale == 1 ? 560 : 760, scale == 1 ? 420 : 540);
                    UiCopyChecks.SetDpi(records, scale, scale == 1 ? 520 : 720, scale == 1 ? 420 : 520);
                    await Layout(draft); await Layout(cookie); await Layout(records);
                    void Inspect(Window window, string message)
                    {
                        var label = All<TextBlock>(window).Single(t => t.IsVisible && t.Text == message);
                        var grid = (Grid)label.Parent;
                        var row = Grid.GetRow(label);
                        var top = grid.RowDefinitions.Take(row).Sum(r => r.ActualHeight);
                        var height = grid.RowDefinitions.Count == 0 ? grid.ActualHeight : grid.RowDefinitions[row].ActualHeight;
                        var center = label.TranslatePoint(new Point(label.ActualWidth / 2, label.ActualHeight / 2), grid);
                        var dx = center.X - grid.ActualWidth / 2;
                        var dy = center.Y - top - height / 2;
                        var dpi = VisualTreeHelper.GetDpi(label).PixelsPerInchX;
                        log.Add($"{theme} {dpi:F0} DPI {message}  dx={dx:F2} dy={dy:F2} DIP");
                        Require(height > label.ActualHeight + 10 && Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1, log[^1]);
                        Require(Math.Abs(dpi - 96 * scale) < .1 && label.FontSize == 16 && label.FontWeight == FontWeights.Normal, log[^1]);
                    }
                    Inspect(draft, "暂无草稿");
                    Inspect(cookie, "尚未导入饼干，可以继续以游客身份浏览。");
                    var tabs = All<TabControl>(records).Single();
                    tabs.SelectedIndex = 0; await Layout(records); Inspect(records, "暂无本机主题记录");
                    tabs.SelectedIndex = 1; await Layout(records); Inspect(records, "暂无本机回复记录");
                    if (scale != 1.25) continue;
                    capture(draft, Path.Combine(output, $"draft-{theme}.png"));
                    capture(cookie, Path.Combine(output, $"cookie-{theme}.png"));
                    capture(records, Path.Combine(output, $"records-{theme}.png"));
                }
                var recordVm = (SentPostsViewModel)All<MyPostsView>(records).Single().DataContext;
                var emptyReply = All<TextBlock>(records).Single(t => t.Text == "暂无本机回复记录");
                vm.Store.AddSentPost(new("fixture", true, 101, 4, "测试版", "测试身份", "", "", "测试正文", false, DateTimeOffset.Now));
                recordVm.Reload(); await Layout(records);
                Require(!emptyReply.IsVisible && All<Button>(records).Any(b => b.IsVisible && Equals(b.Content, "查看全文")), "记录出现后空提示仍可见");
                File.Delete(Path.Combine(vm.Store.Root, "sent-posts.json"));
                recordVm.Reload(); await Layout(records); Require(emptyReply.IsVisible, "清空后未恢复提示");
            }
            finally { records.Close(); cookie.Close(); draft.Close(); }
            vm.State.Cookies.Add(new("fixture", "测试身份", ""));
            var manager = new CookieWindow(vm) { Owner = main };
            manager.Show();
            try
            {
                var list = All<ListBox>(manager).Single();
                list.SelectedIndex = -1; await Layout(manager);
                var hint = All<TextBlock>(manager).Single(t => t.Text == "选择一份饼干查看详情。");
                var pane = (Grid)hint.Parent;
                var center = hint.TranslatePoint(new Point(hint.ActualWidth / 2, hint.ActualHeight / 2), pane);
                Require(hint.IsVisible && Math.Abs(center.X - pane.ActualWidth / 2) < 1 && Math.Abs(center.Y - pane.ActualHeight / 2) < 1,
                    "选择提示未居中于右侧详情栏");
                capture(manager, Path.Combine(output, $"details-{theme}.png"));
                list.SelectedIndex = 0; await Layout(manager);
                Require(!hint.IsVisible && All<Button>(manager).Any(b => b.IsVisible && Equals(b.Content, "保存备注")), "选择饼干后未恢复详情");
            }
            finally { manager.Close(); }
        }
        var saved = new Draft("fixture", 4, 0, "", "测试草稿", "", false);
        vm.State.Drafts.Add(saved);
        var filled = new DraftWindow(vm, (_, d) => vm.State.Drafts.Remove(d)) { Owner = main };
        filled.Show();
        try
        {
            await Layout(filled);
            All<Button>(filled).Single(b => Equals(b.Content, "继续编辑")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Layout(filled);
            Require(All<TextBlock>(filled).Any(t => t.IsVisible && t.Text == "暂无草稿"), "最后一份草稿处理后未出现空提示");
        }
        finally { filled.Close(); }
        await PreviewAsync(main, vm, output, capture, log);
        File.WriteAllLines(Path.Combine(output, "geometry.txt"), log);
    }

    private static async Task PreviewAsync(MainWindow main, MainViewModel vm, string output, Action<Window, string> capture, List<string> log)
    {
        vm.State.Forums = [new(4, "测试版", "")];
        vm.State.Cookies = [new("fixture", "测试身份", "")]; vm.State.ActiveCookieId = "fixture";
        vm.State.HideSpoilers = true; vm.AppearanceChanged();
        App.ApplyTheme("dark");
        await vm.OpenThreadAsync(101, 1);
        var list = (ListBox)main.FindName("ReaderList");
        IncrementalScroll.SetCommand(list, null); IncrementalScroll.SetPreviousCommand(list, null);
        main.Width = 1380; main.Height = 800;
        const string bodyText = "第一行保留原来的行距。\r\n\r\n    ( ´ ▽ \u0060 )ﾉ  😀\r\n[h]防剧透内容[/h]\r\n最后一行。";
        const string title = "泡面岛 · 阅读排版对照";
        const string author = "更新内容";
        var bitmap = ComposerImageChecks.Fixture();
        var file = Path.Combine(output, "排版样图.png");
        var bytes = ComposerImageChecks.Encode(bitmap);
        File.WriteAllBytes(file, bytes);
        var longTitle = string.Concat(Enumerable.Repeat("会自动换行的长标题", 10));
        foreach (long thread in new long[] { 0, 101 })
        foreach (var (subject, name) in new[] { (title, author), (longTitle, ""), ("", author), ("", ""), ("无标题", "无名氏"), ("<b>标题</b>", "  ") })
        {
            var illustrated = subject == title;
            var draft = new Draft("compare", 4, thread, subject, bodyText, illustrated ? file : "", false, name);
            var editor = new ComposerWindow(vm, thread, existing: draft) { ShowInTaskbar = false };
            editor.Show(); await Layout(editor);
            try
            {
                if (illustrated)
                {
                    var card = All<ComposerImageCard>(editor).Single();
                    for (var i = 0; i < 100 && !All<Image>(card).Any(im => im.Source != null); i++) await Task.Delay(25);
                    Require(All<Image>(card).Any(im => im.Source != null), "草稿附图没有载入");
                }
                var post = new Post { Id = thread == 0 ? 101 : 102, Title = System.Net.WebUtility.HtmlEncode(subject),
                    Name = name, Content = System.Net.WebUtility.HtmlEncode(bodyText).Replace("\r\n", "<br>"), UserHash = "测试身份",
                    Time = "2026-11-26 10:00:00", ImagePath = illustrated ? "qa/local" : "", Extension = ".png" };
                // 正式阅读器只需读取已准备好的模拟缩略图缓存。
                if (illustrated)
                {
                    var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(post.ImageUrl(true)!)));
                    vm.Store.Cache.Write("images/" + key + ".bin", bytes);
                }
                if (subject.StartsWith('<')) post = post with { Title = "<span>" + post.Title + "</span>" };
                vm.ReadingPosts.Clear(); vm.ReadingPosts.Add(new(post, thread == 0, thread == 0));
                await Layout(main);
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var readerBody = All<PostBody>(row).Single();
                // 在打开模态预览前拍摄正式窗口，避免父窗口遮罩干扰比较。
                if (illustrated)
                {
                    var imageControl = All<PostImage>(row).Single();
                    for (var i = 0; i < 100 && !All<Image>(imageControl).Any(im => im.Source != null); i++) await Task.Delay(25);
                    Require(All<Image>(imageControl).Any(im => im.Source != null), "正式阅读区未显示模拟附图");
                    capture(main, Path.Combine(output, thread == 0 ? "reader-thread.png" : "reader-reply.png"));
                }
                var inspect = editor.Dispatcher.InvokeAsync(async () =>
                {
                    var preview = editor.OwnedWindows.OfType<TextWindow>().Single();
                    try
                    {
                        preview.Height = 600; await Layout(preview);
                        var body = All<PostBody>(preview).Single();
                        preview.Width += readerBody.ActualWidth - body.ActualWidth; await Layout(preview);
                        var expected = new[] { subject == "无标题" ? "" : subject.Trim(), name == "无名氏" ? "" : name.Trim() };
                        var labels = All<EmojiLabel>(preview).ToArray();
                        var readerLabels = All<EmojiLabel>(row).Where(t => t.Name is "OriginalPostTitle" or "ReaderAuthor").ToArray();
                        Require(labels.Length == 2 && readerLabels.Length == 2, "阅读区与预览标题控件数量不同");
                        for (int i = 0; i < labels.Length; i++)
                        {
                            var a = labels[i]; var b = readerLabels[i];
                            Require(a.Text == expected[i] && a.IsVisible == (expected[i] != "") && a.IsVisible == b.IsVisible, "标题或名称占位规则错误");
                            if (!a.IsVisible) continue;
                            var gap = a.TranslatePoint(new Point(), body);
                            var reference = b.TranslatePoint(new Point(), readerBody);
                            Require(Math.Abs(gap.Y - reference.Y) <= 1 && Math.Abs(gap.X - reference.X) <= 1 &&
                                Math.Abs(a.ActualHeight - b.ActualHeight) <= 1, $"阅读区与预览标题间距不同 {subject} {gap} / {reference}");
                            if (subject == longTitle && i == 0) Require(a.ActualHeight > a.FontSize * 2, "长标题没有换行");
                        }
                        Require(EmojiText.Read(body.Document) == EmojiText.Read(readerBody.Document) && Math.Abs(body.ActualHeight - readerBody.ActualHeight) < 1, "同文正文排版不同");
                        var confirmation = All<TextBlock>(preview).Single(t => t.Style == preview.FindResource("MutedText"));
                        var confirmationText = new TextRange(confirmation.ContentStart, confirmation.ContentEnd).Text;
                        Require(confirmationText.Contains("测试身份") && (thread == 0 || confirmationText.Contains("No.101")), "发送确认信息不完整：" + confirmationText);
                        Require(confirmation.Inlines.OfType<Hyperlink>().Count() == (thread > 0 ? 1 : 0), "确认行串号链接缺失");
                        if (illustrated)
                        {
                            Require(All<Image>(preview).Any(im => AutomationProperties.GetName(im) == "发送预览附图" && im.IsVisible), "缺少附图");
                            capture(preview, Path.Combine(output, thread == 0 ? "preview-thread.png" : "preview-reply.png"));
                        }
                        log.Add($"thread={thread} title={subject} name={name} preview={body.ActualWidth:F1}x{body.ActualHeight:F1} reader={readerBody.ActualWidth:F1}x{readerBody.ActualHeight:F1}");
                    }
                    finally { preview.Close(); }
                }, DispatcherPriority.ApplicationIdle);
                All<Button>(editor).Single(b => Equals(b.Content, "预览并发送")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await await inspect.Task;
            }
            finally { editor.Close(); }
        }
    }
}

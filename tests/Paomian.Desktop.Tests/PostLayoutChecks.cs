using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class PostLayoutChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var failures = new List<string>(); var metrics = new List<object>();
        void Check(bool ok, string text) { if (!ok) failures.Add(text); }
        var handler = new Transport();
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(Path.Combine(output, Guid.NewGuid().ToString("N"))));
        vm.State.Forums = [new(4, "测试版", "")];
        vm.State.Cookies = [new("test", "测试身份", SecretProtection.Protect("fixture-only"))]; vm.State.ActiveCookieId = "test";
        string[] samples =
        [
            "正文起点单段",
            "正文起点\n第二行",
            "正文起点\r\n第二行\r\n第三行",
            "正文起点\n\n末行",
            "正文起点\n\n\n末行\n",
            "正文起点 v1.1.0\n阅读与滚动\n- 第一项\n- 第二项\n\n图片\n- 第三项",
            "正文起点  连续空格\n    /\\_/\\\n   ( o.o )\n    > ^ <",
            "正文起点😀👨‍👩‍👧‍👦\n>>No.12345678\n[h]秘密😀[/h]\n<b>输入的标签</b>",
            "正文起点\n" + string.Join('\n', Enumerable.Repeat("长正文  普通换行与滚动检查", 45)),
            "正文起点  " + string.Concat(Enumerable.Repeat("需要自动折行的文字　", 16)) + "\r\n\r\n",
        ];
        foreach (var theme in new[] { "light", "dark" })
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                App.ApplyTheme(theme);
                Application.Current.Resources["HideSpoilers"] = theme == "dark";
                vm.State.FontSize = scale == 1 ? 16 : scale == 1.25 ? 20 : 14;
                vm.State.ReadingLineHeight = scale == 1 ? 1.85 : scale == 1.25 ? 2.1 : 1.5;
                vm.State.ReadingLetterSpacing = scale == 1 ? 0 : scale == 1.25 ? 1 : .5;
                ReadingTypography.Apply(vm.State);
                foreach (var thread in new[] { 0L, 12345678L })
                {
                    var composer = new ComposerWindow(vm, thread) { Owner = owner, ShowInTaskbar = false };
                    composer.Show(); await Pause(); UiCopyChecks.SetDpi(composer, scale, 620, 720);
                    var editor = All<EmojiEditor>(composer).Single();
                    var postsBefore = handler.Posts;
                    try
                    {
                        for (var i = 0; i < samples.Length; i++)
                        {
                            editor.Text = samples[i]; await Pause(); var original = editor.Text;
                            Check(original == samples[i].Replace("\r\n", "\n").Replace("\n", "\r\n"), "编辑器文本变化");
                            var inspect = composer.Dispatcher.InvokeAsync(async () =>
                            {
                                var preview = composer.OwnedWindows.OfType<TextWindow>().Single();
                                try
                                {
                                    var body = All<PostBody>(preview).Single();
                                    var reader = new PostBody { Html = WebUtility.HtmlEncode(original).Replace("\r\n", "<br>"), FontSize = vm.ReadingFontSize };
                                    // 共用同一可见布局槽并一起接收 DPI 消息，避免比较不同生命周期的文本缓存。
                                    var panel = (StackPanel)body.Parent;
                                    var comparison = new Grid();
                                    var index = panel.Children.IndexOf(body);
                                    panel.Children.RemoveAt(index);
                                    comparison.Children.Add(reader);
                                    comparison.Children.Add(body);
                                    panel.Children.Insert(index, comparison);
                                    await Pause();
                                    UiCopyChecks.SetDpi(preview, scale, 620, 600);
                                    preview.UpdateLayout();
                                    await Pause();
                                    try
                                    {
                                        Check(EmojiText.Read(body.Document).EndsWith(EmojiText.Read(reader.Document), StringComparison.Ordinal), "预览丢失空格、空行或正文格式");
                                        var a = Positions(body); var b = Positions(reader);
                                        var tolerance = 1 / scale;
                                        var previewLines = a.Select(r => r.Y).Distinct().ToArray();
                                        var readerLines = b.Select(r => r.Y).Distinct().ToArray();
                                        var ok = previewLines.Length == readerLines.Length
                                            && previewLines.Zip(readerLines).All(p => Math.Abs(p.First - p.Second) <= tolerance)
                                            && Math.Abs(Height(a) - Height(b)) <= tolerance
                                            && a.Length == b.Length && a.Zip(b).All(p => Math.Abs(p.First.Y - p.Second.Y) <= tolerance);
                                        var previewBottom = EndOfBody(body) - FirstTextY(body);
                                        var readerBottom = EndOfBody(reader) - FirstTextY(reader);
                                        Check(Math.Abs(previewBottom - readerBottom) <= tolerance, "末尾空行导致正文总高度不同");
                                        if (!ok)
                                        {
                                            var differences = a.Zip(b).Select((pair, index) => new { index, pair.First, pair.Second })
                                                .Where(p => Math.Abs(p.First.Y - p.Second.Y) > tolerance || Math.Abs(p.First.Height - p.Second.Height) > tolerance);
                                            Check(false, $"preview {theme}/{scale}/{thread}/{i}: widths {body.ActualWidth}/{reader.ActualWidth}, positions {a.Length}/{b.Length}; {string.Join(" | ", differences.Take(5))}");
                                        }
                                        metrics.Add(new { theme, scale, thread, sample = i, raw = original, editorParagraphs = editor.Document.Blocks.Count, previewParagraphs = body.Document.Blocks.Count, previewHtml = body.Html, previewText = EmojiText.Read(body.Document), readerText = EmojiText.Read(reader.Document), editorHeight = editor.Document.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom, readerParagraphs = reader.Document.Blocks.Count, previewHeight = Height(a), readerHeight = Height(b), previewFont = body.FontSize, readerFont = reader.FontSize, previewLines = a.Select(r => r.Y).Distinct().ToArray(), readerLines = b.Select(r => r.Y).Distinct().ToArray(), ok });
                                        if (i == 5 && thread == 0)
                                        {
                                            reader.Width = body.ActualWidth;
                                            comparison.Children.Remove(reader);
                                            preview.UpdateLayout();
                                            capture(composer, Path.Combine(output, $"editor-{theme}-{scale}.png"));
                                            capture(preview, Path.Combine(output, $"preview-{theme}-{scale}.png"));
                                            var reference = new Window { Owner = owner, Content = new ScrollViewer { Content = reader }, ShowInTaskbar = false };
                                            reference.Show();
                                            try
                                            {
                                                UiCopyChecks.SetDpi(reference, scale, 650, 600);
                                                await Pause();
                                                capture(reference, Path.Combine(output, $"reader-{theme}-{scale}.png"));
                                            }
                                            finally { reference.Close(); }
                                        }
                                        if (i == 8)
                                        {
                                            var scroll = All<ScrollViewer>(preview).Single(s => s.Content is StackPanel);
                                            var button = All<Button>(preview).Single(b => Equals(b.Content, "确认发送")); var y = button.TranslatePoint(new Point(), preview).Y;
                                            scroll.ScrollToBottom(); await Pause();
                                            Check(scroll.ScrollableHeight > 0 && Math.Abs(button.TranslatePoint(new Point(), preview).Y - y) < .1 && button.IsVisible, "长正文滚动或固定按钮错误");
                                        }
                                    }
                                    finally
                                    {
                                        comparison.Children.Remove(body);
                                        panel.Children.Remove(comparison);
                                        panel.Children.Insert(index, body);
                                    }
                                }
                                finally { preview.Close(); }
                            }, DispatcherPriority.ApplicationIdle);
                            Click(composer, "预览并发送"); await await inspect.Task;
                            Check(editor.Text == original && handler.Posts == postsBefore, "预览改变正文或提前发送");
                        }
                        editor.CaretIndex = editor.Text.Length; await ComposerInputChecks.Type(editor, "续");
                        Check(editor.Text.EndsWith("续"), "关闭预览后无法继续编辑");
                        var sent = editor.Text;
                        var confirm = composer.Dispatcher.InvokeAsync(() => Click(composer.OwnedWindows.OfType<TextWindow>().Single(), "确认发送"), DispatcherPriority.ApplicationIdle);
                        Click(composer, "预览并发送"); await confirm.Task;
                        for (int retry = 0; retry < 100 && handler.Posts == postsBefore; retry++) await Task.Delay(20);
                        await Pause();
                        Check(handler.Posts == postsBefore + 1 && handler.LastBody == sent && editor.Text == sent, "最终模拟提交改变原文");
                        Check(vm.Store.Load().Drafts.Any(d => d.ThreadId == thread && d.Content == sent), "预览/失败后草稿没有保留原文");
                    }
                    finally { composer.Close(); }
                }
                var post = new Post { Id = 12345679, UserHash = "普通回应", Title = "泡面岛 v1.1.0 更新", Name = "更新内容", Content = "阅读与滚动……" };
                var list = (ListBox)owner.FindName("ReaderList");
                // 直接装载正式 DataTemplate，保持标题、名称、正文的真实布局。
                var row = new ContentControl { Content = new ReadPost(post, false), ContentTemplate = list.ItemTemplate, Width = 480, DataContext = vm };
                var host = new Window { Owner = owner, DataContext = vm, Content = row, Width = 550, Height = 420, ShowInTaskbar = false }; host.Show(); UiCopyChecks.SetDpi(host, scale, 550, 420); await Pause();
                Check(All<EmojiLabel>(row).Any(t => t.IsVisible && t.Text == post.Title), "普通回应标题未显示");
                capture(host, Path.Combine(output, $"reply-{theme}-{scale}.png")); host.Close();
            }
        await ReplyTitleChecks.RunAsync(owner, output, capture, Check);
        File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(metrics, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllLines(Path.Combine(output, "failures.txt"), failures);
        if (failures.Count > 0) throw new InvalidOperationException($"Post layout: {failures.Count} failures; see {output}");
        Console.WriteLine($"PASS: {metrics.Count} preview layouts and reply titles");
    }
    private static double EndOfBody(PostBody body) =>
        body.Document.ContentEnd.GetCharacterRect(LogicalDirection.Backward).Bottom;

    private static double FirstTextY(PostBody body) => BodySegments(body).First().Start.GetCharacterRect(LogicalDirection.Forward).Y;

    private static IEnumerable<EmojiText.Segment> BodySegments(PostBody body)
    {
        var segments = EmojiText.Segments(body.Document.ContentStart, body.Document.ContentEnd).ToArray();
        var start = Array.FindIndex(segments, s => s.Text.StartsWith("正文起点", StringComparison.Ordinal));
        // 字间距会将 Run 分为单字。
        if (start < 0) start = Array.FindIndex(segments, s => s.Text == "正");
        if (start < 0) throw new InvalidOperationException("预览或阅读区没有正文起点");
        return segments.Skip(start);
    }

    private static Rect[] Positions(PostBody body)
    {
        var rects = new List<Rect>();
        foreach (var s in BodySegments(body))
        {
            if (s.Start.GetPointerContext(LogicalDirection.Forward) != TextPointerContext.Text) continue;
            for (int i = 0; i < s.Text.Length; i++)
            {
                if (char.IsWhiteSpace(s.Text[i]) || char.IsSurrogate(s.Text[i])) continue;
                var r = s.Start.GetPositionAtOffset(i)!.GetCharacterRect(LogicalDirection.Forward);
                if (!r.IsEmpty) rects.Add(r);
            }
        }
        if (rects.Count == 0) throw new InvalidOperationException("没有实际文字位置");
        var y = rects[0].Y; return rects.Select(r => new Rect(r.X, r.Y - y, r.Width, r.Height)).ToArray();
    }
    private static double Height(Rect[] rects) => rects.Max(r => r.Bottom);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static void Click(Window w, string text) => All<Button>(w).Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Pause() { await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); await Task.Delay(20); }
    private sealed class Transport : HttpMessageHandler
    {
        internal int Posts; internal string LastBody = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get)
            {
                Posts++;
                var parts = (MultipartFormDataContent)request.Content!;
                LastBody = await parts.Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "content").ReadAsStringAsync(ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<p class='error'>模拟拒绝，保留草稿</p>") };
        }
    }
}

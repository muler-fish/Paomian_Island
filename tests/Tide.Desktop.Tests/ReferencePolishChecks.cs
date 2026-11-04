using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ReferencePolishChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var paragraph = new Paragraph();
        const string source = "纯数字 12345678；No.23456789；&gt;&gt;No.42；&gt;&gt;34567890；1234567；99999999999999999999999999；https://example.com/45678901；<a href='https://example.com/56789012'>No.56789012</a>";
        PostBody.AppendHtml(paragraph.Inlines, source);
        var links = Inlines(paragraph.Inlines).OfType<Hyperlink>().ToArray();
        var texts = links.Select(link => new TextRange(link.ContentStart, link.ContentEnd).Text).ToArray();
        Require(texts.SequenceEqual(new[] { "12345678", "23456789", ">>No.42", "34567890", "https://example.com/45678901", "No.56789012" }), "引用标蓝范围、八位边界、长数字或 URL 优先级错误: " + string.Join(" | ", texts));
        Require(links.All(link => !Inlines(link.Inlines).OfType<Hyperlink>().Any()), "HTML 链接内部生成了嵌套链接");
        Require(new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Contains("99999999999999999999999999"), "越界数字原文被截断");

        var model = owner.Model;
        var root = new PostCard(new Post { Id = 87654321, Content = "已加载串首" }, "综合版1");
        var reply = new ReadPost(new Post { Id = 87654322, Content = "已加载回复" }, false);
        model.Posts.Add(root); model.ReadingPosts.Add(reply);
        try
        {
            Require(model.IsKnownThreadRoot(root.Post.Id), "已知列表串首没有打开入口");
            Require(!model.IsKnownThreadRoot(reply.Post.Id) && !model.IsKnownThreadRoot(87654323), "回复或未知引用被误判为串首");
        }
        finally { model.Posts.Remove(root); model.ReadingPosts.Remove(reply); }

        var quote = new TextWindow("引用 · No.87654322", "正在读取引用…", referencePreview: true) { Owner = owner, ShowInTaskbar = false };
        quote.Show(); await Pause();
        try
        {
            var loadingHeight = quote.ActualHeight;
            quote.SetPost(new Post { Id = 87654322, UserHash = "测试饼干", Content = string.Join("<br>", Enumerable.Range(1, 8).Select(i => $"引用正文第 {i} 行，保留连续  空格 (ﾟ∀ﾟ)")) });
            await Pause();
            var body = UiRefinementChecks.Descendants<PostBody>(quote).Single();
            Require(quote.ActualHeight > loadingHeight + 100, "异步引用内容没有撑开窗口");
            Require(body.ActualHeight <= body.Document.LineHeight * 10 + 5, "短引用超过约十行限制");
            Require(body.ExtentHeight - body.ViewportHeight < 2, "八行引用提前出现正文滚动条");
            capture(quote, System.IO.Path.Combine(output, "reference-eight-lines.png"));

            quote.SetPost(new Post { Id = 87654322, UserHash = "测试饼干", Content = string.Join("<br>", Enumerable.Range(1, 40).Select(i => $"长引用第 {i} 行")) });
            await Pause();
            Require(body.ExtentHeight - body.ViewportHeight > body.ActualHeight && body.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "长引用无法在正文中滚动");
            Require(body.ActualHeight <= body.Document.LineHeight * 10 + 5, "长引用未限制为约十行");
            body.ScrollToEnd(); await Pause();
            Require(body.VerticalOffset > 0, "引用滚动未生效");
            capture(quote, System.IO.Path.Combine(output, "reference-long-scrolled.png"));

            // Changing line height while already capped must update the ten-line viewport too.
            body.Document.LineHeight = 40; quote.UpdateLayout(); await Pause();
            Require(Math.Abs(body.MaxHeight - 404) < 1, "行高变化后引用上限没有重新计算");
            quote.Width = quote.MinWidth; await Pause();
            var close = UiRefinementChecks.Descendants<Button>(quote).First(b => Equals(b.Content, "关闭"));
            var bounds = close.TransformToAncestor(quote).TransformBounds(new Rect(close.RenderSize));
            Require(bounds.Bottom <= quote.ActualHeight && bounds.Top >= 0, "窄引用窗口关闭按钮不可访问");

            var nested = new TextWindow("引用 · No.12345678", "嵌套引用", referencePreview: true) { Owner = quote, ShowInTaskbar = false };
            nested.Show(); await Pause(); nested.Close();
            Require(quote.IsVisible, "关闭嵌套引用关闭了上一级引用");
        }
        finally { quote.Close(); }
        Console.WriteLine("PASS: reference expansion, ten-line scrolling, line-height/width adaptation, known-root gating and numeric/No./URL parsing.");
    }

    private static IEnumerable<Inline> Inlines(InlineCollection source)
    {
        foreach (var inline in source)
        {
            yield return inline;
            if (inline is Span span) foreach (var child in Inlines(span.Inlines)) yield return child;
        }
    }
    private static Task Pause() => Task.Delay(180);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

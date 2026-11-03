using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class EmojiChecks
{
    internal static int ColorPixels(FrameworkElement element)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (pixels[i+3] > 200 && Math.Max(pixels[i], Math.Max(pixels[i+1], pixels[i+2])) - Math.Min(pixels[i], Math.Min(pixels[i+1], pixels[i+2])) > 40) count++;
        return count;
    }

    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var body = new PostBody { Html = "😀", HideSpoilers = true, FontSize = 48, Foreground = Brushes.Black, Background = Brushes.White };
        body.Document.Foreground = Brushes.Black;
        var window = new Window { Width = 400, Height = 180, Content = body, Owner = owner, ShowInTaskbar = false };
        try
        {
            var shaping = new DrawingVisual();
            using (var dc = shaping.RenderOpen()) dc.DrawText(new FormattedText("👨‍👩‍👧‍👦", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI Emoji"), 64, Brushes.Black, 1), new Point());
            var drawings = GlyphDrawings(VisualTreeHelper.GetDrawing(shaping)).ToArray();
            File.WriteAllText(Path.Combine(output, "shaping.json"), JsonSerializer.Serialize(new { Library = Emoji.Wpf.EmojiData.Typeface.MakeGlyphIndexList("👨‍👩‍👧‍👦").ToArray(), Windows = drawings.Select(d => new { Glyphs = d.GlyphRun.GlyphIndices, Font = d.GlyphRun.GlyphTypeface.FontUri.AbsoluteUri, Advances = d.GlyphRun.AdvanceWidths }) }));
            window.Show(); await Task.Delay(150); window.UpdateLayout();
            var colored = ColorPixels(body); capture(window, Path.Combine(output, "emoji-reading.png"));
            if (colored < 30) throw new InvalidOperationException($"正文 emoji 仍为单色：彩色像素 {colored}");
            var samples = "普通文字 😀 ❤️ 👍🏽 👨‍👩‍👧‍👦 🇨🇳 1️⃣\r\n第二行😅\r\n\r\n末尾🌈\r\n";
            body.Html = System.Net.WebUtility.HtmlEncode(samples).Replace("\r\n", "<br>");
            Require(EmojiText.Read(body.Document) == samples, "正文还原 emoji 或换行丢失");
            body.FontSize = 16; await Task.Delay(50); var small = ColorPixels(body);
            body.FontSize = 24; await Task.Delay(50); var large = ColorPixels(body);
            Require(large > small, "emoji 大小没有随正文字号变化");
            body.SelectAll();
            var copied = new DataObject(); var copyArgs = new DataObjectCopyingEventArgs(copied, false);
            body.RaiseEvent(copyArgs);
            Require(copyArgs.CommandCancelled && Clipboard.GetText() == samples.TrimEnd('\r', '\n') + "\r\n\r\n", "正文复制没有还原 Unicode 和文档换行");
            body.Html = "<b>😀</b><a href=\"https://www.nmbxd.com/t/1\">链接❤️</a>[h]隐藏🌈[/h]";
            Require(!EmojiText.Read(body.Document).Contains("隐藏🌈"), "隐藏内容提前进入正文");
            var paragraph = (Paragraph)body.Document.Blocks.FirstBlock;
            var spoiler = paragraph.Inlines.OfType<SpoilerSpan>().Last();
            spoiler.Reveal();
            Require(EmojiText.Read(body.Document).Contains("隐藏🌈") && paragraph.Inlines.OfType<Span>().Any(s => s.Inlines.OfType<Emoji.Wpf.EmojiInline>().Any()), "展开隐藏 emoji 失败");
            var label = new EmojiLabel { Text = "😀", Foreground = Brushes.Gray, Background = Brushes.White, FontSize = 36 };
            window.Content = label; await Task.Delay(60);
            Require(ColorPixels(label) > 30, "列表/菜单文本的前景色覆盖了 emoji 颜色");
            var editor = new EmojiEditor { Text = samples, FontSize = 20, MinHeight = 180 };
            window.Content = editor; await Task.Delay(100);
            Require(editor.Text == samples, "编辑器原文或尾部空行丢失");
            Require(ColorPixels(editor) > 30, "输入区 emoji 仍为单色");
            editor.Select(0, editor.Text.Length); Require(editor.SelectedText == samples, "全选后的原文不完整");
            editor.Text = "甲😀乙\r\n丙👍🏽丁";
            editor.Select(1, "😀乙\r\n丙👍🏽".Length);
            Require(editor.SelectedText == "😀乙\r\n丙👍🏽", "跨行选区 UTF-16 偏移错误");
            editor.BeginChange(); editor.SelectedText = "替换🌈"; editor.EndChange();
            Require(editor.Text == "甲替换🌈丁", "选区替换错误");
            editor.Undo(); Require(editor.Text == "甲😀乙\r\n丙👍🏽丁", "一次撤销未恢复原文");
            editor.Redo(); Require(editor.Text == "甲替换🌈丁", "重做未恢复原文");
            editor.Text = "👨";
            foreach (var suffix in new[] { "‍", "👩", "‍", "👧", "‍", "👦" })
            { editor.Select(editor.Text.Length, 0); editor.SelectedText = suffix; }
            Require(editor.Text == "👨‍👩‍👧‍👦" && CountEmoji(editor.Document) == 1, "分段输入家庭序列未合并或损坏");
            var family = editor.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Emoji.Wpf.EmojiInline>().Single();
            var face = new Emoji.Wpf.EmojiInline { Text = "😀", FontSize = editor.FontSize };
            Require(family.Child.Width <= face.Child.Width * 1.3, "家庭组合仍被拆成多个独立表情");
            const string arrows = "↙(`ヮ´ )↗ 开摆！ ← ↑ → ↓ ↔ ↕ ↖ ↗ ↘ ↙";
            editor.Text = arrows; Require(editor.Text == arrows && CountEmoji(editor.Document) == 0, "颜文字的箭头被转换成彩色 emoji");
            body.Html = System.Net.WebUtility.HtmlEncode(arrows); Require(CountEmoji(body.Document) == 0, "阅读区箭头被转换");
            label.Text = arrows; Require(!label.Inlines.OfType<Emoji.Wpf.EmojiInline>().Any(), "菜单/列表箭头被转换");
            editor.Text = "↗️😀︎🫠";
            Require(CountEmoji(editor.Document) == 2, "显式彩色箭头、文本呈现选择符或新 emoji 识别错误");
            editor.Text = arrows + "\r\n👨‍👩‍👧‍👦 👩🏽‍💻 🫠 😀\r\n↗️（显式表情）";
            window.Width = 620; window.Height = 240; await Task.Delay(80); capture(window, Path.Combine(output, "emoji-symbols-and-sequences.png"));
            var timer = Stopwatch.StartNew(); editor.Text = new string('中', 9999); timer.Stop();
            Require(timer.ElapsedMilliseconds < 2000, "普通长文本显示过慢");
            editor.Select(editor.Text.Length, 0);
            var rejectedPaste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, "😀"), false, DataFormats.UnicodeText);
            editor.RaiseEvent(rejectedPaste); Require(rejectedPaste.CommandCancelled, "粘贴超长 emoji 未拒绝");
            editor.Text = "";
            var paste = new DataObjectPastingEventArgs(new DataObject(DataFormats.UnicodeText, samples), false, DataFormats.UnicodeText);
            editor.RaiseEvent(paste); Require(!paste.CommandCancelled && paste.FormatToApply == DataFormats.UnicodeText, "纯文本粘贴被拒绝");
            editor.SelectedText = (string)paste.DataObject.GetData(DataFormats.UnicodeText);
            Require(editor.Text == samples, "粘贴换行或 emoji 丢失");
            await Task.Delay(80); window.UpdateLayout(); capture(window, Path.Combine(output, "emoji-editor-light.png"));
            App.ApplyTheme("dark"); await Task.Delay(80); capture(window, Path.Combine(output, "emoji-editor-dark.png")); App.ApplyTheme("light");
            File.WriteAllText(Path.Combine(output, "emoji-results.json"), JsonSerializer.Serialize(new { ColoredPixels = colored, Small = small, Large = large, PlainTextMilliseconds = timer.ElapsedMilliseconds, TextRoundTrip = true, Selection = true, UndoRedo = true, IncrementalSequence = true, CopyData = true, Paste = true, LengthLimit = true, Spoiler = true, LabelColor = true, PlainArrows = true, PresentationSelectors = true, FamilyLayout = true }));
        }
        finally { window.Close(); }
    }
    private static int CountEmoji(FlowDocument document) => document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).Count(i => i is Emoji.Wpf.EmojiInline);
    private static IEnumerable<GlyphRunDrawing> GlyphDrawings(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph;
        if (drawing is DrawingGroup group) foreach (var child in group.Children) foreach (var glyphChild in GlyphDrawings(child)) yield return glyphChild;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

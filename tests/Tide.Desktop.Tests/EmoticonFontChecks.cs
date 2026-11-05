using System.IO;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class EmoticonFontChecks
{
    internal static List<string> MissingGlyphs(Visual root)
    {
        var missing = new List<string>();
        void Drawing(Drawing? drawing)
        {
            if (drawing is DrawingGroup group) foreach (var child in group.Children) Drawing(child);
            if (drawing is GlyphRunDrawing glyph && glyph.GlyphRun.GlyphIndices.Contains((ushort)0))
                missing.Add(new string(glyph.GlyphRun.Characters?.ToArray() ?? []) + " [glyphs " + string.Join(",", glyph.GlyphRun.GlyphIndices) + "; " + glyph.GlyphRun.GlyphTypeface.FontUri + "]");
        }
        void Visuals(Visual node)
        {
            Drawing(VisualTreeHelper.GetDrawing(node));
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (VisualTreeHelper.GetChild(node, i) is Visual child) Visuals(child);
        }
        Visuals(root); return missing;
    }
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var picker = new ComposerWindow(owner.Model, 0) { Owner = owner, ShowInTaskbar = false };
        try
        {
            picker.Show();
            var preview = UiRefinementChecks.Descendants<EmojiEditor>(picker).Single(t => AutomationProperties.GetName(t) == "发言正文");
            preview.Text = "颜文字与正文 · 微软雅黑\r\n(=ﾟωﾟ)=  (ﾟ∀ﾟ)  (´･ω･`)  ↙(`ヮ´ )↗ 开摆！\r\n(`ᝫ´ )  ₍₍(ง`ᝫ´ )ว⁾\r\n/̵͇̿̿/’̿’̿ ̿ ̿̿ ̿̿ ̿̿\r\n( ﾟᯅ 。)  ҉( ﾟ∀。)\r\nฅ ง ว ໒ ༝";
            var sample = preview.Text;
            await Task.Delay(200); capture(picker, Path.Combine(output, "font-rendering.png"));
            var missing = MissingGlyphs(preview);
            var catalogMissing = new List<string>();
            foreach (var entry in EmoticonCatalog.Entries)
            {
                // 检查真实 TextBox 绘制，长图案逐屏滚动；独立 FormattedText 的段落/组合符号处理与 TextBox 不同。
                preview.Text = entry.Text; preview.ScrollToHome(); preview.UpdateLayout(); await Task.Delay(20);
                while (true)
                {
                    var found = MissingGlyphs(preview);
                    if (found.Count > 0) catalogMissing.Add(entry.Display + ": " + string.Join(" / ", found));
                    if (preview.VerticalOffset + preview.ViewportHeight >= preview.ExtentHeight - 1) break;
                    var previous = preview.VerticalOffset;
                    preview.ScrollToVerticalOffset(previous + preview.ViewportHeight); preview.UpdateLayout(); await Task.Delay(20);
                    if (preview.VerticalOffset <= previous) break;
                }
            }
            preview.Text = sample;
            var reading = new TextWindow("颜文字字体验证", WebUtility.HtmlEncode(preview.Text).Replace("\r\n", "<br>")) { Owner = picker, ShowInTaskbar = false };
            reading.Show(); await Task.Delay(100);
            var postBody = UiRefinementChecks.Descendants<PostBody>(reading).Single();
            var readingMissing = MissingGlyphs(postBody);
            capture(reading, Path.Combine(output, "font-reading.png")); reading.Close();
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); picker.Emoticons.Toggle(); await Task.Delay(100);
                EmoticonChecks.CapturePopup(picker, Path.Combine(output, "emoticons-" + theme + ".png"));
                picker.Emoticons.Toggle();
            }
            var fontNames = Fonts.SystemFontFamilies.Where(f => new Typeface(f, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal).TryGetGlyphTypeface(out var g) && g.CharacterToGlyphMap.ContainsKey(0x176B)).Select(f => f.Source).ToArray();
            File.WriteAllText(Path.Combine(output, "font-probe.json"), JsonSerializer.Serialize(new { preview.Text, Font = preview.FontFamily.Source, Language = preview.Language.IetfLanguageTag, Missing = missing, ReadingMissing = readingMissing, CatalogEntries = EmoticonCatalog.Entries.Count, CatalogMissing = catalogMissing, InstalledFontsFor176B = fontNames }));
            if (missing.Count > 0) throw new InvalidOperationException("颜文字实际绘制了缺字方框：" + string.Join(" / ", missing));
            if (readingMissing.Count > 0 || catalogMissing.Count > 0) throw new InvalidOperationException("阅读区或颜文字库仍有缺字：" + string.Join(" / ", readingMissing.Concat(catalogMissing)));
        }
        finally { picker.Close(); App.ApplyTheme("light"); }
    }
}

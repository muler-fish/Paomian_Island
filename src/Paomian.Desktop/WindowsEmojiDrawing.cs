using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Paomian.Desktop;

/// <summary>复杂组合使用 Windows 的字形位置，再按系统字体的 COLR/CPAL 颜色层绘制。</summary>
internal static class WindowsEmojiDrawing
{
    private static readonly HashSet<string> Prepared = [];
    private static readonly Lazy<Typography.OpenFont.Typeface?> Font = new(() =>
    {
        if (!new Typeface("Segoe UI Emoji").TryGetGlyphTypeface(out var glyphTypeface)) return null;
        using var stream = glyphTypeface.GetFontStream();
        return new Typography.OpenFont.OpenFontReader().Read(stream, 0, Typography.OpenFont.ReadFlags.Full);
    });

    internal static void Prepare(string text)
    {
        if (!text.Contains('\u200d') || !Prepared.Add(text)) return;
        var font = Font.Value;
        if (font?.COLRTable is null || font.CPALTable is null) return;
        // 原库的排字在较新 Windows 的家庭组合上会丢失叠放位置；系统排字保留其偏移。
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"), 64, Brushes.Black, 1), new Point());
        var glyphs = Glyphs(VisualTreeHelper.GetDrawing(visual)).ToArray();
        if (glyphs.Length == 0 || glyphs.Any(g => g.GlyphIndices.Contains((ushort)0) || !g.GlyphTypeface.FamilyNames.Values.Contains("Segoe UI Emoji"))) return;
        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
        {
            foreach (var run in glyphs)
            {
                double advance = 0;
                for (var index = 0; index < run.GlyphIndices.Count; index++)
                {
                    var id = run.GlyphIndices[index];
                    var offset = run.GlyphOffsets?[index] ?? new Point();
                    context.PushTransform(new TranslateTransform(run.BaselineOrigin.X + advance + offset.X, run.BaselineOrigin.Y - offset.Y));
                    if (font.COLRTable.LayerIndices.TryGetValue(id, out var first))
                    {
                        for (var layer = first; layer < first + font.COLRTable.LayerCounts[id]; layer++)
                        {
                            var palette = font.COLRTable.GlyphPalettes[layer];
                            Brush brush = Brushes.Black;
                            if (palette != 0xffff)
                            {
                                font.CPALTable.GetColor(font.CPALTable.Palettes[0] + palette, out var r, out var g, out var b, out var a);
                                brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                            }
                            Draw(font.COLRTable.GlyphLayers[layer], brush);
                        }
                    }
                    else Draw(id, Brushes.Black);
                    context.Pop(); advance += run.AdvanceWidths[index];
                    void Draw(ushort glyph, Brush brush)
                    {
                        var layerRun = new GlyphRun(run.GlyphTypeface, run.BidiLevel, run.IsSideways, run.FontRenderingEmSize, 1f,
                            new[] { glyph }, new Point(), new[] { 0.0 }, null, null, null, null, null, null);
                        context.DrawGeometry(brush, null, layerRun.BuildGeometry());
                    }
                }
            }
        }
        if (drawing.Bounds.IsEmpty) return;
        drawing.Freeze(); Emoji.Wpf.EmojiData.RegisterDrawing(text, drawing);
    }

    private static IEnumerable<GlyphRun> Glyphs(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph) yield return glyph.GlyphRun;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var run in Glyphs(child)) yield return run;
    }
}

using System.Windows.Documents;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace Tide.Desktop;

internal static class ColorEmoji
{
    // 按整段匹配 Unicode 序列，普通中文/ASCII 快速通过；不逐字符修改文档。
    internal static void Render(InlineCollection inlines)
    {
        foreach (var inline in inlines.ToArray())
        {
            if (inline is SpoilerSpan { Hidden: true }) continue;
            if (inline is Span span) { Render(span.Inlines); continue; }
            if (inline is not Run run) continue;
            var matches = EmojiSequences.Matches(run.Text);
            if (matches.Length == 0) continue;
            var position = 0;
            foreach (Match match in matches)
            {
                if (match.Index > position) inlines.InsertBefore(run, new Run(run.Text[position..match.Index]));
                WindowsEmojiDrawing.Prepare(match.Value);
                // FontSize 从正文继承；固定黑色仅表示不对 emoji 自身颜色施加主题染色。
                inlines.InsertBefore(run, new Emoji.Wpf.EmojiInline { Text = match.Value, Foreground = Brushes.Black });
                position = match.Index + match.Length;
            }
            if (position < run.Text.Length) inlines.InsertBefore(run, new Run(run.Text[position..]));
            inlines.Remove(run);
        }
    }
}

using System.Text;
using System.Windows.Documents;

namespace Tide.Desktop;

/// <summary>彩色图像只是显示层；复制、草稿和提交始终还原为 Unicode 文本。</summary>
internal static class EmojiText
{
    internal sealed record Segment(TextPointer Start, TextPointer End, string Text);

    internal static IEnumerable<Segment> Segments(TextPointer start, TextPointer end)
    {
        for (var position = start; position is not null && position.CompareTo(end) < 0;)
        {
            var hidden = SpoilerSpan.HiddenAncestor(position.Parent)
                ?? (position.GetAdjacentElement(LogicalDirection.Forward) as SpoilerSpan is { Hidden: true } entering ? entering : null);
            if (hidden is not null)
            {
                var after = hidden.ElementEnd.CompareTo(end) < 0 ? hidden.ElementEnd : end;
                yield return new(position, after, "[隐藏内容]"); position = after; continue;
            }
            var next = position.GetNextContextPosition(LogicalDirection.Forward);
            if (next is null) yield break;
            if (next.CompareTo(end) > 0) next = end;
            var kind = position.GetPointerContext(LogicalDirection.Forward);
            if (kind == TextPointerContext.Text)
                yield return new(position, next, new TextRange(position, next).Text);
            else if (kind == TextPointerContext.ElementStart)
            {
                if (position.GetAdjacentElement(LogicalDirection.Forward) is Emoji.Wpf.EmojiInline emoji)
                {
                    var after = emoji.ElementEnd;
                    if (after.CompareTo(end) <= 0) { yield return new(position, after, emoji.Text); next = after; }
                }
                else if (position.GetAdjacentElement(LogicalDirection.Forward) is LineBreak line)
                { yield return new(position, line.ElementEnd, "\r\n"); next = line.ElementEnd; }
            }
            else if (kind == TextPointerContext.ElementEnd && position.GetAdjacentElement(LogicalDirection.Forward) is Paragraph)
                yield return new(position, next, "\r\n");
            position = next;
        }
    }

    internal static string Read(TextPointer start, TextPointer end)
    {
        var text = new StringBuilder(); foreach (var segment in Segments(start, end)) text.Append(segment.Text);
        return text.ToString();
    }

    internal static string Read(FlowDocument document)
    {
        var text = Read(document.ContentStart, document.ContentEnd);
        return text.EndsWith("\r\n") ? text[..^2] : text;
    }

    internal static TextPointer At(FlowDocument document, int offset)
    {
        foreach (var segment in Segments(document.ContentStart, document.ContentEnd))
        {
            if (offset <= 0) return segment.Start;
            if (offset < segment.Text.Length)
                return segment.Start.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text
                    ? segment.Start.GetPositionAtOffset(offset)! : segment.End;
            offset -= segment.Text.Length;
            if (offset == 0) return segment.End;
        }
        return document.ContentEnd;
    }
}

using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Tide.Desktop;

// Keep the original glyph advances, formatting and line breaks while masking the paint.
internal sealed class SpoilerSpan : Span
{
    private readonly string _html;
    internal bool Hidden { get; private set; } = true;
    internal SpoilerSpan(string html, bool interactive)
    {
        _html = html;
        Focusable = interactive; Cursor = interactive ? Cursors.Hand : null;
        ToolTip = interactive ? "点击显示防剧透内容" : null;
        System.Windows.Automation.AutomationProperties.SetName(this, "显示防剧透内容");
        PostBody.AppendHtml(Inlines, html); ColorEmoji.Render(Inlines);
        ReadingTypography.Space(Inlines, ReadingTypography.CurrentSpacing);
        Mask(this);
        if (!interactive) return;
        PreviewMouseLeftButtonDown += (_, e) => { if (Hidden) { Reveal(); e.Handled = true; } };
        PreviewKeyDown += (_, e) => { if (Hidden && e.Key is Key.Enter or Key.Space) { Reveal(); e.Handled = true; } };
    }
    private static void Mask(Inline inline)
    {
        inline.Foreground = Brushes.Transparent;
        inline.Background = Brushes.Black;
        // Decorations carry separate pens; suppress their paint, not font metrics.
        inline.TextDecorations = null;
        if (inline is Hyperlink link) { link.Focusable = false; link.ToolTip = null; }
        if (inline is InlineUIContainer { Child: { } child }) child.Opacity = 0;
        if (inline is Span span) foreach (var nested in span.Inlines) Mask(nested);
    }
    internal void Reveal()
    {
        if (!Hidden) return;
        Hidden = false; Inlines.Clear();
        ClearValue(ForegroundProperty); ClearValue(BackgroundProperty); ClearValue(TextDecorationsProperty);
        Cursor = null; ToolTip = null; Focusable = false;
        PostBody.AppendHtml(Inlines, _html); ColorEmoji.Render(Inlines);
        ReadingTypography.Space(Inlines, ReadingTypography.CurrentSpacing);
    }
    internal static SpoilerSpan? HiddenAncestor(DependencyObject? value)
    {
        while (value is not null)
        {
            if (value is SpoilerSpan { Hidden: true } spoiler) return spoiler;
            value = value is FrameworkContentElement content ? content.Parent : LogicalTreeHelper.GetParent(value);
        }
        return null;
    }
}

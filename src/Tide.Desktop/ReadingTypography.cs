using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>Shared reading metrics. Tracking is layout-only: no extra characters enter copied or sent text.</summary>
internal static class ReadingTypography
{
    public static readonly DependencyProperty LineHeightProperty = DependencyProperty.RegisterAttached(
        "LineHeight", typeof(double), typeof(ReadingTypography), new FrameworkPropertyMetadata(1.85, Changed));
    public static readonly DependencyProperty LetterSpacingProperty = DependencyProperty.RegisterAttached(
        "LetterSpacing", typeof(double), typeof(ReadingTypography), new FrameworkPropertyMetadata(0d, Changed));
    public static double GetLineHeight(DependencyObject value) => (double)value.GetValue(LineHeightProperty);
    public static void SetLineHeight(DependencyObject value, double height) => value.SetValue(LineHeightProperty, height);
    public static double GetLetterSpacing(DependencyObject value) => (double)value.GetValue(LetterSpacingProperty);
    public static void SetLetterSpacing(DependencyObject value, double spacing) => value.SetValue(LetterSpacingProperty, spacing);
    private static void Changed(DependencyObject value, DependencyPropertyChangedEventArgs args)
    {
        if (value is PostBody body) body.RefreshTypography(args.Property == LetterSpacingProperty);
        if (value is PostPreview preview) preview.RefreshTypography(args.Property == LetterSpacingProperty);
    }
    public static void Bind(FrameworkElement target)
    {
        target.SetResourceReference(LineHeightProperty, "ReadingLineHeight");
        target.SetResourceReference(LetterSpacingProperty, "ReadingLetterSpacing");
    }
    public static void Apply(UserState state)
    {
        if (Application.Current is not { } app) return;
        if (!Equals(app.Resources["ReadingLineHeight"], state.ReadingLineHeight)) app.Resources["ReadingLineHeight"] = state.ReadingLineHeight;
        if (!Equals(app.Resources["ReadingLetterSpacing"], state.ReadingLetterSpacing)) app.Resources["ReadingLetterSpacing"] = state.ReadingLetterSpacing;
    }
    internal static double CurrentSpacing => Application.Current?.TryFindResource("ReadingLetterSpacing") is double value ? value : 0;

    internal static void Space(InlineCollection inlines, double amount)
    {
        if (amount <= 0) return;
        foreach (var inline in inlines.ToArray())
        {
            // Spoilers build their own spacing before masking, and use the same layout when revealed.
            if (inline is SpoilerSpan) continue;
            if (inline is Span span) { Space(span.Inlines, amount); continue; }
            if (inline is not Run run || run.Text.Length < 2) continue;
            var elements = StringInfo.GetTextElementEnumerator(run.Text);
            var pieces = new List<string>();
            while (elements.MoveNext()) pieces.Add(elements.GetTextElement());
            if (pieces.Count < 2) continue;
            for (var index = 0; index < pieces.Count; index++)
            {
                inlines.InsertBefore(run, new Run(pieces[index]));
                if (index == pieces.Count - 1 || pieces[index].Contains('\n') || pieces[index+1].Contains('\n') || pieces[index].Contains('\r') || pieces[index+1].Contains('\r')) continue;
                var gap = new InlineUIContainer { BaselineAlignment = BaselineAlignment.Baseline };
                var space = new Border { Width = amount, Focusable = false, IsHitTestVisible = false };
                space.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(TextElement.FontSize)) { Source = gap });
                gap.Child = space; inlines.InsertBefore(run, gap);
            }
            inlines.Remove(run);
        }
    }
}

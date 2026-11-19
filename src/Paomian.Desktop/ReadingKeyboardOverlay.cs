using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Paomian.Desktop;

internal sealed record ReadingHintTarget(DependencyObject Source, object? Context, Rect Bounds, Rect Clip, Action Invoke, bool IsRow = false)
{
    internal bool SameTarget(ReadingHintTarget other) => Source == other.Source && ReferenceEquals(Context, other.Context) &&
        Math.Abs(Bounds.X - other.Bounds.X) < 1 && Math.Abs(Bounds.Y - other.Bounds.Y) < 1 &&
        Math.Abs(Bounds.Width - other.Bounds.Width) < 1 && Math.Abs(Bounds.Height - other.Bounds.Height) < 1;
}

internal sealed record ReadingHint(ReadingHintTarget Target, string Code, Rect Label);

/// <summary>栏位标记和提示不占布局，也不改变正文或可操作控件的焦点。</summary>
public sealed class ReadingKeyboardOverlay : FrameworkElement
{
    private const string Letters = "ASDFGHJKLWER";
    private readonly Typeface _typeface = new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private FrameworkElement? _pane, _navigationRow;
    private ReadingHint[] _hints = [];
    private string _prefix = "";

    internal IReadOnlyList<ReadingHint> Hints => _hints;
    internal FrameworkElement? ActivePane => _pane;

    internal void ShowFocus(FrameworkElement? pane, FrameworkElement? row)
    {
        _pane = pane; _navigationRow = row;
        InvalidateVisual();
    }

    internal void ShowHints(ReadingHint[] hints, string prefix)
    {
        _hints = hints; _prefix = prefix;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var accent = (Brush)FindResource("Accent");
        if (_pane is { IsVisible: true } pane)
        {
            var bounds = pane.TransformToVisual(this).TransformBounds(new Rect(pane.RenderSize));
            drawing.DrawRectangle(accent, null, new Rect(bounds.Left, bounds.Top, bounds.Width, 2));
        }
        if (_navigationRow is { IsVisible: true, IsKeyboardFocusWithin: true } row && _pane is not null)
        {
            var bounds = row.TransformToVisual(this).TransformBounds(new Rect(row.RenderSize));
            var scroll = ScrollAncestor(row);
            if (scroll is not null) bounds.Intersect(scroll.TransformToVisual(this).TransformBounds(new Rect(scroll.RenderSize)));
            if (bounds.Height >= 8) drawing.DrawRectangle(accent, null, new Rect(bounds.X, bounds.Y + 4, 3, bounds.Height - 8));
        }

        if (_hints.Length == 0) return;
        var paper = (Brush)FindResource(_prefix.Length > 0 ? "OnAccent" : "Surface");
        var ink = _prefix.Length > 0 ? accent : (Brush)FindResource("Text");
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var matches = _hints.Where(hint => hint.Code.StartsWith(_prefix, StringComparison.Ordinal)).ToArray();
        var leader = new Pen((Brush)FindResource("Muted"), 1);
        foreach (var hint in matches.Where(h => !h.Target.IsRow))
        {
            var label = hint.Label;
            var target = hint.Target.Bounds;
            var labelPoint = new Point(label.Left + label.Width / 2, label.Bottom);
            var near = new Point(Math.Clamp(labelPoint.X, target.Left, target.Right), Math.Clamp(labelPoint.Y, target.Top, target.Bottom));
            if ((near - labelPoint).Length > 7) drawing.DrawLine(leader, labelPoint, near);
        }
        foreach (var hint in matches)
        {
            var text = new FormattedText(hint.Code, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface, 13, paper, dpi);
            if (_prefix.Length > 0) text.SetTextDecorations(TextDecorations.Underline, 0, _prefix.Length);
            drawing.DrawRoundedRectangle(ink, null, hint.Label, 3, 3);
            drawing.DrawText(text, hint.Label.Location + new Vector((hint.Label.Width - text.Width) / 2, (hint.Label.Height - text.Height) / 2));
        }
    }

    private static ScrollContentPresenter? ScrollAncestor(Visual element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ScrollContentPresenter presenter) return presenter;
        return null;
    }

    internal static ReadingHint[] PlaceHints(ReadingHintTarget[] targets, Rect pane)
    {
        var length = 1;
        for (long capacity = Letters.Length; capacity < targets.Length; capacity *= Letters.Length) length++;
        var hints = new List<ReadingHint>(targets.Length);
        var inlineText = targets.Where(target => target.Source is Inline).Select(target => target.Bounds).ToArray();
        for (var i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            var code = string.Create(length, i, (characters, number) =>
            {
                for (var position = characters.Length - 1; position >= 0; position--)
                { characters[position] = Letters[number % Letters.Length]; number /= Letters.Length; }
            });
            const double height = 19;
            var width = 10 + 8 * length;
            var x = target.IsRow ? target.Bounds.Right - width - 7 : target.Bounds.Left;
            var y = target.IsRow ? target.Bounds.Top + 3 : target.Bounds.Top - height;
            var bounds = FindSpace(target.Clip) ?? FindSpace(pane);
            if (bounds is { } label) hints.Add(new(target, code, label));

            Rect? FindSpace(Rect area)
            {
                if (area.Width < width + 2 || area.Height < height + 2) return null;
                Rect At(double left, double top) => new(Math.Clamp(left, area.Left + 1, area.Right - width - 1),
                    Math.Clamp(top, area.Top + 1, area.Bottom - height - 1), width, height);
                bool Free(Rect candidate, bool avoidText = false)
                {
                    candidate.Inflate(1, 1);
                    return !hints.Any(hint => hint.Label.IntersectsWith(candidate)) && (!avoidText || !inlineText.Any(candidate.IntersectsWith));
                }
                var ideal = At(x, y);
                if (target.Source is Inline)
                {
                    // 优先使用行首留白、链接之间或行尾的空处。
                    var center = target.Bounds.Top + (target.Bounds.Height - height) / 2;
                    foreach (var candidate in new[] { At(target.Bounds.Left - width - 3, center), At(target.Bounds.Right + 3, center) })
                        if (Free(candidate, avoidText: true)) return candidate;
                    var atStart = At(target.Bounds.Left - width + 8, center);
                    if (Free(atStart) && !inlineText.Any(text => text != target.Bounds && text.IntersectsWith(atStart))) return atStart;
                    if (Free(ideal, avoidText: true)) return ideal;
                }
                if (Free(ideal)) return ideal;
                for (var row = 0; row <= 3; row++)
                {
                    var dy = (1 - 2 * (row % 2)) * ((row + 1) / 2) * (height + 2);
                    for (var col = -2; col <= 2; col++)
                    {
                        var nearby = At(x + col * (width + 2), y + dy);
                        if (Free(nearby)) return nearby;
                    }
                }
                for (var top = area.Top + 1; top + height < area.Bottom; top += height + 2)
                    for (var left = area.Left + 1; left + width < area.Right; left += width + 2)
                    {
                        var cell = new Rect(left, top, width, height);
                        if (Free(cell)) return cell;
                    }
                return null;
            }
        }
        return [.. hints];
    }
}

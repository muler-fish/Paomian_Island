using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Paomian.Desktop;

public partial class MainWindow
{
    private FrameworkElement? _hintPane;
    private ReadingHint[] _readingHints = [];
    private string _hintPrefix = "";

    private bool HandleReadingHint(Key key, bool repeat, FrameworkElement pane)
    {
        if (_hintPane is null)
        {
            if (key != Key.F) return false;
            if (!repeat) ShowReadingHints(pane);
            return true;
        }
        if (key == Key.Escape) { ClearReadingHints(); return true; }
        if (key == Key.Enter || key == Key.Space) return true;
        if (key is not (>= Key.A and <= Key.Z or Key.Back)) { ClearReadingHints(); return false; }
        if (repeat) return true;

        if (key == Key.Back)
        {
            if (_hintPrefix.Length > 0) _hintPrefix = _hintPrefix[..^1];
        }
        else
        {
            var prefix = _hintPrefix + (char)('A' + (key - Key.A));
            if (_readingHints.FirstOrDefault(hint => hint.Code == prefix) is { } match)
            {
                // 续页、缩略图及文档重建都可能在两个按键之间换掉容器或目标。
                ContentColumns.UpdateLayout();
                var action = _hintPane == pane ? CurrentHintTargets(pane).FirstOrDefault(target => target.SameTarget(match.Target)) : null;
                ClearReadingHints();
                action?.Invoke();
                return true;
            }
            if (_readingHints.Any(hint => hint.Code.StartsWith(prefix, StringComparison.Ordinal))) _hintPrefix = prefix;
        }
        ReadingKeyboardLayer.ShowHints(_readingHints, _hintPrefix);
        return true;
    }

    private void ShowReadingHints(FrameworkElement pane)
    {
        _keyboardScroll.Stop();
        InertialScroll.Stop(pane == ListPane ? ThreadList : ReaderList);
        ContentColumns.UpdateLayout();
        var targets = CurrentHintTargets(pane).ToArray();
        if (targets.Length == 0) return;
        _readingHints = ReadingKeyboardOverlay.PlaceHints(targets, BoundsInKeyboardLayer(pane));
        if (_readingHints.Length == 0) return;
        _hintPane = pane;
        ReadingKeyboardLayer.ShowHints(_readingHints, "");
        LayoutUpdated += ValidateReadingHints;
    }

    private void ClearReadingHints()
    {
        if (_hintPane is null) return;
        LayoutUpdated -= ValidateReadingHints;
        _hintPane = null;
        _readingHints = [];
        _hintPrefix = "";
        ReadingKeyboardLayer.ShowHints([], "");
    }

    private void ValidateReadingHints(object? sender, EventArgs e)
    {
        if (_hintPane is not { } pane) return;
        var targets = CurrentHintTargets(pane).ToDictionary(target => target.Source);
        if (_readingHints.Any(hint => !targets.TryGetValue(hint.Target.Source, out var current) || !current.SameTarget(hint.Target)))
            ClearReadingHints();
    }

    private IEnumerable<ReadingHintTarget> CurrentHintTargets(FrameworkElement pane)
    {
        var list = pane == ListPane ? ThreadList : ReaderList;
        var paneBounds = Rect.Intersect(new Rect(ReadingKeyboardLayer.RenderSize), BoundsInKeyboardLayer(pane));
        var viewer = ReadingAnchor.Children<ScrollViewer>(list).FirstOrDefault();
        var viewport = viewer?.Template?.FindName("PART_ScrollContentPresenter", viewer) as FrameworkElement;
        var scrollBounds = viewport is null ? Rect.Empty : Rect.Intersect(paneBounds, BoundsInKeyboardLayer(viewport));
        return CollectHintTargets(pane, paneBounds, scrollBounds);

        IEnumerable<ReadingHintTarget> CollectHintTargets(DependencyObject parent, Rect clip, Rect viewportClip)
        {
            if (parent is FrameworkElement element)
            {
                if (!element.IsVisible || !element.IsEnabled || !element.IsHitTestVisible || element.Opacity <= 0) yield break;
                if (element == viewport) clip = viewportClip;
                if (element == ReaderFooter) clip.Intersect(BoundsInKeyboardLayer(element));
                if (element == ReaderHeaderActions && Grid.GetRow(ReaderHeaderActions) == 1)
                {
                    var toolbar = BoundsInKeyboardLayer(element);
                    toolbar.Y -= 8; toolbar.Height += 8;
                    clip.Intersect(toolbar);
                }
                if (element.ClipToBounds) clip.Intersect(BoundsInKeyboardLayer(element));
                if (VisualTreeHelper.GetClip(element) is { } geometry)
                    clip.Intersect(element.TransformToVisual(ReadingKeyboardLayer).TransformBounds(geometry.Bounds));
                if (clip.IsEmpty) yield break;
                var bounds = Rect.Intersect(clip, BoundsInKeyboardLayer(element));

                switch (element)
                {
                    case ListBoxItem row:
                        // 只看当前实现的容器；缓存区中完全离开视口的行不能进入候选。
                        if (bounds.IsEmpty) yield break;
                        if (pane == ListPane && ThreadList.ItemContainerGenerator.ItemFromContainer(row) is PostCard card && ReferenceEquals(row.DataContext, card)
                            && bounds.Height >= 10)
                            yield return new(row, card, bounds, clip, () => OpenKeyboardThread(row), IsRow: true);
                        break;
                    case ButtonBase button:
                        if (bounds.Width >= 10 && bounds.Height >= 10 && CanInvokeKeyboardButton(button) &&
                            (button != BackToTopButton || viewer?.VerticalOffset > .01))
                            yield return new(button, button.DataContext, bounds, clip, () => InvokeKeyboardButton(button));
                        yield break;
                    case PostBody body when pane == ReaderPane:
                        if (!bounds.IsEmpty)
                            foreach (var inline in InlineHintTargets(body, bounds, clip)) yield return inline;
                        yield break;
                    case TextBoxBase or ComboBox:
                        if ((element == PageBox || element == SubscriptionSelector) && bounds.Width >= 10 && bounds.Height >= 10)
                            yield return new(element, element.DataContext, bounds, clip, () => element.Focus());
                        yield break;
                    case ScrollBar:
                        yield break;
                }
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
                foreach (var target in CollectHintTargets(VisualTreeHelper.GetChild(parent, index), clip, viewportClip)) yield return target;
        }
    }

    private Rect BoundsInKeyboardLayer(FrameworkElement element) => element.TransformToVisual(ReadingKeyboardLayer).TransformBounds(new Rect(element.RenderSize));

    private IEnumerable<ReadingHintTarget> InlineHintTargets(PostBody body, Rect visibleBody, Rect clip)
    {
        if (!body.IsDocumentEnabled || body.Template?.FindName("PART_ContentHost", body) is not ScrollViewer { Content: FrameworkElement textView }) yield break;
        var transform = textView.TransformToVisual(ReadingKeyboardLayer);
        if (transform.Inverse is not { } inverse) yield break;
        var localBounds = inverse.TransformBounds(visibleBody);
        var firstLine = body.GetPositionFromPoint(ReadingKeyboardLayer.TranslatePoint(visibleBody.TopLeft + new Vector(1, 1), body), true)?.GetLineStartPosition(0);

        foreach (var paragraph in body.Document.Blocks.OfType<Paragraph>())
        foreach (var inline in Actions(paragraph.Inlines))
        {
            var start = inline.ContentStart;
            var end = inline.ContentEnd;
            if (firstLine is not null && end.CompareTo(firstLine) <= 0) continue;
            if (firstLine is not null && start.CompareTo(firstLine) < 0) start = firstLine;
            for (var line = start; line is not null && line.CompareTo(end) < 0;)
            {
                var inner = line.GetCharacterRect(LogicalDirection.Forward);
                if (inner.IsEmpty || inner.Y >= localBounds.Bottom) break;
                var nextLine = line.GetLineStartPosition(1);
                var lineEnd = nextLine is not null && nextLine.CompareTo(end) < 0 ? nextLine : end;
                var far = lineEnd.GetCharacterRect(LogicalDirection.Backward);
                if (!far.IsEmpty && Math.Abs(far.Y - inner.Y) < inner.Height / 2) inner.Union(far);
                inner.Width = Math.Max(8, inner.Width);
                var bounds = Rect.Intersect(visibleBody, transform.TransformBounds(inner));
                if (bounds.Height >= 4 && bounds.Width >= 2)
                {
                    yield return new(inline, body.DataContext, bounds, clip, inline is SpoilerSpan spoiler
                        ? spoiler.Reveal : () => inline.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent, inline)));
                    break;
                }
                if (nextLine is null || nextLine.CompareTo(line) <= 0) break;
                line = nextLine;
            }
        }

        static IEnumerable<Inline> Actions(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                if (!inline.IsEnabled) continue;
                if (inline is Hyperlink { Focusable: true } or SpoilerSpan { Hidden: true, Focusable: true }) yield return inline;
                else if (inline is Span and not SpoilerSpan { Hidden: true })
                    foreach (var nested in Actions(((Span)inline).Inlines)) yield return nested;
            }
        }
    }

    private static bool CanInvokeKeyboardButton(ButtonBase button) => button.IsVisible && button.IsEnabled && button switch
    {
        { Command: RoutedCommand command } => command.CanExecute(button.CommandParameter, button.CommandTarget ?? button),
        { Command: { } command } => command.CanExecute(button.CommandParameter),
        _ => true
    };

    private static void InvokeKeyboardButton(ButtonBase button)
    {
        if (!CanInvokeKeyboardButton(button)) return;
        // 同一输入回合执行，避免 UIA Invoke 的异步派发落在已经换绑的控件上。
        if (button is ToggleButton toggle) toggle.SetCurrentValue(ToggleButton.IsCheckedProperty, toggle.IsChecked != true);
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        if (button.Command is RoutedCommand routed) routed.Execute(button.CommandParameter, button.CommandTarget ?? button);
        else button.Command?.Execute(button.CommandParameter);
    }
}

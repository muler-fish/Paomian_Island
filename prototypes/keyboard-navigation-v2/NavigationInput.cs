using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Paomian.KeyboardPrototype;

internal sealed class NavigationInput
{
    private const string Alphabet = "ASDFGHJKLWER";
    private readonly Window _window;
    private readonly ReadingRegion[] _regions;
    private readonly ForumTree? _forums;
    private HintSession? _hints;
    private bool _openPending;

    private sealed class HintSession(ReadingRegion region, HintLabel[] labels, double offset)
    {
        internal ReadingRegion Region { get; } = region;
        internal HintLabel[] Labels { get; } = labels;
        internal double Offset { get; } = offset;
        internal string Prefix { get; set; } = "";
    }

    internal IReadOnlyList<HintLabel> Hints => _hints?.Labels ?? [];

    internal NavigationInput(Window window, ReadingRegion[] regions, ForumTree? forums = null)
    {
        _window = window;
        _regions = regions;
        _forums = forums;
        window.PreviewKeyDown += (_, e) =>
        {
            if (HandleKey(e.Key, e.KeyboardDevice.Modifiers, e.IsRepeat)) e.Handled = true;
        };
        window.PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
                FocusedRegion()?.Scroll.Release(e.Key == Key.Up ? -1 : 1);
        };
        window.GotKeyboardFocus += (_, _) => CancelHints();
        window.LostKeyboardFocus += (_, e) =>
        {
            foreach (var region in regions)
                if (e.NewFocus is not Visual next || !next.IsDescendantOf(region.Root)) region.Scroll.Stop();
        };
        window.PreviewMouseDown += (_, _) => Interrupt();
        window.PreviewMouseWheel += (_, _) => Interrupt();
        window.Deactivated += (_, _) => Interrupt();
        window.Closed += (_, _) => Interrupt();
        foreach (var region in regions)
        {
            region.Root.SizeChanged += (_, _) =>
            {
                if (_hints?.Region == region) CancelHints();
            };
            region.View.ScrollChanged += (_, e) =>
            {
                if (_hints?.Region == region && (e.VerticalChange != 0 || e.ExtentHeightChange != 0 || e.ViewportHeightChange != 0)) CancelHints();
            };
        }
    }

    internal void ShowModal(Window child)
    {
        if (_openPending) return;
        Interrupt();
        _window.UpdateLayout();
        var previousFocus = Keyboard.FocusedElement as UIElement;
        var offsets = _regions.Select(region => region.View.VerticalOffset).ToArray();
        var forumOffset = _forums?.VerticalOffset ?? 0;
        _openPending = true;
        child.Owner = _window;
        child.ShowInTaskbar = false;
        // 等按键或点击的路由结束，再进入拥有者自己的模态循环。
        _window.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!_window.IsVisible || !_window.IsActive) return;
                var content = (UIElement)_window.Content;
                var layer = AdornerLayer.GetAdornerLayer(content);
                var scrim = new OwnerScrim(content) { IsHitTestVisible = false };
                layer?.Add(scrim);
                try { child.ShowDialog(); }
                finally { layer?.Remove(scrim); }

                if (!_window.IsVisible) return;
                // ShowDialog 恢复直接父窗。只有父窗活动时才恢复真实键盘焦点。
                if (_window.IsActive && previousFocus is { IsVisible: true, IsEnabled: true, Focusable: true } && Window.GetWindow(previousFocus) == _window)
                    previousFocus.Focus();
                for (var i = 0; i < _regions.Length; i++) _regions[i].View.ScrollToVerticalOffset(offsets[i]);
                _forums?.RestoreOffset(forumOffset);
                _window.UpdateLayout();
            }
            finally { _openPending = false; }
        });
    }

    internal bool HandleKey(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        if (key is Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return false;
        var region = FocusedRegion();
        var inTree = _forums?.IsKeyboardFocusWithin == true;
        var editing = Keyboard.FocusedElement is TextBoxBase;
        if (key == Key.Tab && (modifiers is ModifierKeys.None or ModifierKeys.Shift))
        {
            CancelHints(restore: true);
            StopScroll();
            if (_forums is not null && !editing && (region is not null || inTree))
            {
                if (!repeat) CycleRegion(region, modifiers == ModifierKeys.Shift ? -1 : 1);
                return true;
            }
            foreach (var tabRegion in _regions) tabRegion.PrepareTabStops();
            return false;
        }

        if (modifiers != ModifierKeys.None || editing) return false;
        if (key == Key.Escape)
        {
            if (_hints is not null)
            {
                if (!repeat) CancelHints(restore: true);
                return true;
            }
            if (_forums is null && region is not null)
            {
                if (!repeat) _window.Close();
                return true;
            }
            return false;
        }

        if (inTree) return _forums!.HandleKey(key, repeat);
        if (region is null || _openPending) return false;
        if (_hints is { } hints)
        {
            if (key is >= Key.A and <= Key.Z or Key.Back)
            {
                if (!repeat) TypeHint(hints, key);
                return true;
            }
            if (key == Key.Enter) return true;
            if (key is Key.Up or Key.Down or Key.PageUp or Key.PageDown) CancelHints(restore: true);
        }
        else if (key == Key.F)
        {
            if (!repeat) ShowHints(region);
            return true;
        }

        switch (key)
        {
            case Key.Up or Key.Down:
                if (!repeat)
                {
                    region.View.Focus();
                    region.Scroll.Press(key == Key.Up ? -1 : 1);
                }
                return true;
            case Key.PageUp or Key.PageDown:
                if (!repeat)
                {
                    region.View.Focus();
                    region.Scroll.Page(key == Key.PageUp ? -1 : 1);
                }
                return true;
            case Key.Left or Key.Right:
                return true;
            default:
                return false;
        }
    }

    private ReadingRegion? FocusedRegion() => _regions.FirstOrDefault(region => region.Root.IsKeyboardFocusWithin);

    private void CycleRegion(ReadingRegion? region, int step)
    {
        // 树为第 0 栏，侧栏与两个物理滚动区域各自保留位置。
        var count = _regions.Length + 1;
        var index = Array.IndexOf(_regions, region) + 1;
        var next = (index + step + count) % count;
        if (next == 0) _forums!.FocusSelection();
        else _regions[next - 1].View.Focus();
    }

    private void ShowHints(ReadingRegion region)
    {
        region.Scroll.Stop();
        region.Root.UpdateLayout();
        var targets = region.VisibleTargets();
        if (targets.Length == 0)
        {
            region.Feedback("本屏没有可操作项");
            return;
        }

        var width = 1;
        for (long capacity = Alphabet.Length; capacity < targets.Length; capacity *= Alphabet.Length) width++;
        var codes = new string[targets.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            var number = i;
            var digits = new char[width];
            for (var digit = width - 1; digit >= 0; digit--)
            {
                digits[digit] = Alphabet[number % Alphabet.Length];
                number /= Alphabet.Length;
            }
            codes[i] = new string(digits);
        }
        var labels = region.PlaceHints(targets, codes);
        if (labels.Length == 0) return;
        _hints = new HintSession(region, labels, region.View.VerticalOffset);
        region.DrawHints(labels, "");
    }

    private void TypeHint(HintSession hints, Key key)
    {
        if (key == Key.Back)
        {
            if (hints.Prefix.Length > 0) hints.Prefix = hints.Prefix[..^1];
        }
        else
        {
            var prefix = hints.Prefix + (char)('A' + (key - Key.A));
            if (hints.Labels.FirstOrDefault(label => label.Text == prefix) is { } match)
            {
                var visible = hints.Region.VisibleTargets().Any(item => item.Target == match.Item.Target);
                CancelHints(restore: true);
                if (visible) match.Item.Target.Execute();
                return;
            }
            if (hints.Labels.Any(label => label.Text.StartsWith(prefix, StringComparison.Ordinal))) hints.Prefix = prefix;
        }
        hints.Region.DrawHints(hints.Labels, hints.Prefix);
    }

    private void CancelHints(bool restore = false)
    {
        if (_hints is not { } hints) return;
        _hints = null;
        hints.Region.ClearOverlay();
        if (restore && Math.Abs(hints.Region.View.VerticalOffset - hints.Offset) > .01)
            hints.Region.View.ScrollToVerticalOffset(hints.Offset);
    }

    private void StopScroll()
    {
        foreach (var region in _regions) region.Scroll.Stop();
    }

    private void Interrupt()
    {
        StopScroll();
        CancelHints();
    }

    private sealed class OwnerScrim(UIElement owner) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext drawing) => drawing.DrawRectangle((Brush)FindResource("Scrim"), null, new Rect(RenderSize));
    }
}

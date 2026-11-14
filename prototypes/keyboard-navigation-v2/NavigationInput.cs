using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Paomian.KeyboardPrototype;

internal enum Scheme { Letters, Enter }

internal sealed class NavigationInput
{
    private const string Alphabet = "ASDFGHJKLWER";
    private readonly Window _window;
    private readonly ReadingRegion[] _regions;
    private readonly bool _dialog;
    private ReadingRegion? _lastRegion;
    private Choice? _choice, _choiceToRestore;
    private bool _openPending;

    private sealed class Choice(ReadingRegion region, VisibleTarget[] targets, double originalOffset, string[] labels)
    {
        internal ReadingRegion Region { get; } = region;
        internal VisibleTarget[] Targets { get; } = targets;
        internal double OriginalOffset { get; } = originalOffset;
        internal string[] Labels { get; } = labels;
        internal string Prefix { get; set; } = "";
        internal int Index { get; set; }
        internal VisibleTarget Current => Targets[Index];
    }

    internal Scheme Scheme { get; private set; }
    internal string[] HintLabels => _choice?.Labels ?? [];
    internal VisibleTarget[] Candidates => _choice?.Targets ?? [];
    internal ReadingTarget? SelectedTarget => _choice is { Labels.Length: 0 } choice ? choice.Current.Target : null;

    internal NavigationInput(Window window, ReadingRegion[] regions, Scheme scheme, bool dialog = false)
    {
        _window = window;
        _regions = regions;
        _dialog = dialog;
        Scheme = scheme;
        _lastRegion = regions.FirstOrDefault();
        window.PreviewKeyDown += KeyDown;
        window.PreviewKeyUp += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down)
                ReadingFocus()?.Scroll.Release(e.Key == Key.Up ? -1 : 1);
        };
        window.GotKeyboardFocus += (_, _) =>
        {
            _lastRegion = _regions.FirstOrDefault(region => region.Root.IsKeyboardFocusWithin) ?? _lastRegion;
            if (_choice?.Region != ReadingFocus()) Leave(restorePosition: false);
        };
        window.LostKeyboardFocus += (_, e) =>
        {
            foreach (var region in regions)
                if (e.OldFocus == region.View) region.Scroll.Stop();
        };
        window.PreviewMouseDown += (_, _) => Interrupt();
        window.PreviewMouseWheel += (_, _) => Interrupt();
        window.Deactivated += (_, _) => Interrupt();
        window.Closed += (_, _) => Interrupt();
        foreach (var region in regions)
        {
            region.Root.SizeChanged += (_, _) => InvalidateChoice(region);
            region.View.ScrollChanged += (_, _) => InvalidateChoice(region);
        }
    }

    internal void SetScheme(Scheme value)
    {
        Leave(restorePosition: true);
        StopScroll();
        Scheme = value;
    }

    internal void ShowModal(Window child)
    {
        if (_openPending) return;
        StopScroll();
        var previousFocus = Keyboard.FocusedElement as UIElement;
        var offsets = _regions.Select(region => region.View.VerticalOffset).ToArray();
        var selection = _choiceToRestore;
        Leave(restorePosition: false);
        _openPending = true;
        child.Owner = _window;
        child.ShowInTaskbar = false;
        _window.Dispatcher.InvokeAsync(() =>
        {
            if (!_window.IsVisible || !_window.IsActive)
            {
                _openPending = false;
                return;
            }
            var content = (UIElement)_window.Content;
            var layer = AdornerLayer.GetAdornerLayer(content);
            var scrim = new OwnerScrim(content) { IsHitTestVisible = false };
            layer?.Add(scrim);
            try
            {
                child.ShowDialog();
            }
            finally
            {
                layer?.Remove(scrim);
                _openPending = false;
            }
            if (_window.IsVisible)
                _window.Dispatcher.InvokeAsync(() => Restore(previousFocus, offsets, selection), DispatcherPriority.Input);
        });
    }

    private void Restore(UIElement? focus, double[] offsets, Choice? selection)
    {
        if (!_window.IsVisible) return;
        if (focus is not { IsVisible: true, IsEnabled: true, Focusable: true } || Window.GetWindow(focus) != _window)
            focus = _lastRegion?.View;
        if (focus is not null)
        {
            FocusManager.SetFocusedElement(FocusManager.GetFocusScope(focus), focus);
            if (_window.IsActive) focus.Focus();
        }

        for (var i = 0; i < _regions.Length; i++) _regions[i].View.ScrollToVerticalOffset(offsets[i]);
        _window.UpdateLayout();
        if (selection is null || !_window.IsActive || Keyboard.FocusedElement != selection.Region.View) return;

        var targets = selection.Region.VisibleTargets();
        var index = Array.FindIndex(targets, target => target.Target == selection.Current.Target);
        if (index < 0) return;
        _choice = new Choice(selection.Region, targets, selection.OriginalOffset, []) { Index = index };
        PaintChoice();
    }

    private void KeyDown(object sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key, e.KeyboardDevice.Modifiers, e.IsRepeat)) e.Handled = true;
    }

    internal bool HandleKey(Key key, ModifierKeys modifiers, bool repeat = false)
    {
        if (key is Key.System or Key.ImeProcessed or Key.DeadCharProcessed) return false;
        if (key == Key.Tab && (modifiers is ModifierKeys.None or ModifierKeys.Shift))
        {
            Leave(restorePosition: true);
            StopScroll();
            foreach (var tabRegion in _regions) tabRegion.PrepareTabStops();
            return false;
        }

        if (key == Key.F6 && (modifiers is ModifierKeys.None or ModifierKeys.Shift) && _regions.Length > 0)
        {
            if (!repeat) CycleRegion(modifiers == ModifierKeys.Shift ? -1 : 1);
            return true;
        }

        if (modifiers != ModifierKeys.None || Keyboard.FocusedElement is TextBoxBase) return false;
        if (key == Key.Escape && (_choice is not null || _dialog))
        {
            if (repeat) return true;
            if (_choice is not null) Leave(restorePosition: true);
            else _window.Close();
            return true;
        }

        var region = ReadingFocus();
        if (region is null || _openPending) return false;
        if (_choice is { } choice)
        {
            if (choice.Labels.Length > 0 && (key is >= Key.A and <= Key.Z or Key.Back))
            {
                if (!repeat) TypeHint(choice, key);
                return true;
            }

            if (choice.Labels.Length == 0 && (key is Key.Up or Key.Down or Key.Enter))
            {
                if (key == Key.Enter)
                {
                    if (!repeat) Execute(choice);
                }
                else
                {
                    choice.Index = Math.Clamp(choice.Index + (key == Key.Up ? -1 : 1), 0, choice.Targets.Length - 1);
                    PaintChoice();
                }
                return true;
            }

            if (key is Key.Up or Key.Down or Key.PageUp or Key.PageDown) Leave(restorePosition: true);
        }
        else if ((Scheme == Scheme.Letters && key == Key.F) || (Scheme == Scheme.Enter && key == Key.Enter))
        {
            if (!repeat) Choose(region);
            return true;
        }

        switch (key)
        {
            case Key.Up or Key.Down:
                if (!repeat) region.Scroll.Press(key == Key.Up ? -1 : 1);
                return true;
            case Key.PageUp or Key.PageDown:
                if (!repeat) region.Scroll.Page(key == Key.PageUp ? -1 : 1);
                return true;
            default:
                return false;
        }
    }

    private ReadingRegion? ReadingFocus() => _regions.FirstOrDefault(region => region.View.IsKeyboardFocused);

    private void CycleRegion(int step)
    {
        Leave(restorePosition: true);
        StopScroll();
        var index = Array.IndexOf(_regions, _lastRegion);
        _regions[(index + step + _regions.Length) % _regions.Length].View.Focus();
    }

    private void Choose(ReadingRegion region)
    {
        region.Scroll.Stop();
        region.View.UpdateLayout();
        var targets = region.VisibleTargets();
        if (targets.Length == 0)
        {
            region.Feedback("本屏没有可操作项");
            return;
        }

        string[] labels = [];
        if (Scheme == Scheme.Letters)
        {
            var width = 1;
            for (long capacity = Alphabet.Length; capacity < targets.Length; capacity *= Alphabet.Length) width++;
            labels = new string[targets.Length];
            for (var i = 0; i < labels.Length; i++)
            {
                var number = i;
                var digits = new char[width];
                for (var digit = width - 1; digit >= 0; digit--)
                {
                    digits[digit] = Alphabet[number % Alphabet.Length];
                    number /= Alphabet.Length;
                }
                labels[i] = new string(digits);
            }
        }

        _choice = new Choice(region, targets, region.View.VerticalOffset, labels);
        PaintChoice();
    }

    private void TypeHint(Choice choice, Key key)
    {
        if (key == Key.Back)
        {
            if (choice.Prefix.Length > 0) choice.Prefix = choice.Prefix[..^1];
        }
        else
        {
            var prefix = choice.Prefix + (char)('A' + (key - Key.A));
            var exact = Array.IndexOf(choice.Labels, prefix);
            if (exact >= 0)
            {
                choice.Index = exact;
                Execute(choice);
                return;
            }
            if (choice.Labels.Any(label => label.StartsWith(prefix, StringComparison.Ordinal))) choice.Prefix = prefix;
        }
        PaintChoice();
    }

    private void Execute(Choice choice)
    {
        Leave(restorePosition: false);
        _choiceToRestore = choice.Labels.Length == 0 ? choice : null;
        try { choice.Current.Target.Execute(); }
        finally { _choiceToRestore = null; }
    }

    private void PaintChoice()
    {
        if (_choice is not { } choice) return;
        if (choice.Labels.Length == 0) choice.Region.ShowSelection(choice.Current, choice.Index, choice.Targets.Length);
        else choice.Region.ShowHints(choice.Targets, choice.Labels, choice.Prefix);
    }

    private void InvalidateChoice(ReadingRegion region)
    {
        if (_choice?.Region == region) Leave(restorePosition: false);
    }

    private void Leave(bool restorePosition)
    {
        if (_choice is not { } choice) return;
        _choice = null;
        choice.Region.ClearOverlay();
        if (restorePosition && Math.Abs(choice.Region.View.VerticalOffset - choice.OriginalOffset) > .01)
            choice.Region.View.ScrollToVerticalOffset(choice.OriginalOffset);
    }

    private void StopScroll()
    {
        foreach (var region in _regions) region.Scroll.Stop();
    }

    private void Interrupt()
    {
        StopScroll();
        Leave(restorePosition: false);
    }

    private sealed class OwnerScrim(UIElement owner) : Adorner(owner)
    {
        protected override void OnRender(DrawingContext drawing) => drawing.DrawRectangle((Brush)FindResource("Scrim"), null, new Rect(RenderSize));
    }
}

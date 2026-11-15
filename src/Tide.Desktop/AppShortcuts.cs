using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Tide.Core;

namespace Tide.Desktop;

internal enum ShortcutScope { Main, Image }
internal enum ShortcutId { Jump, Back, Forward, RefreshList, RefreshThread, Subscription, PreviousImage, NextImage, ZoomIn, ZoomOut, FitImage, ActualImage, RotateImage }

internal readonly record struct ShortcutKey(Key Key, ModifierKeys Modifiers = ModifierKeys.None)
{
    internal static Key KeyOf(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;
    internal string Serialized => $"{Modifiers}:{Key}";
    internal static bool TryRead(string value, out ShortcutKey shortcut)
    {
        shortcut = default;
        var separator = value.IndexOf(':');
        if (separator < 0 || !Enum.TryParse(value[..separator], out ModifierKeys modifiers) ||
            !Enum.TryParse(value[(separator + 1)..], out Key key)) return false;
        shortcut = new(key, modifiers);
        return true;
    }
    public override string ToString()
    {
        var name = Key switch
        {
            Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓",
            Key.Add => "小键盘 +", Key.Subtract => "小键盘 -",
            Key.OemPlus => Modifiers.HasFlag(ModifierKeys.Shift) ? "+" : "=", Key.OemMinus => "-",
            >= Key.D0 and <= Key.D9 => ((int)Key - (int)Key.D0).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "小键盘 " + ((int)Key - (int)Key.NumPad0),
            _ => Key.ToString()
        };
        return (Modifiers.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") +
            (Modifiers.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") +
            (Modifiers.HasFlag(ModifierKeys.Shift) && Key != Key.OemPlus ? "Shift+" : "") + name;
    }
}

internal sealed record ShortcutDefinition(ShortcutId Id, string Label, ShortcutScope Scope, params ShortcutKey[] Defaults);

internal sealed class AppShortcuts
{
    internal static IReadOnlyList<ShortcutDefinition> Commands { get; } =
    [
        new(ShortcutId.Jump, "聚焦串号或链接输入框", ShortcutScope.Main, new ShortcutKey(Key.L, ModifierKeys.Control)),
        new(ShortcutId.Back, "阅读历史后退", ShortcutScope.Main, new ShortcutKey(Key.Left, ModifierKeys.Alt)),
        new(ShortcutId.Forward, "阅读历史前进", ShortcutScope.Main, new ShortcutKey(Key.Right, ModifierKeys.Alt)),
        // 保留既有的配置键，原来的刷新键现在跟随活动栏。
        new(ShortcutId.RefreshList, "刷新当前内容栏", ShortcutScope.Main, new ShortcutKey(Key.F5)),
        new(ShortcutId.RefreshThread, "刷新正文", ShortcutScope.Main, new ShortcutKey(Key.R, ModifierKeys.Control)),
        new(ShortcutId.Subscription, "订阅或取消订阅当前串", ShortcutScope.Main, new ShortcutKey(Key.D, ModifierKeys.Control)),
        new(ShortcutId.PreviousImage, "上一张", ShortcutScope.Image, new ShortcutKey(Key.Left)),
        new(ShortcutId.NextImage, "下一张", ShortcutScope.Image, new ShortcutKey(Key.Right)),
        new(ShortcutId.ZoomIn, "放大", ShortcutScope.Image, new ShortcutKey(Key.OemPlus, ModifierKeys.Shift), new ShortcutKey(Key.OemPlus), new ShortcutKey(Key.Add)),
        new(ShortcutId.ZoomOut, "缩小", ShortcutScope.Image, new ShortcutKey(Key.OemMinus), new ShortcutKey(Key.Subtract)),
        new(ShortcutId.FitImage, "适应窗口", ShortcutScope.Image, new ShortcutKey(Key.D0), new ShortcutKey(Key.NumPad0)),
        new(ShortcutId.ActualImage, "原始大小", ShortcutScope.Image, new ShortcutKey(Key.D1), new ShortcutKey(Key.NumPad1)),
        new(ShortcutId.RotateImage, "向左旋转", ShortcutScope.Image, new ShortcutKey(Key.R))
    ];
    internal static AppShortcuts Defaults { get; } = new(new UserState());
    private readonly UserState _state;
    private readonly Dictionary<ShortcutId, ShortcutKey?> _overrides = new();
    internal event Action? Changed;

    internal AppShortcuts(UserState state)
    {
        _state = state;
        foreach (var command in Commands)
        {
            if (!state.ShortcutBindings.TryGetValue(command.Id.ToString(), out var value) || value is null) continue;
            if (value.Length == 0) _overrides[command.Id] = null;
            else if (ShortcutKey.TryRead(value, out var key) && Validate(command.Scope, key) is null)
                _overrides[command.Id] = key;
        }
        // 修正手工编辑或旧配置带来的冲突；默认键表本身各作用域内唯一。
        ShortcutDefinition? conflict;
        while ((conflict = Commands.LastOrDefault(command => _overrides.ContainsKey(command.Id) &&
            Conflicts(command, Get(command)) is not null)) is not null) _overrides.Remove(conflict.Id);
        Store();
    }

    internal IReadOnlyList<ShortcutKey> Get(ShortcutId id) => Get(Definition(id));
    private IReadOnlyList<ShortcutKey> Get(ShortcutDefinition command) =>
        _overrides.TryGetValue(command.Id, out var key) ? key is { } value ? [value] : [] : command.Defaults;
    private static ShortcutDefinition Definition(ShortcutId id) => Commands.First(c => c.Id == id);
    internal bool IsCustomized(ShortcutId id) => _overrides.ContainsKey(id);
    internal string Label(ShortcutId id) => string.Join(" / ", Get(id));
    internal string Hint(string label, ShortcutId id) => Label(id) is { Length: > 0 } key ? $"{label} · {key}" : label;

    internal string? TrySet(ShortcutId id, ShortcutKey? key)
    {
        var command = Definition(id);
        if (key is { } value)
        {
            var error = Validate(command.Scope, value) ?? Conflicts(command, [value]);
            if (error is not null) return error;
        }
        if (key.HasValue && command.Defaults.SequenceEqual([key.Value])) _overrides.Remove(id);
        else _overrides[id] = key;
        Save();
        return null;
    }

    internal string? Restore(ShortcutId id)
    {
        var command = Definition(id);
        if (Conflicts(command, command.Defaults) is { } error) return error;
        _overrides.Remove(id);
        Save();
        return null;
    }

    internal void RestoreAll()
    {
        _overrides.Clear();
        Save();
    }

    private string? Conflicts(ShortcutDefinition command, IReadOnlyList<ShortcutKey> keys)
    {
        foreach (var other in Commands.Where(c => c.Scope == command.Scope && c.Id != command.Id))
            foreach (var key in Get(other))
                if (keys.Contains(key)) return $"{key} 已用于「{other.Label}」。";
        return null;
    }

    private void Store() => _state.ShortcutBindings = _overrides.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value?.Serialized ?? "");
    private void Save() { Store(); Changed?.Invoke(); }

    internal static bool ModifierOnly(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string? Validate(ShortcutScope scope, ShortcutKey gesture)
    {
        var (key, mods) = gesture;
        if (ModifierOnly(key) || key is Key.None or Key.System or Key.ImeProcessed or Key.DeadCharProcessed)
            return "请再按一个非修饰键。";
        if (!Enum.IsDefined(key) || (mods & ~(ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift)) != 0 ||
            key is Key.Tab or Key.Escape or Key.Enter or Key.Apps or Key.PrintScreen or Key.Pause or Key.CapsLock or Key.NumLock or Key.Scroll ||
            mods.HasFlag(ModifierKeys.Alt) && key is Key.F4 or Key.Space ||
            (key == Key.Delete && mods.HasFlag(ModifierKeys.Alt) && mods.HasFlag(ModifierKeys.Control)) ||
            (key == Key.F10 && mods is ModifierKeys.None or ModifierKeys.Shift))
            return "这个按键用于系统或标准窗口操作。";
        if (mods == ModifierKeys.Control && key is Key.A or Key.C or Key.V or Key.X or Key.Z or Key.Y or Key.B or Key.I or Key.U or Key.Insert ||
            mods == ModifierKeys.Shift && key is Key.Insert or Key.Delete)
            return "这个组合键用于文字编辑。";
        if (scope == ShortcutScope.Main && key is not (>= Key.F1 and <= Key.F24) &&
            (mods & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
            return "主窗口快捷键需要 Ctrl、Alt 或功能键。";
        return null;
    }

    internal void Bind(Window window, ShortcutId id, ICommand command)
    {
        window.InputBindings.Add(new InputBinding(command, new Gesture(this, id)));
    }

    internal static void RouteTextShortcut(Window window, KeyEventArgs e)
    {
        // RichTextBox 会先处理对齐等类级手势，即使正文只读。只有通过同一作用域和焦点检查的绑定优先。
        if (e.OriginalSource is not TextBoxBase) return;
        foreach (InputBinding binding in window.InputBindings)
        {
            if (binding.Gesture is not Gesture gesture || !gesture.Matches(window, e)) continue;
            e.Handled = true;
            if (binding.Command.CanExecute(binding.CommandParameter)) binding.Command.Execute(binding.CommandParameter);
            return;
        }
    }

    internal static bool Allows(ShortcutId id, ShortcutKey key, IInputElement? focus)
    {
        if (id == ShortcutId.Jump && key == new ShortcutKey(Key.L, ModifierKeys.Control)) return true;
        for (var element = focus as DependencyObject; element is not null; element = LogicalTreeHelper.GetParent(element))
            if (element is TextBoxBase { IsReadOnly: false } or PasswordBox or ComboBox { IsEditable: true }) return false;
        return true;
    }

    private sealed class Gesture(AppShortcuts shortcuts, ShortcutId id) : InputGesture
    {
        public override bool Matches(object targetElement, InputEventArgs inputEventArgs)
        {
            if (inputEventArgs is not KeyEventArgs e || e.IsRepeat && id is ShortcutId.Back or ShortcutId.Forward) return false;
            var key = new ShortcutKey(ShortcutKey.KeyOf(e), e.KeyboardDevice.Modifiers);
            if (!shortcuts.Get(id).Contains(key)) return false;
            var focus = e.OriginalSource as IInputElement ?? Keyboard.FocusedElement;
            if (focus is DependencyObject element && Window.GetWindow(element) is { } window && !ReferenceEquals(window, targetElement)) return false;
            return Allows(id, key, focus);
        }
    }
}

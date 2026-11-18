using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tide.Desktop;

internal static class SettingsShortcuts
{
    internal static void Add(StackPanel page, Window owner, AppShortcuts shortcuts, Func<string, Action, Button> button)
    {
        var refresh = new List<Action>();
        var bossFeedback = ErrorText();
        foreach (var group in AppShortcuts.Commands.GroupBy(command => command.Scope).OrderBy(group => group.Key != ShortcutScope.Global))
        {
            var global = group.Key == ShortcutScope.Global;
            SettingsLayout.Section(page, global ? "老板键" : group.Key == ShortcutScope.Main ? "主窗口" : "看图窗口");
            if (global)
            {
                var toggle = new CheckBox { IsChecked = shortcuts.BossKeyEnabled, MinWidth = 24 };
                void Toggle(object sender, RoutedEventArgs e)
                {
                    if (toggle.IsChecked == shortcuts.BossKeyEnabled) return;
                    var error = shortcuts.SetBossKeyEnabled(toggle.IsChecked == true);
                    toggle.IsChecked = shortcuts.BossKeyEnabled;
                    SetError(bossFeedback, error);
                }
                toggle.Checked += Toggle;
                toggle.Unchecked += Toggle;
                SettingsLayout.Row(page, "开启老板键", "在其他应用中也可按组合键隐藏或恢复泡面岛。", toggle);
                refresh.Add(() => toggle.IsChecked = shortcuts.BossKeyEnabled);
            }
            foreach (var command in group)
            {
                var current = SettingsLayout.Text("", true);
                current.Margin = new(0, 0, 10, 0);
                current.VerticalAlignment = VerticalAlignment.Center;
                var feedback = global ? bossFeedback : ErrorText();
                var edit = button("修改", () => new ShortcutRecorder(command, shortcuts) { Owner = owner }.ShowDialog());
                var clear = button("清除", () => shortcuts.TrySet(command.Id, null));
                var reset = button("恢复默认", () => SetError(feedback, shortcuts.Restore(command.Id)));
                var actions = new WrapPanel { MaxWidth = 296, HorizontalAlignment = HorizontalAlignment.Right };
                actions.Children.Add(current);
                foreach (var action in global ? new[] { edit, reset } : [edit, clear, reset])
                {
                    action.Margin = new(4, 0, 0, 0);
                    AutomationProperties.SetName(action, command.Label + "：" + action.Content);
                    actions.Children.Add(action);
                }
                SettingsLayout.Row(page, command.Label, null, actions);
                page.Children.Add(feedback);
                refresh.Add(() =>
                {
                    var label = shortcuts.Label(command.Id);
                    current.Text = label.Length == 0 ? "已清除" : label;
                    clear.IsEnabled = label.Length > 0;
                    reset.IsEnabled = shortcuts.IsCustomized(command.Id);
                    SetError(feedback, global ? shortcuts.BossKeyStartupError : null);
                });
            }
        }
        var restoreFeedback = ErrorText();
        var restoreAll = button("全部恢复默认", () => SetError(restoreFeedback, shortcuts.RestoreAll()));
        restoreAll.HorizontalAlignment = HorizontalAlignment.Left;
        SettingsLayout.Block(page, restoreAll, separator: false);
        page.Children.Add(restoreFeedback);
        void Refresh() { foreach (var row in refresh) row(); }
        Refresh();
        shortcuts.Changed += Refresh;
        owner.Closed += (_, _) => shortcuts.Changed -= Refresh;
    }

    private static TextBlock ErrorText()
    {
        var feedback = SettingsLayout.Text("", true);
        feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        feedback.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
        return feedback;
    }

    private static void SetError(TextBlock feedback, string? error)
    {
        feedback.Text = error ?? "";
        feedback.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class ShortcutRecorder : DialogWindow
    {
        private readonly ShortcutDefinition _command;
        private readonly AppShortcuts _shortcuts;
        private readonly TextBox? _manual;

        internal ShortcutRecorder(ShortcutDefinition command, AppShortcuts shortcuts) : base("修改快捷键", command.Label)
        {
            _command = command; _shortcuts = shortcuts;
            Width = 460; Height = 270;
            Body.Children.Add(Hint("按下新的组合键，按 Esc 取消。"));
            AutomationProperties.SetLiveSetting(Feedback, AutomationLiveSetting.Polite);
            Body.Children.Add(Feedback);
            AddAction("取消", Close);
            if (command.Scope == ShortcutScope.Global)
            {
                // 已被别的程序注册的按键可能不会送到录入窗口，允许直接输入组合键来核对。
                _manual = new TextBox { Text = shortcuts.Label(command.Id), MinWidth = 180 };
                AutomationProperties.SetName(_manual, "手动输入老板键");
                var entry = new StackPanel();
                entry.Children.Add(_manual);
                entry.Children.Add(Button("使用组合键", ApplyManual));
                Body.Children.Add(new Expander { Header = "手动输入组合键", Content = entry });
            }
        }

        private void ApplyManual()
        {
            try
            {
                var gesture = new KeyGestureConverter().ConvertFromInvariantString(_manual!.Text.Trim()) as KeyGesture;
                if (gesture is not null) { Record(new(gesture.Key, gesture.Modifiers)); return; }
            }
            catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException) { }
            Feedback.Text = $"请输入组合键，例如 {AppShortcuts.Defaults.Label(ShortcutId.BossKey)}。";
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            var key = ShortcutKey.KeyOf(e);
            var modifiers = e.KeyboardDevice.Modifiers;
            if (key == Key.Escape || key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift ||
                _manual?.Parent is StackPanel { IsKeyboardFocusWithin: true })
            {
                base.OnPreviewKeyDown(e);
                return;
            }
            e.Handled = true;
            if (e.IsRepeat || AppShortcuts.ModifierOnly(key)) return;
            Record(new(key, modifiers));
        }

        private void Record(ShortcutKey key)
        {
            var error = _shortcuts.TrySet(_command.Id, key);
            if (error is null) Close();
            else Feedback.Text = error;
        }
    }
}

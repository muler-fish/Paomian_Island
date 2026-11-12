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
        foreach (var group in AppShortcuts.Commands.GroupBy(command => command.Scope))
        {
            SettingsLayout.Section(page, group.Key == ShortcutScope.Main ? "主窗口" : "看图窗口");
            foreach (var command in group)
            {
                var current = SettingsLayout.Text("", true);
                current.Margin = new(0, 0, 10, 0);
                current.VerticalAlignment = VerticalAlignment.Center;
                var feedback = SettingsLayout.Text("", true);
                feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
                feedback.Visibility = Visibility.Collapsed;
                AutomationProperties.SetLiveSetting(feedback, AutomationLiveSetting.Polite);
                var edit = button("修改", () => new ShortcutRecorder(command, shortcuts) { Owner = owner }.ShowDialog());
                var clear = button("清除", () => shortcuts.TrySet(command.Id, null));
                var reset = button("恢复默认", () =>
                {
                    feedback.Text = shortcuts.Restore(command.Id) ?? "";
                    feedback.Visibility = feedback.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                });
                var actions = new WrapPanel { MaxWidth = 296, HorizontalAlignment = HorizontalAlignment.Right };
                actions.Children.Add(current);
                foreach (var action in new[] { edit, clear, reset })
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
                    feedback.Visibility = Visibility.Collapsed;
                });
            }
        }
        var restoreAll = button("全部恢复默认", shortcuts.RestoreAll);
        restoreAll.HorizontalAlignment = HorizontalAlignment.Left;
        SettingsLayout.Block(page, restoreAll, separator: false);
        void Refresh() { foreach (var row in refresh) row(); }
        Refresh();
        shortcuts.Changed += Refresh;
        owner.Closed += (_, _) => shortcuts.Changed -= Refresh;
    }

    private sealed class ShortcutRecorder : DialogWindow
    {
        private readonly ShortcutDefinition _command;
        private readonly AppShortcuts _shortcuts;

        internal ShortcutRecorder(ShortcutDefinition command, AppShortcuts shortcuts) : base("修改快捷键", command.Label)
        {
            _command = command; _shortcuts = shortcuts;
            Width = 460; Height = 270;
            Body.Children.Add(Hint("按下新的组合键，按 Esc 取消。"));
            AutomationProperties.SetLiveSetting(Feedback, AutomationLiveSetting.Polite);
            Body.Children.Add(Feedback);
            AddAction("取消", Close);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            var key = ShortcutKey.KeyOf(e);
            var modifiers = e.KeyboardDevice.Modifiers;
            if (key == Key.Escape || key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
            {
                base.OnPreviewKeyDown(e);
                return;
            }
            e.Handled = true;
            if (e.IsRepeat || AppShortcuts.ModifierOnly(key)) return;
            var error = _shortcuts.TrySet(_command.Id, new(key, modifiers));
            if (error is null) Close();
            else Feedback.Text = error;
        }
    }
}

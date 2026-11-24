using System.Windows.Controls;
using Paomian.Core;

namespace Paomian.Desktop;

internal static class SettingsMouseWheel
{
    internal static void Add(StackPanel page, MainViewModel vm, Func<Action, Button> resetButton)
    {
        SettingsLayout.Section(page, "鼠标滚轮");
        var preset = new ComboBox { Width = 140, ItemsSource = new[] { "推荐预设", "短时平滑", "精细短滑", "均衡短滑", "快速短滑", "自定义" } };
        SettingsLayout.Row(page, "滚动预设", null, preset);
        var details = new StackPanel { Margin = new(10, 4, 0, 0) };
        page.Children.Add(new Expander { Header = "调整滚轮参数", Content = details, IsExpanded = false,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch, Margin = new(-10, 12, 0, 0) });
        var curve = new ComboBox { Width = 140, ItemsSource = new[] { "线性", "二次缓出", "三次缓出", "五次缓入缓出" } };
        var syncing = false;
        var current = vm.ReadingWheel;
        var distance = SettingsLayout.Slider(details, "每格距离", null, current.Distance, 8, 160, 1, " DIP",
            value => Edit(vm.ReadingWheel with { Distance = value }));
        var duration = SettingsLayout.Slider(details, "过渡时长", null, current.DurationMs, 16, 220, 1, " ms",
            value => Edit(vm.ReadingWheel with { DurationMs = value }));
        var immediate = SettingsLayout.Slider(details, "立即位移比例", null, current.ImmediateFraction * 100, 0, 100, 1, "%",
            value => Edit(vm.ReadingWheel with { ImmediateFraction = value / 100 }));
        SettingsLayout.Row(details, "缓动曲线", null, curve);
        var reset = resetButton(() => Select(WheelPreset.Recommended));
        reset.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        SettingsLayout.Block(details, reset, false);

        preset.SelectionChanged += (_, _) => { if (!syncing && preset.SelectedIndex >= 0) Select((WheelPreset)preset.SelectedIndex); };
        curve.SelectionChanged += (_, _) => { if (curve.SelectedIndex >= 0) Edit(vm.ReadingWheel with { Curve = (WheelCurve)curve.SelectedIndex }); };
        Display();

        void Edit(WheelTuning value)
        {
            if (syncing) return;
            vm.SetMouseWheel(new(WheelPreset.Custom, value));
            syncing = true;
            preset.SelectedIndex = (int)WheelPreset.Custom;
            syncing = false;
        }
        void Select(WheelPreset choice)
        {
            vm.SetMouseWheel(vm.State.MouseWheel.Select(choice));
            Display();
        }
        void Display()
        {
            syncing = true;
            var value = vm.ReadingWheel;
            preset.SelectedIndex = (int)vm.State.MouseWheel.Preset;
            distance.Value = value.Distance;
            duration.Value = value.DurationMs;
            immediate.Value = Math.Round(value.ImmediateFraction * 100);
            curve.SelectedIndex = (int)value.Curve;
            syncing = false;
        }
    }
}

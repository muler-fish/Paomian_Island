using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

using Paomian.Core;

namespace Paomian.Desktop.Tests;

internal static partial class KeyboardReadingChecks
{
    private static readonly (WheelPreset Name, WheelTuning Value)[] FormalPresets =
    [
        (WheelPreset.Recommended, new(100, 192, .01)), (WheelPreset.Short, new(72, 96, .2)),
        (WheelPreset.Fine, new(48, 72, .5)), (WheelPreset.Balanced, new(56, 72, .5)),
        (WheelPreset.Fast, new(64, 48, .7))
    ];

    internal static void CheckWheelBursts()
    {
        foreach (var (_, tuning) in FormalPresets)
        foreach (var direction in new[] { -1, 1 })
        {
            var motion = new ShortWheelMotion { Tuning = tuning };
            var offset = 2000d;
            var notch = direction * tuning.Distance;
            for (var tick = 1; tick <= 6; tick++)
            {
                offset += motion.Push(notch, offset, 4000, false);
                offset += motion.Advance(12);
                Require(Near(offset + motion.Remaining, 2000 + tick * notch, .00001), "连续输入重复或丢失行程");
            }
            var turn = offset;
            var first = motion.Push(-notch, turn, 4000, false);
            Require(first * direction < 0 && motion.Remaining * direction <= 0, "反向后仍沿旧目标移动");
            offset += first + motion.Advance(tuning.DurationMs);
            Require(Near(offset, turn - notch, .00001) && motion.Remaining == 0 && motion.Advance(500) == 0,
                "反向未在新目标停稳");
        }
        Console.WriteLine("PASS five presets, six-notch bursts in both directions, immediate reversal and exact stop");
    }

    private static async Task CheckWheelPanel(MainWindow window, string output, Action<Window, string> capture)
    {
        var model = window.Model;
        await model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        var reader = Find<ListBox>(window, "ReaderList");
        var middle = Find<ListBox>(window, "ThreadList");
        var view = Descendants<ScrollViewer>(reader).First();
        var row = await PositionPost(reader, view, KeyboardReadingFixture.LongReply, -1000);
        double Top() => row.TranslatePoint(new Point(), view).Y;
        var start = Top();
        foreach (var key in new[] { Key.F6, Key.F7 })
        {
            var input = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, key)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            window.RaiseEvent(input);
            Require(!input.Handled && window.OwnedWindows.Count == 0, "普通启动占用了验收快捷键");
        }
        var settings = new SettingsWindow(model) { Owner = window };
        settings.Show();
        await Idle();
        T Field<T>(string label) where T : FrameworkElement => Descendants<T>(settings).Single(
            field => AutomationProperties.GetName(field) == label);
        var presetBox = Field<ComboBox>("滚动预设");
        var notch = Field<Slider>("每格距离");
        var duration = Field<Slider>("过渡时长");
        var immediate = Field<Slider>("立即位移比例");
        var curveBox = Field<ComboBox>("缓动曲线");
        var reset = Descendants<Button>(settings).Single(button => Equals(button.Content, "恢复推荐预设"));
        Require(presetBox.SelectedIndex == 0 && notch.Value == 100 && duration.Value == 192 && immediate.Value == 1 &&
            curveBox.SelectedItem as string == "二次缓出" && notch.ActualWidth > 100, "默认参数或控件尺寸错误");
        Require(presetBox.Items.Cast<string>().SequenceEqual(["推荐预设","短时平滑","精细短滑","均衡短滑","快速短滑","自定义"]), "预设名称或顺序错误");
        Require(!Descendants<CheckBox>(settings).Any(box => box.Content?.ToString()?.Contains("Windows 行数") == true), "旧换算选项仍然可见");
        foreach (var (id, value) in FormalPresets)
        {
            presetBox.SelectedIndex = (int)id;
            Require(notch.Value == value.Distance && duration.Value == value.DurationMs && immediate.Value == value.ImmediateFraction * 100 &&
                curveBox.SelectedIndex == (int)value.Curve && model.Store.Load().MouseWheel.Preset == id, "预设界面或保存错误");
            foreach (var surface in new FrameworkElement[] { reader, middle, Find<ScrollViewer>(window, "ForumScroll"), Field<ScrollViewer>("设置内容") })
                Require(InertialScroll.GetTuning(surface) == value, "阅读视口绑定错误");
        }
        curveBox.SelectedIndex = (int)WheelCurve.Cubic;
        Require(presetBox.SelectedItem as string == "自定义", "修改曲线后未切换自定义");
        presetBox.SelectedItem = "推荐预设";
        notch.Value = 60;
        Require(presetBox.SelectedItem as string == "自定义", "修改距离后未切换自定义");
        duration.Value = 52; immediate.Value = 65; curveBox.SelectedIndex = (int)WheelCurve.Cubic;
        var custom = new WheelTuning(60, 52, .65, WheelCurve.Cubic);
        Require(Near(start, Top()) && model.Store.Load().MouseWheel.Effective == custom, "调节时位置变化或参数未保存");
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(model.ReadingWheel == WheelTuning.Recommended && presetBox.SelectedIndex == 0, "恢复推荐失败");
        presetBox.SelectedItem = "自定义";
        Require(model.ReadingWheel == custom && notch.Value == 60, "重新选择自定义丢失上次调节");
        using var restarted = new MainViewModel(UiRefinementChecks.CreateApi(), new LocalStore(model.Store.Root));
        Require(restarted.ReadingWheel == custom && restarted.State.MouseWheel.Preset == WheelPreset.Custom,
            "从同一资料目录重新启动后丢失自定义");
        if (SystemParameters.WheelScrollLines > 0)
        {
            RaiseWheel(view, -120);
            await Until(() => !InertialScroll.IsRunning(reader), "自定义滚轮没有停止");
            await Idle();
            Require(Near(start - Top(), 60, 1.1), "右栏未使用设置中的行程");
            var middleView = Descendants<ScrollViewer>(middle).First();
            var middleRow = Descendants<ListBoxItem>(middle).First(item => item.IsVisible);
            var y = middleRow.TranslatePoint(new Point(), middleView).Y;
            RaiseWheel(middleView, -120);
            await Until(() => !InertialScroll.IsRunning(middle), "中栏滚轮没有停止");
            await Idle();
            Require(Near(y - middleRow.TranslatePoint(new Point(), middleView).Y, 60, 1.1), "中栏未使用设置中的行程");
            RaiseWheel(view, -120);
            presetBox.SelectedItem = "推荐预设";
            await Idle();
            var stopped = Top();
            await Task.Delay(230);
            Require(!InertialScroll.IsRunning(reader) && Near(stopped, Top()), "修改参数后仍在播放旧动画");
        }
        reset.BringIntoView();
        foreach (var theme in new[] { "dark", "light" })
        {
            App.ApplyTheme(theme); await Idle();
            capture(settings, Path.Combine(output, $"wheel-settings-{theme}.png"));
        }
        foreach (var preset in FormalPresets.Select(item => item.Value))
        foreach (var curve in Enum.GetValues<WheelCurve>())
        {
            var motion = new ShortWheelMotion { Tuning = preset with { Curve = curve } };
            var pos = motion.Push(preset.Distance, 0, 5000, false);
            Require(Near(pos, preset.Distance * preset.ImmediateFraction, .00001), "第一段位移错误");
            for (var ms = 1; ms <= preset.DurationMs + 10; ms++)
            {
                var step = motion.Advance(ms);
                Require(step >= 0 && pos + step <= preset.Distance + .00001, "曲线倒退或过冲");
                pos += step;
            }
            Require(Near(pos, preset.Distance, .00001) && motion.Remaining == 0, "未到目标即停止");
        }
        Require(WheelTuning.Short.NotchDistance(1, 300) == 72 && WheelTuning.Balanced.NotchDistance(6, 300) == 56 &&
            WheelTuning.Fast.NotchDistance(-1, 300) == 300 && WheelTuning.Fast.NotchDistance(0, 300) == 0,
            "Windows 行数、整页或零行设置错误");
        if (SystemParameters.WheelScrollLines > 0)
        {
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                UiCopyChecks.SetDpi(window, scale, 1120, 680);
                await Idle();
                row = await PositionPost(reader, view, KeyboardReadingFixture.LongReply, -1000);
                for (var i = 0; i < FormalPresets.Length; i++)
                {
                    presetBox.SelectedIndex = i;
                    var before = Top();
                    for (var tick = 0; tick < 8; tick++) RaiseWheel(view, -15);
                    await Idle();
                    var expected = FormalPresets[i].Value.Distance;
                    Require(!InertialScroll.IsRunning(reader) && Near(before - Top(), expected, 1.1),
                        $"精细输入总行程错误 DPI={scale} preset={i} expected={expected} actual={before - Top()}");
                    for (var tick = 0; tick < 8; tick++) RaiseWheel(view, 15);
                    await Idle();
                    Require(Near(before, Top(), 1.1), "精细滚轮反向未回到原位置");
                }
            }
        }
        settings.Close();
        Console.WriteLine("PASS live presets, custom sliders and curve, position, reopen, system rules and monotone endpoints");
    }

    private static async Task MeasurePanelDesktop(MainWindow window, string output)
    {
        await window.Model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        var reader = Find<ListBox>(window, "ReaderList");
        var view = Descendants<ScrollViewer>(reader).First();
        var handle = new WindowInteropHelper(window).Handle;
        using var cursor = new FocusDesktopInput();
        await FocusDesktopInput.ActivateAsync(handle);
        Find<FrameworkElement>(window, "ReaderPane").Focus();
        var results = new List<object>();
        for (var trial = 0; trial < 3; trial++)
        for (var index = 0; index < FormalPresets.Length; index++)
        {
            var i = (index + trial) % FormalPresets.Length;
            window.Model.SetMouseWheel(new(FormalPresets[i].Name));
            var row = await PositionPost(reader, view, KeyboardReadingFixture.LongReply, -1200);
            FocusDesktopInput.Hover(view, handle, new Point(view.ActualWidth * .36, view.ActualHeight * .32));
            await Idle();
            var top = row.TranslatePoint(new Point(), view).Y;
            var watch = Stopwatch.StartNew();
            var frames = new List<WheelFrame>();
            var received = new List<int>();
            void Wheel(object s, MouseWheelEventArgs e) => received.Add(e.Delta);
            void Sample(object? s, EventArgs e) => frames.Add(new(watch.Elapsed.TotalMilliseconds,
                view.VerticalOffset, top - row.TranslatePoint(new Point(), view).Y,
                InertialScroll.RemainingDistance(reader), InertialScroll.IsRunning(reader)));
            reader.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel), true);
            CompositionTarget.Rendering += Sample;
            try
            {
                watch.Restart();
                FocusDesktopInput.Wheel(handle, -120);
                await Task.Delay(260);
            }
            finally
            {
                CompositionTarget.Rendering -= Sample;
                reader.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Wheel));
            }
            var preset = FormalPresets[i];
            var finalTravel = top - row.TranslatePoint(new Point(), view).Y;
            results.Add(new { trial, preset = preset.Name, received, finalTravel, frames });
            File.WriteAllText(Path.Combine(output, "windows-input.json"), JsonSerializer.Serialize(results));
            Console.WriteLine($"{preset.Name} #{trial} deltas=[{string.Join(',', received)}] frames={frames.Count} travel={finalTravel:F2} running={InertialScroll.IsRunning(reader)}");
            Require(received.SequenceEqual([-120]) && frames.Count > 0 &&
                Near(finalTravel, preset.Value.NotchDistance(SystemParameters.WheelScrollLines, view.ViewportHeight), 1.1) &&
                !InertialScroll.IsRunning(reader), $"Windows 滚轮未到达 {preset.Name} 的目标");
        }
        window.Model.SetMouseWheel(WheelSettings.Default);
        await DragDuringWheel(reader, view, handle);
    }
}

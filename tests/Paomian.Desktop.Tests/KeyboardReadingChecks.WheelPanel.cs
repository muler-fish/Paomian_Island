using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Paomian.Desktop.Tests;

internal static partial class KeyboardReadingChecks
{
    private static async Task CheckWheelPanel(MainWindow window, string output, Action<Window, string> capture)
    {
        WheelTuningWindow Panel() => window.OwnedWindows.OfType<WheelTuningWindow>().Single();
        T Field<T>(string label) where T : FrameworkElement => Descendants<T>(Panel()).Single(
            field => AutomationProperties.GetName(field) == label);
        await window.Model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        var reader = Find<ListBox>(window, "ReaderList");
        var view = Descendants<ScrollViewer>(reader).First();
        Find<FrameworkElement>(window, "ReaderPane").Focus();
        var row = await PositionPost(reader, view, KeyboardReadingFixture.LongReply, -1000);
        double Top() => row.TranslatePoint(new Point(), view).Y;
        var start = Top();
        var notch = Field<Slider>("每格距离");
        Require(notch.ActualWidth > 100 && notch.Value == 72, "基准预设或滑轨尺寸错误");
        Field<ComboBox>("预设").SelectedIndex = 2;
        Require(notch.Value == 56 && Field<Slider>("过渡时长").Value == 72 && Field<Slider>("立即位移").Value == 50,
            "切换均衡预设后数值没有同步");
        notch.Value = 60;
        Field<Slider>("过渡时长").Value = 52;
        Field<Slider>("立即位移").Value = 65;
        Field<ComboBox>("曲线").SelectedIndex = (int)WheelCurve.Cubic;
        Require(Field<ComboBox>("预设").SelectedIndex == WheelTuningWindow.Presets.Length && Near(start, Top()), "自定义调节改变了视口或预设状态错误");
        capture(Panel(), Path.Combine(output, "wheel-tuning.png"));
        if (SystemParameters.WheelScrollLines > 0)
        {
            RaiseWheel(view, -120);
            await Until(() => !InertialScroll.IsRunning(reader), "自定义滚轮没有停止");
            await Idle();
            Require(Near(start - Top(), 60, 1.1), "调节面板更改没有实际应用到正文");
        }
        var settled = Top();
        Press(Key.F7);
        Require(!window.OwnedWindows.OfType<WheelTuningWindow>().Any(), "F7 没有关闭调校");
        Press(Key.F7); await Idle();
        Require(Field<Slider>("每格距离").Value == 60 && Near(settled, Top()), "重新打开面板丢失输入或改变位置");
        Press(Key.F6);
        Require(Field<ComboBox>("预设").SelectedIndex == 0 && Near(settled, Top()), "未能在同一阅读位置回到 B");

        foreach (var preset in WheelTuningWindow.Presets.Select(item => item.Value))
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
        Require(WheelTuning.Baseline.NotchDistance(1, 300) == 24 && WheelTuning.Balanced.NotchDistance(6, 300) == 56 &&
            WheelTuning.Fast.NotchDistance(-1, 300) == 300 && WheelTuning.Fast.NotchDistance(0, 300) == 0,
            "Windows 行数、整页或零行设置错误");
        if (SystemParameters.WheelScrollLines > 0)
        {
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                UiCopyChecks.SetDpi(window, scale, 1120, 680);
                await Idle();
                row = await PositionPost(reader, view, KeyboardReadingFixture.LongReply, -1000);
                for (var i = 1; i < WheelTuningWindow.Presets.Length; i++)
                {
                    Field<ComboBox>("预设").SelectedIndex = i;
                    var before = Top();
                    for (var tick = 0; tick < 8; tick++) RaiseWheel(view, -15);
                    await Idle();
                    var expected = WheelTuningWindow.Presets[i].Value.Distance;
                    Require(!InertialScroll.IsRunning(reader) && Near(before - Top(), expected, 1.1),
                        $"精细输入总行程错误 DPI={scale} preset={i} expected={expected} actual={before - Top()}");
                    for (var tick = 0; tick < 8; tick++) RaiseWheel(view, 15);
                    await Idle();
                    Require(Near(before, Top(), 1.1), "精细滚轮反向未回到原位置");
                }
            }
        }
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
        for (var index = 0; index < WheelTuningWindow.Presets.Length; index++)
        {
            var i = (index + trial) % WheelTuningWindow.Presets.Length;
            InertialScroll.SetTuning(reader, WheelTuningWindow.Presets[i].Value);
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
            var preset = WheelTuningWindow.Presets[i];
            var finalTravel = top - row.TranslatePoint(new Point(), view).Y;
            results.Add(new { trial, preset = preset.Name, received, finalTravel, frames });
            File.WriteAllText(Path.Combine(output, "windows-input.json"), JsonSerializer.Serialize(results));
            Console.WriteLine($"{preset.Name} #{trial} deltas=[{string.Join(',', received)}] frames={frames.Count} travel={finalTravel:F2} running={InertialScroll.IsRunning(reader)}");
            Require(received.SequenceEqual([-120]) && frames.Count > 0 &&
                Near(finalTravel, preset.Value.NotchDistance(SystemParameters.WheelScrollLines, view.ViewportHeight), 1.1) &&
                !InertialScroll.IsRunning(reader), $"Windows 滚轮未到达 {preset.Name} 的目标");
        }
        InertialScroll.SetTuning(reader, WheelTuning.BrowserComparison);
        await DragDuringWheel(reader, view, handle);
    }
}

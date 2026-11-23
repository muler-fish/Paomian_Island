using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Paomian.Desktop.Tests;

internal static partial class KeyboardReadingChecks
{
    private sealed record WheelFrame(double Ms, double Offset, double Travel, double Remaining, bool Animating);
    private sealed record WheelInput(double Ms, int Delta, double Travel);

    private static async Task CompareWheel(MainWindow window, string output, bool desktop)
    {
        var reader = Find<ListBox>(window, "ReaderList");
        var view = Descendants<ScrollViewer>(reader).First();
        await window.Model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        Find<FrameworkElement>(window, "ReaderPane").Focus();
        var post = reader.Items.OfType<ReadPost>().Single(p => p.Post.Id == KeyboardReadingFixture.LongReply);
        var traces = new List<object>();
        var summaries = new List<object>();
        var handle = new WindowInteropHelper(window).Handle;
        using var inputDevice = desktop ? new FocusDesktopInput() : null;
        if (desktop) await FocusDesktopInput.ActivateAsync(handle);
        // 对照入口初始为 B；通过和人工验收相同的 F6 切换。
        foreach (var mode in new[] { "B", "C", "A" })
        {
            if (mode != "B") { Press(Key.F6); await Idle(); }
            foreach (var (name, inputs) in new (string, int[])[]
            {
                ("single", [-120]), ("burst", [-120,-120,-120,-120,-120,-120]),
                ("reverse", [-120,-120,-120,120,120]), ("fine", [-15,-15,-15,-15,-15,-15,-15,-15])
            })
            {
                for (var trial = 0; trial < 3; trial++)
                {
                    await PositionPost(reader, view, post.Post.Id, -1200);
                    if (desktop) FocusDesktopInput.Hover(view, handle);
                    var row = (ListBoxItem)reader.ItemContainerGenerator.ContainerFromItem(post);
                    double Top() => row.TranslatePoint(new Point(), view).Y;
                    var initial = Top();
                    var frames = new List<WheelFrame>();
                    var events = new List<WheelInput>();
                    TimeSpan? lastFrame = null;
                    var timer = Stopwatch.StartNew();
                    var delivered = new List<int>();
                    void Received(object sender, MouseWheelEventArgs e) => delivered.Add(e.Delta);
                    reader.AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Received), true);
                    void Frame(object? sender, EventArgs e)
                    {
                        var renderingTime = ((RenderingEventArgs)e).RenderingTime;
                        if (renderingTime == lastFrame) return;
                        lastFrame = renderingTime;
                        frames.Add(new(timer.Elapsed.TotalMilliseconds, view.VerticalOffset, initial - Top(),
                            InertialScroll.RemainingDistance(reader), InertialScroll.IsRunning(reader)));
                    }
                    CompositionTarget.Rendering += Frame;
                    try
                    {
                        foreach (var delta in inputs)
                        {
                            if (events.Count > 0) await Task.Delay(name == "fine" ? 8 : 28);
                            events.Add(new(timer.Elapsed.TotalMilliseconds, delta, initial - Top()));
                            if (desktop) FocusDesktopInput.Wheel(handle, delta);
                            else RaiseWheel(view, delta);
                        }
                        await Task.Delay(650);
                    }
                    finally
                    {
                        CompositionTarget.Rendering -= Frame;
                        reader.RemoveHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(Received));
                    }
                    Require(delivered.SequenceEqual(inputs), "收到的滚轮输入与回放不一致");
                    var moves = frames.Where((f, i) => Math.Abs(f.Travel - (i == 0 ? 0 : frames[i - 1].Travel)) > .1).ToArray();
                    Require(moves.Length > 0 && !InertialScroll.IsRunning(reader), $"{mode} {name} 没有稳定到达终点");
                    var gaps = frames.Zip(frames.Skip(1), (a,b) => b.Ms-a.Ms).Order().ToArray();
                    var turn = events.FirstOrDefault(input => input.Delta > 0);
                    var reverseMs = turn == null ? (double?)null : moves.First(f => f.Ms >= turn.Ms && f.Travel < turn.Travel - .1).Ms - turn.Ms;
                    var result = new { mode, name, trial, first = moves[0].Ms - events[0].Ms,
                        tail = Math.Max(0, moves[^1].Ms - events[^1].Ms), travel = initial - Top(),
                        tailDistance = initial - Top() - events[^1].Travel, reverseMs, movingFrames = moves.Length,
                        frameMedian = gaps[gaps.Length / 2], frameP95 = gaps[(int)((gaps.Length-1)*.95)] };
                    summaries.Add(result);
                    traces.Add(new { result, events, frames });
                    Console.WriteLine(JsonSerializer.Serialize(result));
                    if (mode == "B" && name != "reverse" && SystemParameters.WheelScrollLines > 0 &&
                        (SystemParameters.ClientAreaAnimation || name == "fine"))
                    {
                        var expected = -inputs.Sum() / 120d * 24 * SystemParameters.WheelScrollLines;
                        Require(Near(result.travel, expected, 1.1), $"B 的实际行程 {result.travel:F1} 与目标 {expected:F1} 不符");
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(output, "metrics.json"), JsonSerializer.Serialize(new {
            desktop, window.ActualWidth, window.ActualHeight, dpi = VisualTreeHelper.GetDpi(view).PixelsPerInchY,
            lines = SystemParameters.WheelScrollLines, animations = SystemParameters.ClientAreaAnimation, summaries, traces }));
        Press(Key.F6); // A → B
        if (desktop) await DragDuringWheel(reader, view, handle);
        // 离线串共三页，先读到最后一页，避免把正常追加的新页误认为边缘回弹。
        await window.Model.LoadMoreThreadAsync();
        await window.Model.LoadMoreThreadAsync();
        await CheckWheelHandoffs(reader, view, post.Post.Id);
    }

    private static async Task CheckWheelHandoffs(ListBox reader, ScrollViewer view, long id)
    {
        var row = await PositionPost(reader, view, id, -1200);
        double Top() => row.TranslatePoint(new Point(), view).Y;
        var initial = Top();
        RaiseWheel(view, -120);
        RaiseWheel(view, -15);
        RaiseWheel(view, -105);
        await Task.Delay(220);
        var lines = SystemParameters.WheelScrollLines;
        var notch = lines < 0 ? view.ViewportHeight : Math.Min(view.ViewportHeight, lines * 24d);
        Require(Near(initial - Top(), notch * 2, 1.1), "同向整格与精细滚轮混合时丢失或重复了待走行程");
        RaiseWheel(view, 120);
        view.ScrollToVerticalOffset(320);
        await Idle();
        var positioned = view.VerticalOffset;
        await Task.Delay(180);
        Require(!InertialScroll.IsRunning(reader) && Near(positioned, view.VerticalOffset), "程序定位后动画仍在改变视口");
        foreach (var delta in new[] { 120, -120 })
        {
            if (delta > 0) view.ScrollToTop(); else view.ScrollToBottom();
            await Idle();
            RaiseWheel(view, delta);
            await Task.Delay(180);
            Require(!InertialScroll.IsRunning(reader) && Near(view.VerticalOffset, delta > 0 ? 0 : view.ScrollableHeight),
                $"在滚动边界继续输入后没有停稳 delta={delta} offset={view.VerticalOffset:F1} max={view.ScrollableHeight:F1}");
        }
        Console.WriteLine("PASS mixed wheel packets, programmatic jump and both boundaries");
    }

    private static async Task DragDuringWheel(ListBox reader, ScrollViewer view, nint handle)
    {
        var thumb = Descendants<ScrollBar>(view).First(b => b.IsVisible && b.Orientation == Orientation.Vertical)
            .Track.Thumb;
        var before = view.VerticalOffset;
        FocusDesktopInput.Hover(view, handle);
        FocusDesktopInput.Wheel(handle, -120);
        await Task.Delay(20);
        var center = new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2);
        await FocusDesktopInput.Drag(thumb, center, new Point(center.X, center.Y + 46), handle);
        var release = view.VerticalOffset;
        await Task.Delay(200);
        Require(!InertialScroll.IsRunning(reader) && release - before > 100 && Near(release, view.VerticalOffset, 1),
            $"拖动滚动条没有接管或松手后视口继续移动 {before:F1}/{release:F1}/{view.VerticalOffset:F1}");
        Console.WriteLine($"PASS scrollbar drag: {before:F1} -> {release:F1}, settled {view.VerticalOffset:F1}");
    }

    private static async Task CheckWheelDpi(MainWindow window)
    {
        await window.Model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        var reader = Find<ListBox>(window, "ReaderList");
        var view = Descendants<ScrollViewer>(reader).First();
        Find<FrameworkElement>(window, "ReaderPane").Focus();
        foreach (var theme in new[] { "light", "dark" })
        foreach (var scale in new[] { 1d, 1.25, 1.5 })
        {
            App.ApplyTheme(theme);
            UiCopyChecks.SetDpi(window, scale, 1120, 680);
            await Idle();
            var row = await PositionPost(reader, view, KeyboardReadingFixture.LongReply, -1000);
            double Top() => row.TranslatePoint(new Point(), view).Y;
            var initial = Top();
            foreach (var delta in new[] { -120, 120 })
            {
                RaiseWheel(view, delta);
                await Task.Delay(200);
                if (InertialScroll.IsRunning(reader))
                    Console.WriteLine($"等待窗口呈现 {theme}/{scale} delta={delta} at200={initial - Top():F1} remaining={InertialScroll.RemainingDistance(reader):F1}");
                await Until(() => !InertialScroll.IsRunning(reader), "DPI 变化后滚轮动画未停止");
                await Idle();
                var expected = delta < 0 ? 24d * SystemParameters.WheelScrollLines : 0;
                if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines > 0)
                    Require(Near(initial - Top(), expected, 1.1), $"{theme}/{scale} delta={delta} start={initial:F2} end={Top():F2} expected={expected} dpi={VisualTreeHelper.GetDpi(reader).DpiScaleY}");
            }
            var actual = VisualTreeHelper.GetDpi(reader).DpiScaleY;
            Require(Near(actual, scale, .01), "目标 DPI 未生效");
            Console.WriteLine($"PASS {theme} DPI={actual*96:F0} 双向滚轮");
        }
    }

    private static void RaiseWheel(UIElement target, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled) target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta)
            { RoutedEvent = Mouse.MouseWheelEvent });
    }
}

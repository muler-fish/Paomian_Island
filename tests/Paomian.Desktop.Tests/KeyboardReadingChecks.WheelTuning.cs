using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Paomian.Core;

namespace Paomian.Desktop.Tests;

internal static partial class KeyboardReadingChecks
{
    private sealed record ProfileScene(string Name, FrameworkElement Surface, ScrollViewer Viewer,
        Func<Task<FrameworkElement>> Position, ReadingScrollPanel? Panel = null);

    private sealed record LayoutPass(double Ms, bool Measure, double Duration);
    private sealed record ProfileRun(string Layer, string Preset, string Sequence, int Trial,
        double Distance, double Duration, double Immediate, double Viewport, double Width, double Extent,
        int Realized, List<WheelInput> Inputs, List<WheelFrame> Frames, List<LayoutPass> Layout,
        double UiCpuMs, double ObservedMs, List<double> DispatcherMs);

    private sealed class ProfileViewer : ScrollViewer
    {
        internal Action<bool, TimeSpan>? LayoutSample;
        protected override Size MeasureOverride(Size size)
        {
            var start = Stopwatch.GetTimestamp();
            var result = base.MeasureOverride(size);
            LayoutSample?.Invoke(true, Stopwatch.GetElapsedTime(start));
            return result;
        }
        protected override Size ArrangeOverride(Size size)
        {
            var start = Stopwatch.GetTimestamp();
            var result = base.ArrangeOverride(size);
            LayoutSample?.Invoke(false, Stopwatch.GetElapsedTime(start));
            return result;
        }
    }

    private static async Task ProfileWheel(MainWindow window, string output)
    {
        await window.Model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        var reader = Find<ListBox>(window, "ReaderList");
        var readerView = Descendants<ScrollViewer>(reader).First();
        var longRow = await PositionPost(reader, readerView, KeyboardReadingFixture.LongReply, -1200);
        var body = Descendants<PostBody>(longRow).Single();
        var width = body.ActualWidth;
        var font = body.FontSize;
        var line = body.Document.LineHeight;
        var html = body.Html;
        var text = new TextRange(body.Document.ContentStart, body.Document.ContentEnd).Text;
        var runs = new List<ProfileRun>();
        var refresh = DisplayHz(window);
        var environment = new { window.ActualWidth, window.ActualHeight, readerView.ViewportWidth,
            readerView.ViewportHeight, bodyWidth = width, font, line, refresh, dpi = VisualTreeHelper.GetDpi(window).PixelsPerInchY,
            lines = SystemParameters.WheelScrollLines, animation = SystemParameters.ClientAreaAnimation,
            renderTier = RenderCapability.Tier >> 16 };
        Console.WriteLine(JsonSerializer.Serialize(environment));
        File.WriteAllText(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(environment));
        Require(SystemParameters.WheelScrollLines > 0 && SystemParameters.ClientAreaAnimation, "性能对照需要有界行数和系统动画。");

        var parent = (Grid)reader.Parent;
        foreach (var layer in new[] { "plain", "rich" })
        {
            FrameworkElement content = layer == "plain" ? new TextBlock { Text = text, FontFamily = body.FontFamily,
                FontWeight = body.FontWeight, FontSize = font, LineHeight = line, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                TextWrapping = TextWrapping.Wrap } : new PostBody { Html = html, FontSize = font };
            content.Width = width;
            var section = new ProfileViewer { Content = content, HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            InertialScroll.SetIsEnabled(section, true);
            Grid.SetRow(section, Grid.GetRow(reader));
            reader.Visibility = Visibility.Hidden;
            parent.Children.Add(section);
            try
            {
                await Idle();
                await MeasureScene(new(layer, section, section, async () =>
                {
                    section.ScrollToVerticalOffset(1200);
                    await Idle();
                    return content;
                }), runs);
            }
            finally
            {
                parent.Children.Remove(section);
                reader.Visibility = Visibility.Visible;
            }
        }
        var panel = Descendants<ReadingScrollPanel>(reader).Single();
        await MeasureScene(new("long", reader, readerView,
            async () => await PositionPost(reader, readerView, KeyboardReadingFixture.LongReply, -1200), panel), runs);
        await PositionPost(reader, readerView, KeyboardReadingFixture.LongReply, -1200);
        var captureCosts = new List<double>();
        var saveCosts = new List<double>();
        for (var i = 0; i < 25; i++)
        {
            var start = Stopwatch.GetTimestamp();
            var anchor = ReadingAnchor.Capture(reader, skipTips: true)!;
            captureCosts.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            start = Stopwatch.GetTimestamp();
            window.Model.SaveReadingPosition((ReadPost)anchor.Item, Math.Abs(anchor.Top) + i % 2 * 5);
            saveCosts.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(new { captureCosts, saveCosts }));
        var middle = Find<ListBox>(window, "ThreadList");
        var middleView = Descendants<ScrollViewer>(middle).First();
        await MeasureScene(new("list", middle, middleView, async () =>
        {
            var item = middle.Items[8];
            new ReadingAnchor(item, 0).Restore(middle);
            await Idle();
            return (FrameworkElement)middle.ItemContainerGenerator.ContainerFromItem(item);
        }, Descendants<ReadingScrollPanel>(middle).Single()), runs);

        var publicFiles = Environment.GetEnvironmentVariable("PAOMIAN_WHEEL_PUBLIC_PAGES");
        if (!string.IsNullOrWhiteSpace(publicFiles))
        {
            var anchorId = LoadPublicSnapshot(window.Model, publicFiles);
            await Idle();
            await MeasureScene(new("public", reader, readerView,
                async () => await PositionPost(reader, readerView, anchorId, 0), panel), runs);
        }
        File.WriteAllText(Path.Combine(output, "traces.json"), JsonSerializer.Serialize(new { environment, runs }));
        Console.WriteLine($"PASS {runs.Count} scroll traces");
    }

    // 显式给出以前离线保存的公开页。移除作者字段，只从旧测试缓存读缩略图。
    private static long LoadPublicSnapshot(MainViewModel model, string pages)
    {
        model.ReadingPosts.Clear();
        long anchorId = 0;
        int images = 0, cached = 0;
        foreach (var path in pages.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var page = JsonSerializer.Deserialize<ThreadPage>(File.ReadAllText(path))!;
            var posts = page.Page == 1 ? page.Replies.Prepend(page.Original) : page.Replies;
            foreach (var post in posts)
            {
                var clean = post with { UserHash = "", Name = "" };
                if (post.ImageUrl(!post.Extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)) is { } url)
                {
                    images++;
                    var key = "images/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".bin";
                    var old = Path.Combine(Path.GetDirectoryName(path)!, "..", key);
                    if (File.Exists(old)) { model.Store.Cache.Write(key, File.ReadAllBytes(old)); cached++; }
                    else clean = clean with { ImagePath = "keyboard/walk", Extension = ".png" };
                    if (anchorId == 0 && model.ReadingPosts.Count >= 4) anchorId = clean.Id;
                }
                var index = model.ReadingPosts.Count;
                model.ReadingPosts.Add(new(clean, false, index == 0, page.Page, index));
            }
        }
        Console.WriteLine($"Public snapshot: {model.ReadingPosts.Count} rows, {images} images, {cached} cached originals of thumbnails");
        return anchorId == 0 ? model.ReadingPosts[4].Post.Id : anchorId;
    }

    private static async Task MeasureScene(ProfileScene scene, List<ProfileRun> runs)
    {
        (string Name, WheelTuning Settings)[] settings = [("baseline", WheelTuning.Short),
            ("fine", WheelTuning.Fine), ("balanced", WheelTuning.Balanced), ("fast", WheelTuning.Fast)];
        if (scene.Name == "long") settings = [.. settings, ("browser-curve", WheelTuning.Recommended with { Curve = WheelCurve.SmoothStartStop }), ("distance-only", new(48, 96, .2)),
            ("duration-only", new(72, 72, .2)), ("immediate-only", new(72, 96, .5))];
        for (var trial = 0; trial < 5; trial++)
        for (var index = 0; index < settings.Length; index++)
        {
            var (name, tuning) = settings[(index + trial) % settings.Length];
            InertialScroll.SetTuning(scene.Surface, tuning);
            await MeasureSequence(scene, name, tuning, "single", [-120], trial, runs);
            if ((scene.Name is "long" or "public" or "list") && !name.EndsWith("-only", StringComparison.Ordinal) && trial < 3)
            {
                await MeasureSequence(scene, name, tuning, "burst", [-120,-120,-120,-120,-120,-120], trial, runs);
                await MeasureSequence(scene, name, tuning, "reverse", [-120,-120,-120,120,120], trial, runs);
            }
        }
        Console.WriteLine($"PASS {scene.Name} samples={runs.Count(r => r.Layer == scene.Name)}");
    }

    private static async Task MeasureSequence(ProfileScene scene, string name, WheelTuning tuning, string sequence,
        int[] deltas, int trial, List<ProfileRun> runs)
    {
        var marker = await scene.Position();
        // 六个整格最多跨约四分之三屏。取两端视口都可见的行，避免回收时把另一个数据项当成位移。
        if (scene.Panel is { } panel && scene.Name is "list" or "public")
        {
            var y = scene.Viewer.ViewportHeight * .83;
            marker = panel.Children.OfType<ListBoxItem>().First(row =>
            {
                var top = row.TranslatePoint(new Point(), scene.Viewer).Y;
                return top <= y && y < top + row.ActualHeight;
            });
        }
        var item = marker.DataContext;
        double Top() => marker.TranslatePoint(new Point(), scene.Viewer).Y;
        var initial = Top();
        var frames = new List<WheelFrame>(64);
        var inputs = new List<WheelInput>(deltas.Length);
        var passes = new List<LayoutPass>();
        var operations = new List<double>();
        var hooks = scene.Surface.Dispatcher.Hooks;
        var opStarts = new Stack<long>();
        void Start(object? s, DispatcherHookEventArgs e) => opStarts.Push(Stopwatch.GetTimestamp());
        void End(object? s, DispatcherHookEventArgs e)
        {
            if (opStarts.TryPop(out var start) && opStarts.Count == 0)
                operations.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        var watch = Stopwatch.StartNew();
        TimeSpan? render = null;
        void Frame(object? s, EventArgs e)
        {
            var time = ((RenderingEventArgs)e).RenderingTime;
            if (time == render) return;
            render = time;
            frames.Add(new(watch.Elapsed.TotalMilliseconds, scene.Viewer.VerticalOffset, initial - Top(),
                InertialScroll.RemainingDistance(scene.Surface), InertialScroll.IsRunning(scene.Surface)));
        }
        void Layout(bool measure, TimeSpan cost) => passes.Add(new(watch.Elapsed.TotalMilliseconds, measure, cost.TotalMilliseconds));
        if (scene.Panel is not null) scene.Panel.LayoutSample = Layout;
        if (scene.Viewer is ProfileViewer plain) plain.LayoutSample = Layout;
        CompositionTarget.Rendering += Frame;
        hooks.OperationStarted += Start;
        hooks.OperationCompleted += End;
        var cpu = ThreadCpu();
        try
        {
            foreach (var delta in deltas)
            {
                if (inputs.Count > 0) await Task.Delay(28);
                inputs.Add(new(watch.Elapsed.TotalMilliseconds, delta, initial - Top()));
                RaiseWheel(scene.Viewer, delta);
            }
            await Task.Delay(240);
            await Until(() => !InertialScroll.IsRunning(scene.Surface), "动画未停止");
        }
        finally
        {
            cpu = ThreadCpu() - cpu;
            hooks.OperationStarted -= Start;
            hooks.OperationCompleted -= End;
            CompositionTarget.Rendering -= Frame;
            if (scene.Panel is not null) scene.Panel.LayoutSample = null;
            if (scene.Viewer is ProfileViewer physical) physical.LayoutSample = null;
        }
        Require(ReferenceEquals(item, marker.DataContext), $"采样容器已回收 {scene.Name}/{name}/{sequence}");
        Require(frames.Count > 2 && frames.Any(f => Math.Abs(f.Travel) > .1), $"缺少可见移动 {scene.Name}/{name}/{sequence}");
        if (sequence != "reverse")
        {
            var expected = -deltas.Sum() / 120d * tuning.NotchDistance(SystemParameters.WheelScrollLines, scene.Viewer.ViewportHeight);
            Require(Near(frames[^1].Travel, expected, 1.1) && frames.All(f => f.Travel <= expected + 1.1) &&
                !frames.Zip(frames.Skip(1), (a, b) => a.Travel - b.Travel).Any(back => back > 1.1),
                $"单向行程异常 {scene.Name}/{name}/{sequence} expected={expected:F1} actual={frames[^1].Travel:F1}");
        }
        runs.Add(new(scene.Name, name, sequence, trial, tuning.Distance, tuning.DurationMs, tuning.ImmediateFraction,
            scene.Viewer.ViewportHeight, scene.Viewer.ViewportWidth, scene.Viewer.ExtentHeight, scene.Panel?.Children.Count ?? 1,
            inputs, frames, passes, cpu, watch.Elapsed.TotalMilliseconds, operations));
    }

    [DllImport("kernel32.dll")] private static extern nint GetCurrentThread();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetThreadTimes(nint thread,
        out long creation, out long exit, out long kernel, out long user);
    private static double ThreadCpu() => GetThreadTimes(GetCurrentThread(), out _, out _, out var kernel, out var user)
        ? (kernel + user) / 10000d : throw new InvalidOperationException("无法读取 UI 线程 CPU 时间");
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc, int index);
    private static int DisplayHz(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var dc = GetDC(hwnd);
        try { return GetDeviceCaps(dc, 116); } // VREFRESH
        finally { ReleaseDC(hwnd, dc); }
    }
}

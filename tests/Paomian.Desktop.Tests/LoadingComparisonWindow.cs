using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal sealed class LoadingComparisonWindow : Window
{
    private static readonly double[] RingSizes = [22, 36, 56];
    private static readonly string[] RingLabels = ["串列表缩略图 · 22 DIP", "串内图片 · 36 DIP", "独立看图 · 56 DIP"];
    private static readonly double[] LinearWidths = [140, 260];
    private static readonly string[] LinearLabels = ["短轨道 · 140 DIP", "长轨道 · 260 DIP"];

    private readonly string _output;
    private readonly System.Windows.Controls.TextBlock _measurementText;
    private readonly List<ProgressBar> _nativeProgressBars = [];
    private readonly List<RenderSegment> _segments = [];
    private readonly Process _process = Process.GetCurrentProcess();
    private RenderSegment? _currentSegment;
    private TimeSpan? _lastRenderingTime;
    private RecordingSession? _recording;
    private string _theme = "light";

    internal LoadingComparisonWindow(string output)
    {
        _output = output;
        Title = "加载指示器动效对照实验";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(1488, Math.Max(860, workArea.Width - 48));
        Height = Math.Min(820, Math.Max(620, workArea.Height - 60));
        MinWidth = Math.Min(860, Width);
        MinHeight = Math.Min(620, Height);
        SetResourceReference(BackgroundProperty, "Canvas");

        var themeButton = new Button
        {
            Content = "切换深色",
            MinWidth = 112,
            Height = 32,
            Margin = new Thickness(12, 0, 0, 0)
        };
        themeButton.SetResourceReference(StyleProperty, "Secondary");
        themeButton.Click += (_, _) => ToggleTheme(themeButton);
        AutomationProperties.SetName(themeButton, "切换加载动画对照窗口主题");

        _measurementText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        _measurementText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var title = new TextBlock { Text = "加载指示器动效对照", FontSize = 22, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var hint = new TextBlock
        {
            Text = "20 个动画同时运行 · F2 切换浅深色 · Esc 关闭 · 关闭窗口后写入性能记录",
            Margin = new Thickness(0, 5, 0, 0)
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var header = new Grid { Margin = new Thickness(18, 14, 18, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleStack = new StackPanel();
        titleStack.Children.Add(title);
        titleStack.Children.Add(hint);
        titleStack.Children.Add(_measurementText);
        header.Children.Add(titleStack);
        Grid.SetColumn(themeButton, 1);
        header.Children.Add(themeButton);

        var headerBorder = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        headerBorder.SetResourceReference(BackgroundProperty, "Surface");
        headerBorder.SetResourceReference(BorderBrushProperty, "Line");

        var content = new Grid { Margin = new Thickness(12) };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(154) });
        for (var i = 0; i < 4; i++)
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rowHeights = new[] { 56d, 72d, 82d, 98d, 78d, 78d };
        foreach (var height in rowHeights)
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(height) });

        AddCell(content, 0, 0, CreateRowLabel("指示器方案", "圆环和线性动画"));
            AddCell(content, 0, 1, CreateColumnHeader("A · 泡面岛原版", "1.0.10 · 动效基线"));
            AddCell(content, 0, 2, CreateColumnHeader("B · MUI", "v9.3.1 · 正式选用方案"));
        AddCell(content, 0, 3, CreateColumnHeader("C · MaterialDesign", "v5.3.2 · WPF Storyboard 等效移植"));
        AddCell(content, 0, 4, CreateColumnHeader("D · WPF UI / WPF 原生控件", "4.3.0 ProgressRing · WPF 原生 ProgressBar"));

        for (var row = 0; row < RingSizes.Length; row++)
        {
            AddCell(content, row + 1, 0, CreateRowLabel(RingLabels[row], "相同外框尺寸"));
            for (var column = 0; column < 4; column++)
                AddCell(content, row + 1, column + 1, Center(CreateRing(column, RingSizes[row])));
        }

        for (var row = 0; row < LinearWidths.Length; row++)
        {
            AddCell(content, row + 4, 0, CreateRowLabel(LinearLabels[row], "4 DIP 高"));
            for (var column = 0; column < 4; column++)
                AddCell(content, row + 4, column + 1, Center(CreateLinear(column, LinearWidths[row])));
        }

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var root = new DockPanel();
        DockPanel.SetDock(headerBorder, Dock.Top);
        root.Children.Add(headerBorder);
        root.Children.Add(scroll);
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F2)
            {
                ToggleTheme(themeButton);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        Loaded += (_, _) => StartMeasurements();
        Closing += (_, _) => SaveMeasurements();
        ContentRendered += (_, _) => UpdateMeasurementText();
    }

    private static FrameworkElement CreateRing(int scheme, double diameter)
    {
        if (scheme == 0)
        {
            var previous = new PreviousLoadingComparisonIndicator
            {
                Width = diameter,
                Height = diameter,
                IsCircular = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return previous;
        }

        if (scheme == 1)
        {
            var selected = new LoadingIndicator
            {
                Width = diameter,
                Height = diameter,
                Circular = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            selected.SetResourceReference(Control.ForegroundProperty, "Accent");
            return selected;
        }

        if (scheme == 2)
            return new MaterialDesignCircularComparisonIndicator { Width = diameter, Height = diameter };

        var ring = new Wpf.Ui.Controls.ProgressRing
        {
            Width = diameter,
            Height = diameter,
            IsIndeterminate = true,
            CoverRingVisibility = Visibility.Visible,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        ring.SetResourceReference(Control.ForegroundProperty, "Accent");
        ring.SetResourceReference(Wpf.Ui.Controls.ProgressRing.CoverRingStrokeProperty, "Line");
        return ring;
    }

    private FrameworkElement CreateLinear(int scheme, double width)
    {
        if (scheme == 0)
        {
            var previous = new PreviousLoadingComparisonIndicator
            {
                Width = width,
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            return previous;
        }

        if (scheme == 1)
        {
            var selected = new LoadingIndicator { Width = width, Height = 4 };
            selected.SetResourceReference(Control.ForegroundProperty, "Accent");
            return selected;
        }

        if (scheme == 2)
            return new MaterialDesignLinearComparisonIndicator { Width = width, Height = 4 };

        var progress = new ProgressBar
        {
            Width = width,
            Height = 16,
            IsIndeterminate = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new ScaleTransform(1, 0.25),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        progress.SetResourceReference(Control.ForegroundProperty, "Accent");
        progress.SetResourceReference(Control.BackgroundProperty, "Line");
        progress.SetResourceReference(Control.BorderBrushProperty, "Line");
        _nativeProgressBars.Add(progress);
        var host = new Grid
        {
            Width = width,
            Height = 4,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        host.Children.Add(progress);
        return host;
    }

    private void ToggleTheme(Button themeButton)
    {
        FinishCurrentSegment();
        _theme = _theme == "light" ? "dark" : "light";
        App.ApplyTheme(_theme);
        themeButton.Content = _theme == "light" ? "切换深色" : "切换浅色";
        _lastRenderingTime = null;
        StartCurrentSegment();
        UpdateMeasurementText();
    }

    private void StartMeasurements()
    {
        CompositionTarget.Rendering += OnRendering;
        StartCurrentSegment();
        UpdateMeasurementText();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs rendering)
            return;

        var current = rendering.RenderingTime;
        if (_currentSegment is not null && _lastRenderingTime is TimeSpan previous && current > previous)
        {
            var interval = (current - previous).TotalMilliseconds;
            _currentSegment.Intervals.Add(interval);
        }
        _lastRenderingTime = current;
    }

    private void StartCurrentSegment()
    {
        _currentSegment = new RenderSegment(_theme, _process.TotalProcessorTime);
        _segments.Add(_currentSegment);
    }

    private void FinishCurrentSegment()
    {
        if (_currentSegment is null)
            return;

        _currentSegment.Finish(_process.TotalProcessorTime, _process.WorkingSet64);
        _currentSegment = null;
    }

    private void SaveMeasurements()
    {
        CompositionTarget.Rendering -= OnRendering;
        FinishCurrentSegment();
        var dpi = VisualTreeHelper.GetDpi(this);
        var result = new
        {
            WindowTitle = Title,
            StartedAtUtc = _segments.FirstOrDefault()?.StartedAtUtc,
            SystemClientAreaAnimation = SystemParameters.ClientAreaAnimation,
            DpiScale = new { dpi.DpiScaleX, dpi.DpiScaleY },
            PhysicalDpi = new { dpi.PixelsPerInchX, dpi.PixelsPerInchY },
            Indicators = new { CircularPerScheme = 3, LinearPerScheme = 2, Total = 20 },
            Samples = _segments.Select(segment => segment.ToResult()).ToArray(),
            SamplingNote = "RenderingTime 间隔记录 WPF Rendering 回调节奏，不代表物理屏幕出光延迟。"
        };
        Directory.CreateDirectory(_output);
        File.WriteAllText(Path.Combine(_output, "performance.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }

    private WindowFrameCapture.WindowFrame CaptureVisualFrame()
    {
        UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return new WindowFrameCapture.WindowFrame(width, height, pixels);
    }

    internal async Task<IReadOnlyList<NativeProgressBarDiagnostic>> SaveScreenshotAsync(string path)
    {
        UpdateLayout();
        var diagnostics = InspectNativeProgressBars();
        Activate();
        await Task.Delay(250);
        var frame = CaptureVisualFrame();
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = BitmapSource.Create(frame.Width, frame.Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY,
            PixelFormats.Pbgra32, null, frame.Pixels, frame.Width * 4);
        bitmap.Freeze();
        using (var stream = File.Create(path))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        File.WriteAllText(Path.Combine(_output, "progress-bar-diagnostics.json"),
            JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }));
        return diagnostics;
    }

    internal async Task StartRecordingAsync(string outputDirectory, TimeSpan duration, Action completed)
    {
        if (_recording is not null)
            throw new InvalidOperationException("加载动画录制已经在进行中。");

        var videoPath = Path.Combine(outputDirectory, "loading-comparison-60fps.mp4");
        Process? ffmpeg = null;
        try
        {
            var ffmpegPath = FindFfmpeg()
                ?? throw new FileNotFoundException("找不到 ffmpeg.exe，无法录制实际 WPF 窗口。");
            Activate();
            await Task.Delay(350);
            UpdateLayout();
            var initialFrame = CaptureVisualFrame();
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-y");
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-loglevel");
            startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add("rawvideo");
            startInfo.ArgumentList.Add("-pixel_format");
            startInfo.ArgumentList.Add("bgra");
            startInfo.ArgumentList.Add("-video_size");
            startInfo.ArgumentList.Add($"{initialFrame.Width}x{initialFrame.Height}");
            startInfo.ArgumentList.Add("-framerate");
            startInfo.ArgumentList.Add(RecordingSession.TargetFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add("pipe:0");
            startInfo.ArgumentList.Add("-vf");
            startInfo.ArgumentList.Add("pad=ceil(iw/2)*2:ceil(ih/2)*2");
            startInfo.ArgumentList.Add("-c:v");
            startInfo.ArgumentList.Add("libx264");
            startInfo.ArgumentList.Add("-preset");
            startInfo.ArgumentList.Add("veryfast");
            startInfo.ArgumentList.Add("-crf");
            startInfo.ArgumentList.Add("19");
            startInfo.ArgumentList.Add("-pix_fmt");
            startInfo.ArgumentList.Add("yuv420p");
            // ffmpeg 4.3.1 does not support -fps_mode. -vsync cfr keeps
            // the recorded stream at the requested output rate on that version.
            startInfo.ArgumentList.Add("-vsync");
            startInfo.ArgumentList.Add("cfr");
            startInfo.ArgumentList.Add("-r");
            startInfo.ArgumentList.Add(RecordingSession.TargetFps.ToString(System.Globalization.CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-movflags");
            startInfo.ArgumentList.Add("+faststart");
            startInfo.ArgumentList.Add(videoPath);

            ffmpeg = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 ffmpeg 录制进程。");
            _recording = new RecordingSession();
            var errorTask = ffmpeg.StandardError.ReadToEndAsync();
            var frameCount = (int)Math.Ceiling(duration.TotalSeconds * RecordingSession.TargetFps);
            var frameIntervals = new List<double>(frameCount);
            var captureClock = Stopwatch.StartNew();
            long previousCaptureAt = 0;
            for (var index = 0; index < frameCount; index++)
            {
                var targetTime = TimeSpan.FromSeconds((double)index / RecordingSession.TargetFps);
                var wait = targetTime - captureClock.Elapsed;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait);

                var frame = CaptureVisualFrame();
                var capturedAt = captureClock.ElapsedTicks;
                if (previousCaptureAt > 0)
                    frameIntervals.Add((capturedAt - previousCaptureAt) * 1000d / Stopwatch.Frequency);
                previousCaptureAt = capturedAt;
                await ffmpeg.StandardInput.BaseStream.WriteAsync(frame.Pixels);
            }

            ffmpeg.StandardInput.Close();
            await ffmpeg.WaitForExitAsync();
            var ffmpegError = await errorTask;
            var video = ffmpeg.ExitCode == 0 ? await ReadVideoMetadataAsync(videoPath).ConfigureAwait(false) : null;
            var orderedIntervals = frameIntervals.Order().ToArray();
            var metadata = new
            {
                CaptureSource = "WPF RenderTargetBitmap of the comparison window visual tree",
                TargetFps = RecordingSession.TargetFps,
                RequestedDurationMs = duration.TotalMilliseconds,
                ActualCaptureDurationMs = captureClock.Elapsed.TotalMilliseconds,
                CapturedFrames = video?.FrameCount,
                VideoDurationSeconds = video?.DurationSeconds,
                AverageFrameRateFps = video?.AverageFrameRateFps,
                Width = video?.Width,
                Height = video?.Height,
                CaptureFrameMedianIntervalMs = Percentile(orderedIntervals, 0.5),
                CaptureFrameP95IntervalMs = Percentile(orderedIntervals, 0.95),
                CaptureFrameMaximumIntervalMs = orderedIntervals.LastOrDefault(),
                FfmpegExitCode = ffmpeg.ExitCode,
                FfmpegError = ffmpegError,
                VideoPath = ffmpeg.ExitCode == 0 ? videoPath : null,
                SamplingNote = "帧按固定 60 FPS 目标从 WPF 视觉树采样后交给 ffmpeg 编码。该采样率不表示物理显示器刷新率或 WPF 实际回调节奏。"
            };
            File.WriteAllText(Path.Combine(outputDirectory, "recording.json"),
                JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            if (video is null || ffmpeg.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg 录制失败。{ffmpegError}");
        }
        catch (Exception ex)
        {
            if (ffmpeg is { HasExited: false })
            {
                try { ffmpeg.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            File.WriteAllText(Path.Combine(outputDirectory, "recording.json"),
                JsonSerializer.Serialize(new { Error = ex.ToString(), CaptureSource = "WPF RenderTargetBitmap of the comparison window visual tree" },
                    new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(ex);
        }
        finally
        {
            ffmpeg?.Dispose();
            _recording = null;
            await Dispatcher.InvokeAsync(completed);
        }
    }

    private static async Task<VideoMetadata?> ReadVideoMetadataAsync(string videoPath)
    {
        var ffmpegPath = FindFfmpeg();
        if (ffmpegPath is null)
            return null;

        var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath)!, "ffprobe.exe");
        if (!File.Exists(ffprobePath))
            return null;

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-count_frames");
        startInfo.ArgumentList.Add("-select_streams");
        startInfo.ArgumentList.Add("v:0");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("stream=avg_frame_rate,nb_read_frames,duration,width,height");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(videoPath);

        using var ffprobe = Process.Start(startInfo);
        if (ffprobe is null)
            return null;

        var outputTask = ffprobe.StandardOutput.ReadToEndAsync();
        var errorTask = ffprobe.StandardError.ReadToEndAsync();
        await ffprobe.WaitForExitAsync().ConfigureAwait(false);
        if (ffprobe.ExitCode != 0)
            throw new InvalidOperationException(await errorTask.ConfigureAwait(false));

        using var document = JsonDocument.Parse(await outputTask.ConfigureAwait(false));
        var stream = document.RootElement.GetProperty("streams")[0];
        var frameRate = stream.GetProperty("avg_frame_rate").GetString() ?? "0/1";
        var frameRateParts = frameRate.Split('/');
        var averageFrameRate = frameRateParts.Length == 2
            && double.TryParse(frameRateParts[0], System.Globalization.CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(frameRateParts[1], System.Globalization.CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0 ? numerator / denominator : 0;
        var frameCount = int.TryParse(stream.GetProperty("nb_read_frames").GetString(), out var frames) ? frames : 0;
        var duration = double.TryParse(stream.GetProperty("duration").GetString(), System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? seconds
            : 0;
        return new VideoMetadata(frameCount, duration, averageFrameRate,
            stream.GetProperty("width").GetInt32(), stream.GetProperty("height").GetInt32());
    }

    private static string? FindFfmpeg()
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is not null)
        {
            foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(directory.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var fallback = Path.Combine(userProfile, "miniconda3", "Library", "bin", "ffmpeg.exe");
        return File.Exists(fallback) ? fallback : null;
    }

    private IReadOnlyList<NativeProgressBarDiagnostic> InspectNativeProgressBars()
    {
        var diagnostics = new List<NativeProgressBarDiagnostic>(_nativeProgressBars.Count);

        foreach (var progressBar in _nativeProgressBars)
        {
            progressBar.ApplyTemplate();
            var scaleY = (progressBar.RenderTransform as ScaleTransform)?.ScaleY ?? 1;
            var host = progressBar.Parent as FrameworkElement;
            var track = progressBar.Template?.FindName("PART_Track", progressBar) as FrameworkElement;
            var indicator = progressBar.Template?.FindName("PART_Indicator", progressBar) as FrameworkElement;
            diagnostics.Add(new NativeProgressBarDiagnostic(
                progressBar.Width,
                progressBar.Height,
                progressBar.ActualWidth,
                progressBar.ActualHeight,
                progressBar.ActualHeight * Math.Abs(scaleY),
                host?.ActualHeight ?? 0,
                progressBar.IsIndeterminate,
                progressBar.IsVisible,
                progressBar.Style?.TargetType?.FullName,
                progressBar.Template?.TargetType.FullName,
                track?.ActualWidth,
                track is null ? null : track.ActualHeight * Math.Abs(scaleY),
                indicator?.ActualWidth,
                indicator is null ? null : indicator.ActualHeight * Math.Abs(scaleY)));
        }

        return diagnostics;
    }

    private void UpdateMeasurementText()
    {
        if (_currentSegment is null || _currentSegment.Intervals.Count == 0)
        {
            _measurementText.Text = $"主题：{(_theme == "light" ? "浅色" : "深色")} · 等待渲染采样";
            return;
        }

        var ordered = _currentSegment.Intervals.Order().ToArray();
        var median = Percentile(ordered, 0.5);
        var p95 = Percentile(ordered, 0.95);
        _measurementText.Text =
            $"主题：{(_theme == "light" ? "浅色" : "深色")} · WPF 回调 {ordered.Length} 帧 · 中位数 {median:F2} ms · P95 {p95:F2} ms";
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
            return 0;

        var index = Math.Clamp((int)Math.Ceiling(ordered.Length * percentile) - 1, 0, ordered.Length - 1);
        return ordered[index];
    }

    private static Border Center(FrameworkElement element)
    {
        var border = new Border
        {
            Child = element,
            Padding = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        border.SetResourceReference(BackgroundProperty, "Surface");
        border.SetResourceReference(BorderBrushProperty, "Line");
        border.BorderThickness = new Thickness(0, 0, 1, 1);
        return border;
    }

    private static Border CreateColumnHeader(string title, string subtitle)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        var titleText = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14 };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var subtitleText = new TextBlock { Text = subtitle, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        stack.Children.Add(titleText);
        stack.Children.Add(subtitleText);
        return CreateCellBorder(stack, "Surface");
    }

    private static Border CreateRowLabel(string title, string subtitle)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 8, 0) };
        var titleText = new TextBlock { Text = title, FontWeight = FontWeights.Medium, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        var subtitleText = new TextBlock { Text = subtitle, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
        subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        stack.Children.Add(titleText);
        stack.Children.Add(subtitleText);
        return CreateCellBorder(stack, "Sidebar");
    }

    private static Border CreateCellBorder(UIElement child, string backgroundKey)
    {
        var border = new Border
        {
            Child = child,
            BorderThickness = new Thickness(0, 0, 1, 1)
        };
        border.SetResourceReference(BackgroundProperty, backgroundKey);
        border.SetResourceReference(BorderBrushProperty, "Line");
        return border;
    }

    private static void AddCell(Grid grid, int row, int column, UIElement child)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }

    private sealed class RenderSegment(string theme, TimeSpan processorTimeAtStart)
    {
        private readonly Stopwatch _duration = Stopwatch.StartNew();
        private readonly TimeSpan _processorTimeAtStart = processorTimeAtStart;
        private TimeSpan _processorTimeAtEnd;
        private long _workingSetBytes;

        internal string Theme { get; } = theme;
        internal DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;
        internal List<double> Intervals { get; } = [];

        internal void Finish(TimeSpan processorTimeAtEnd, long workingSetBytes)
        {
            _duration.Stop();
            _processorTimeAtEnd = processorTimeAtEnd;
            _workingSetBytes = workingSetBytes;
        }

        internal object ToResult()
        {
            var intervals = Intervals.Order().ToArray();
            var processorMs = Math.Max(0, (_processorTimeAtEnd - _processorTimeAtStart).TotalMilliseconds);
            var durationMs = Math.Max(1, _duration.Elapsed.TotalMilliseconds);
            var over16Point67Ms = Intervals.Count(interval => interval > 1000d / 60d);
            return new
            {
                Theme,
                StartedAtUtc,
                DurationMs = Math.Round(durationMs, 2),
                RenderingCallbacks = Intervals.Count,
                MedianIntervalMs = Math.Round(Percentile(intervals, 0.5), 3),
                P95IntervalMs = Math.Round(Percentile(intervals, 0.95), 3),
                MaximumIntervalMs = Math.Round(intervals.Length == 0 ? 0 : intervals[^1], 3),
                IntervalsOver16Point67Ms = over16Point67Ms,
                AverageProcessCpuPercent = Math.Round(processorMs / durationMs / Environment.ProcessorCount * 100, 2),
                WorkingSetBytes = _workingSetBytes
            };
        }
    }

    private sealed class RecordingSession
    {
        internal const int TargetFps = 60;
    }

    private sealed record VideoMetadata(int FrameCount, double DurationSeconds, double AverageFrameRateFps, int Width, int Height);

    internal sealed record NativeProgressBarDiagnostic(
        double RequestedWidth,
        double RequestedHeight,
        double ActualWidth,
        double ActualHeight,
        double RenderedHeight,
        double HostHeight,
        bool IsIndeterminate,
        bool IsVisible,
        string? StyleTargetType,
        string? TemplateTargetType,
        double? TrackWidth,
        double? TrackHeight,
        double? IndicatorWidth,
        double? IndicatorHeight);
}

internal static class WindowFrameCapture
{
    internal sealed record WindowFrame(int Width, int Height, byte[] Pixels);
}

internal static class LoadingComparisonMathChecks
{
    internal static void Run(string output)
    {
        var start = MuiAnimationMath.CircularFrame(0);
        RequireClose(start.DashLength, 1, "MUI circular start dash");
        RequireClose(start.DashOffset, 0, "MUI circular start offset");
        var circularMiddle = MuiAnimationMath.CircularFrame(0.5);
        RequireClose(circularMiddle.DashLength, 100, "MUI circular midpoint dash");
        RequireClose(circularMiddle.DashOffset, -15, "MUI circular midpoint offset");
        RequireClose(circularMiddle.RotationDegrees, 180, "MUI circular midpoint rotation");
        var circularEnd = MuiAnimationMath.CircularFrame(1);
        RequireClose(circularEnd.DashLength, 1, "MUI circular end dash");
        RequireClose(circularEnd.DashOffset, -126, "MUI circular end offset");
        RequireClose(circularEnd.RotationDegrees, 360, "MUI circular end rotation");
        var reducedMotion = MuiAnimationMath.CircularFrame(0.5, reducedMotion: true);
        RequireClose(reducedMotion.DashLength, 80, "MUI reduced-motion dash");
        RequireClose(reducedMotion.DashOffset, 0, "MUI reduced-motion offset");
        RequireClose(reducedMotion.RotationDegrees, 0, "MUI reduced-motion rotation");

        var firstStart = MuiAnimationMath.FirstLinearSegment(0);
        RequireClose(firstStart.Left, -0.35, "MUI first bar start left");
        RequireClose(firstStart.Right, 1, "MUI first bar start right");
        var firstEnd = MuiAnimationMath.FirstLinearSegment(0.6);
        RequireClose(firstEnd.Left, 1, "MUI first bar end left");
        RequireClose(firstEnd.Right, -0.9, "MUI first bar end right");
        var secondStart = MuiAnimationMath.SecondLinearSegment(0);
        RequireClose(secondStart.Left, -2, "MUI second bar start left");
        RequireClose(secondStart.Right, 1, "MUI second bar start right");
        var secondEnd = MuiAnimationMath.SecondLinearSegment(0.6);
        RequireClose(secondEnd.Left, 1.07, "MUI second bar end left");
        RequireClose(secondEnd.Right, -0.08, "MUI second bar end right");
        RequireClose(MuiAnimationMath.CubicBezier(0.5, 0.42, 0, 0.58, 1), 0.5, "CSS ease-in-out midpoint");

        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "model-checks.json"), JsonSerializer.Serialize(new
        {
            MuiVersion = "9.3.1",
            ViewBoxUnits = 44,
            CircleRadius = (44 - 3.6) / 2,
            CircleCircumference = 2 * Math.PI * ((44 - 3.6) / 2),
            CircularCycleMs = 1400,
            LinearCycleMs = 2100,
            SecondLinearDelayMs = 1150,
            ChecksPassed = 20
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RequireClose(double actual, double expected, string name)
    {
        if (Math.Abs(actual - expected) > 1e-6)
            throw new InvalidOperationException($"{name} expected {expected}, got {actual}.");
    }
}

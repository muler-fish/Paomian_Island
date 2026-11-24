using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class LoadingMotionChecks
{
    internal static async Task RunAsync()
    {
        const int width = 140, diameter = 36, barHeight = 4, height = diameter + barHeight;
        var panel = new Canvas { Width = width, Height = height, Visibility = Visibility.Hidden };
        panel.Resources[SystemParameters.ClientAreaAnimationKey] = true;
        var ring = new LoadingIndicator { Width = diameter, Height = diameter, Circular = true };
        ring.SetResourceReference(Control.ForegroundProperty, "Accent");
        var bar = new ProgressBar { Width = width, Height = barHeight };
        bar.SetResourceReference(FrameworkElement.StyleProperty, "LoadingProgress");
        Canvas.SetTop(bar, diameter);
        panel.Children.Add(ring);
        panel.Children.Add(bar);

        // 接入 WPF 动画时钟，无需显示窗口或占用键鼠。
        using var source = new HwndSource(new HwndSourceParameters("LoadingMotion")
        {
            WindowStyle = unchecked((int)0x80000000), Width = width, Height = height
        });
        source.RootVisual = panel;
        await panel.Dispatcher.InvokeAsync(panel.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if (!ring.IsLoaded || !bar.IsLoaded) throw new InvalidOperationException("加载控件未进入视觉树");
        var timer = Stopwatch.StartNew();
        panel.Visibility = Visibility.Visible;
        var circles = new List<int[]>();
        var firstSegments = new List<int[]>();
        var secondSegments = new List<int[]>();
        while (timer.ElapsedMilliseconds < 2050)
        {
            await Task.Delay(55);
            var start = timer.ElapsedMilliseconds;
            var frame = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            frame.Render(panel);
            var end = timer.ElapsedMilliseconds;
            // 1150 ms 后第二段才开始，1260 ms 后第一段已离开轨道。
            if (start >= 100 && end <= 1050)
            {
                AddFrame(circles, frame, new Int32Rect(0, 0, diameter, diameter));
                AddFrame(firstSegments, frame, new Int32Rect(0, diameter, width, barHeight));
            }
            if (start >= 1400 && end <= 2050)
                AddFrame(secondSegments, frame, new Int32Rect(0, diameter, width, barHeight));
        }
        var result = $"不同画面数：圆环 {circles.Count}，线性首段 {firstSegments.Count}，线性次段 {secondSegments.Count}";
        Console.WriteLine(result);
        if (circles.Count < 2 || firstSegments.Count < 2 || secondSegments.Count < 2)
            throw new InvalidOperationException("加载动画未在预期时间内移动。" + result);
    }

    private static void AddFrame(List<int[]> frames, BitmapSource bitmap, Int32Rect area)
    {
        var pixels = new int[area.Width * area.Height];
        bitmap.CopyPixels(area, pixels, area.Width * sizeof(int), 0);
        if (!frames.Any(previous => pixels.AsSpan().SequenceEqual(previous))) frames.Add(pixels);
    }
}

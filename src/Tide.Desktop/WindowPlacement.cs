using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tide.Desktop;

/// <summary>首次显示前按所在显示器的工作区和 DPI 安排窗口，避免标题栏落到屏幕外。</summary>
internal static class WindowPlacement
{
    internal readonly record struct StartupBounds(Rect Bounds, Size Minimum);

    // Category dialogs remain resizable, but cannot grow beyond their current monitor's work area.
    internal static void ConstrainToWorkArea(Window window)
    {
        void Update()
        {
            var handle = new WindowInteropHelper(window).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window).DpiScaleX;
            if (handle == IntPtr.Zero || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
            var width = (info.Work.Right - info.Work.Left) / dpi;
            var height = (info.Work.Bottom - info.Work.Top) / dpi;
            window.MinWidth = Math.Min(window.MinWidth, width); window.MinHeight = Math.Min(window.MinHeight, height);
            window.MaxWidth = width; window.MaxHeight = height;
        }
        window.SourceInitialized += (_, _) => Update();
        window.LocationChanged += (_, _) => Update();
        window.DpiChanged += (_, _) => window.Dispatcher.BeginInvoke((Action)Update);
    }

    // Async reference content arrives after startup placement. Grow/shrink around the current
    // window centre, constrained by the actual monitor work area and its DPI.
    internal static void FitContentHeight(Window window, double preferredHeight)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var dpi = GetDpiForWindow(handle);
        if (dpi == 0 || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref info)) return;
        var scale = dpi / 96d;
        var top = info.Work.Top / scale;
        var bottom = info.Work.Bottom / scale;
        var height = Math.Clamp(preferredHeight, Math.Min(window.MinHeight, bottom - top), bottom - top);
        var center = window.Top + window.ActualHeight / 2;
        window.Height = height;
        window.Top = Math.Clamp(center - height / 2, top, bottom - height);
    }

    public static void FitOnStartup(Window window, bool restoreSize = false, bool maximized = false)
    {
        // 此时 WPF 已选定显示器并创建 HWND，但窗口内容尚未呈现，可以在首次显示前修正位置。
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            var monitor = MonitorFromWindow(handle, 2); // MONITOR_DEFAULTTONEAREST
            if (!GetMonitorInfo(monitor, ref info)) return;
            var dpi = GetDpiForWindow(handle);
            if (dpi == 0) return;

            // Win32 工作区是物理像素，WPF 尺寸是 DIP；两者混用会在 125% / 150% 缩放时越界。
            var work = new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
            var placement = Calculate(work, dpi / 96d, new(window.Width, window.Height), new(window.MinWidth, window.MinHeight), restoreSize);
            // 先放宽最小值，否则小屏上的 MinHeight / MinWidth 会把已收小的窗口重新撑大。
            window.MinWidth = placement.Minimum.Width;
            window.MinHeight = placement.Minimum.Height;
            window.Width = placement.Bounds.Width;
            window.Height = placement.Bounds.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = placement.Bounds.Left;
            window.Top = placement.Bounds.Top;
            if (window.Owner is { } owner && GetWindowRect(new WindowInteropHelper(owner).Handle, out var ownerRect))
            {
                var scale = dpi / 96d;
                var left = (ownerRect.Left + ownerRect.Right) / (2 * scale) - window.Width / 2;
                var top = (ownerRect.Top + ownerRect.Bottom) / (2 * scale) - window.Height / 2;
                window.Left = Math.Clamp(left, work.Left / scale, work.Right / scale - window.Width);
                window.Top = Math.Clamp(top, work.Top / scale, work.Bottom / scale - window.Height);
            }
            if (maximized) window.WindowState = WindowState.Maximized;
        };
    }

    /// <summary>按工作区限制尺寸并居中，首次启动另留四周空隙；支持副屏负坐标和侧边任务栏。</summary>
    internal static StartupBounds Calculate(Rect workAreaPixels, double dpiScale, Size preferred, Size minimum, bool restoreSize = false)
    {
        var work = new Rect(workAreaPixels.X / dpiScale, workAreaPixels.Y / dpiScale,
            workAreaPixels.Width / dpiScale, workAreaPixels.Height / dpiScale);
        // 首次启动保留四周空隙；恢复时允许使用完整工作区，避免每次启动把用户尺寸缩小。
        var available = new Size(work.Width * (restoreSize ? 1 : .9), work.Height * (restoreSize ? 1 : .9));
        var adjustedMinimum = new Size(Math.Min(minimum.Width, available.Width), Math.Min(minimum.Height, available.Height));
        var width = Math.Clamp(preferred.Width, adjustedMinimum.Width, available.Width);
        var height = Math.Clamp(preferred.Height, adjustedMinimum.Height, available.Height);
        return new(new Rect(work.Left + (work.Width - width) / 2, work.Top + (work.Height - height) / 2, width, height), adjustedMinimum);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
}

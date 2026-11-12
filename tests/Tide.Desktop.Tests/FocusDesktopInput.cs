using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace Tide.Desktop.Tests;

// 只由显式指定外部 HWND 的焦点专项使用。
internal sealed class FocusDesktopInput : IDisposable
{
    private readonly NativePoint _originalCursor;

    internal FocusDesktopInput()
    {
        Require(GetForegroundWindow() != 0, "当前线程没有接入真实输入桌面");
        Require(GetCursorPos(out _originalCursor), "无法读取当前鼠标位置");
    }

    public void Dispose() => SetCursorPos(_originalCursor.X, _originalCursor.Y);

    internal static async Task ActivateAsync(nint target)
    {
        var foreground = GetForegroundWindow();
        if (foreground == target) return;
        var thread = GetCurrentThreadId();
        var previous = GetWindowThreadProcessId(foreground, out _);
        var attached = previous != 0 && previous != thread && AttachThreadInput(thread, previous, true);
        try
        {
            if (IsIconic(target)) ShowWindow(target, 9); // SW_RESTORE
            SetForegroundWindow(target);
        }
        finally
        {
            if (attached) AttachThreadInput(thread, previous, false);
        }
        for (var i = 0; i < 40 && GetForegroundWindow() != target; i++) await Task.Delay(25);
        Require(GetForegroundWindow() == target, $"测试窗口 0x{target:X} 未能进入前台");
    }

    internal static void Click(FrameworkElement element, nint expected)
    {
        Guard(expected);
        var p = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        var point = new NativePoint { X = (int)p.X, Y = (int)p.Y };
        Require(GetAncestor(WindowFromPoint(point), 2) == expected, "目标控件被遮挡，取消点击");
        ClickPoint(point);
    }

    internal static void Key(nint expected, ushort key)
    {
        Guard(expected);
        Send([KeyInput(key), KeyInput(key, true)]);
    }

    internal static async Task AltTabAsync(nint expected, bool advanceTwice = false)
    {
        Guard(expected);
        const ushort alt = 0x12, tab = 0x09;
        try
        {
            Send([KeyInput(alt), KeyInput(tab), KeyInput(tab, true)]);
            await Task.Delay(300);
            if (advanceTwice)
            {
                Send([KeyInput(tab), KeyInput(tab, true)]);
                await Task.Delay(150);
            }
        }
        finally { Send([KeyInput(alt, true)]); }
        await Task.Delay(300);
    }

    internal static void ClickTaskbar(nint expected, nint target)
    {
        GetWindowThreadProcessId(target, out var pid);
        using var process = Process.GetProcessById((int)pid);
        var appId = process.ProcessName switch
        {
            "msedge" => "Appid: MSEdge",
            "explorer" => "Appid: Microsoft.Windows.Explorer",
            _ => throw new InvalidOperationException("任务栏测试只识别 Edge 与文件资源管理器"),
        };
        var taskbar = FindWindow("Shell_TrayWnd", null);
        Require(taskbar != 0, "找不到 Windows 任务栏");
        var button = AutomationElement.FromHandle(taskbar).FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, appId));
        Require(button is not null, "找不到目标应用的任务栏按钮");
        var bounds = button!.Current.BoundingRectangle;
        var point = new NativePoint { X = (int)(bounds.Left + bounds.Width / 2), Y = (int)(bounds.Top + bounds.Height / 2) };
        Guard(expected);
        Require(GetAncestor(WindowFromPoint(point), 2) == taskbar, "任务栏按钮被遮挡，取消点击");
        ClickPoint(point);
    }

    private static void ClickPoint(NativePoint point)
    {
        Require(SetCursorPos(point.X, point.Y), "无法定位鼠标");
        Send([new() { Data = new() { Mouse = new() { Flags = 0x02 } } },
              new() { Data = new() { Mouse = new() { Flags = 0x04 } } }]);
    }

    private static void Guard(nint expected) => Require(expected != 0 && GetForegroundWindow() == expected, "测试窗口已离开前台，停止发送输入");
    private static NativeInput KeyInput(ushort key, bool up = false) => new() { Type = 1, Data = new() { Keyboard = new() { Key = key, Flags = up ? 2u : 0u } } };
    private static void Send(NativeInput[] input) => Require(SendInput((uint)input.Length, input, Marshal.SizeOf<NativeInput>()) == input.Length, "Windows 未接收完整的测试输入");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeInput { public uint Type; public InputData Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint handle);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint handle);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string className, string? title);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool value);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}

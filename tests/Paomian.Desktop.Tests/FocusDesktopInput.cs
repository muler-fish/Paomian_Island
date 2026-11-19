using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace Paomian.Desktop.Tests;

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

    internal static void Chord(nint expected, ushort modifier, ushort key)
    {
        Guard(expected);
        Send([KeyInput(modifier), KeyInput(key), KeyInput(key, true), KeyInput(modifier, true)]);
    }

    // 仅在已确认注册成功的老板键专项中使用。组合键事件作为一个批次送入。
    internal static void BossChord(nint expected, ShortcutKey shortcut, bool repeat = false)
    {
        Guard(expected);
        var keys = new List<ushort>();
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Control)) keys.Add(0x11);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Alt)) keys.Add(0x12);
        if (shortcut.Modifiers.HasFlag(ModifierKeys.Shift)) keys.Add(0x10);
        keys.Add((ushort)KeyInterop.VirtualKeyFromKey(shortcut.Key));
        var inputs = keys.Select(k => KeyInput(k)).ToList();
        if (repeat) inputs.Add(KeyInput(keys[^1]));
        inputs.AddRange(keys.AsEnumerable().Reverse().Select(k => KeyInput(k, true)));
        Send(inputs.ToArray());
    }

    internal static void ClickAt(FrameworkElement element, Point location, nint expected) =>
        ClickPoint(CheckedPoint(element, location, expected));

    internal static void RightClick(FrameworkElement element, Point location, nint expected)
    {
        var point = CheckedPoint(element, location, expected);
        Require(SetCursorPos(point.X, point.Y), "无法定位鼠标");
        Send([MouseInputFor(0x08), MouseInputFor(0x10)]);
    }

    internal static async Task Drag(FrameworkElement element, Point from, Point to, nint expected)
    {
        var start = CheckedPoint(element, from, expected);
        var end = CheckedPoint(element, to, expected);
        Require(SetCursorPos(start.X, start.Y), "无法定位拖选起点");
        await Task.Delay(30);
        Guard(expected);
        Send([MouseInputFor(0x02)]);
        try
        {
            for (var step = 1; step <= 6; step++)
            {
                await Task.Delay(28);
                Guard(expected);
                Require(SetCursorPos(start.X + (end.X - start.X) * step / 6, start.Y + (end.Y - start.Y) * step / 6), "无法拖动光标");
            }
            await Task.Delay(40);
        }
        finally { Send([MouseInputFor(0x04)]); }
        await Task.Delay(80);
    }

    private static NativePoint CheckedPoint(FrameworkElement element, Point location, nint expected)
    {
        Guard(expected);
        var p = element.PointToScreen(location);
        var point = new NativePoint { X = (int)p.X, Y = (int)p.Y };
        Require(GetAncestor(WindowFromPoint(point), 2) == expected, "测试位置被遮挡");
        return point;
    }

    private static NativeInput MouseInputFor(uint flags) => new() { Data = new() { Mouse = new() { Flags = flags } } };

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

    internal static void OpenTaskSwitcher(nint expected)
    {
        Guard(expected);
        Send([KeyInput(0x11), KeyInput(0x12), KeyInput(0x09), KeyInput(0x09, true), KeyInput(0x12, true), KeyInput(0x11, true)]);
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

    internal static void ClickShell(Point location, nint shellWindow, bool right = false, bool twice = false)
    {
        var point = new NativePoint { X = (int)location.X, Y = (int)location.Y };
        Require(shellWindow != 0 && GetAncestor(WindowFromPoint(point), 2) == shellWindow, "托盘或菜单目标已被遮挡");
        Require(SetCursorPos(point.X, point.Y), "无法定位系统托盘鼠标");
        var down = MouseInputFor(right ? 0x08u : 0x02u);
        var up = MouseInputFor(right ? 0x10u : 0x04u);
        Send(twice ? [down, up, down, up] : [down, up]);
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

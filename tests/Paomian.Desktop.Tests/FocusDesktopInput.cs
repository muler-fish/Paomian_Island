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

    internal static void Wheel(nint expected, int delta)
    {
        Guard(expected);
        Require(GetCursorPos(out var point), "无法读取滚轮位置");
        VerifyEndpoint(point, expected, "滚轮位置");
        var input = MouseInputFor(0x0800);
        input.Data.Mouse.Data = unchecked((uint)delta);
        Send([input]);
    }

    internal static void Chord(nint expected, ushort modifier, ushort key)
    {
        Guard(expected);
        Send([KeyInput(modifier), KeyInput(key), KeyInput(key, true), KeyInput(modifier, true)]);
    }

    internal static void TypeUnicode(nint expected, string text)
    {
        Guard(expected);
        NativeInput Character(char ch, bool up) => new() { Type = 1, Data = new() { Keyboard = new() { Scan = ch, Flags = up ? 6u : 4u } } };
        Send(text.SelectMany(ch => new[] { Character(ch, false), Character(ch, true) }).ToArray());
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

    internal static void Hover(FrameworkElement element, nint expected, Point? location = null)
    {
        var p = CheckedPoint(element, location ?? new Point(element.ActualWidth / 2, element.ActualHeight / 2), expected);
        Require(SetCursorPos(p.X, p.Y), "无法移动到测试控件");
    }

    private static void VerifyEndpoint(NativePoint point, nint expected, string label)
    {
        var hit = GetAncestor(WindowFromPoint(point), 2);
        if (hit == expected) return;
        var cls = new System.Text.StringBuilder(100); GetClassName(hit, cls, cls.Capacity);
        GetWindowRect(expected, out var box);
        throw new InvalidOperationException($"{label}被遮挡 ({point.X},{point.Y})，命中 {cls}，期望窗口范围 {box.Left},{box.Top},{box.Right},{box.Bottom}");
    }

    internal static void ClickExplorerFile(nint shell, Point item)
    {
        var point = new NativePoint { X = (int)item.X, Y = (int)item.Y };
        VerifyEndpoint(point, shell, "测试文件图标");
        ClickPoint(point);
    }

    internal static async Task DragFileFromExplorer(nint explorer, Point item, FrameworkElement target, nint owner)
    {
        Guard(explorer);
        var start = new NativePoint { X = (int)item.X, Y = (int)item.Y };
        var end = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
        var to = new NativePoint { X = (int)end.X, Y = (int)end.Y };
        VerifyEndpoint(start, explorer, "测试文件图标");
        VerifyEndpoint(to, owner, "正文框");
        Require(SetCursorPos(start.X, start.Y), "无法定位测试文件"); await Task.Delay(100); Guard(explorer); Send([MouseInputFor(0x02)]);
        try
        {
            await Task.Delay(90);
            var left = GetSystemMetrics(76); var top = GetSystemMetrics(77);
            var width = GetSystemMetrics(78); var height = GetSystemMetrics(79);
            for (int i = 1; i <= 24; i++)
            {
                await Task.Delay(45);
                var front = GetForegroundWindow();
                if (front != explorer && front != owner)
                {
                    var name = new System.Text.StringBuilder(100);
                    GetClassName(front, name, name.Capacity); GetWindowThreadProcessId(front, out var pid);
                    throw new InvalidOperationException($"拖放期间前台改变（第 {i} 步，类 {name}，PID {pid}）。");
                }
                var x = start.X + (to.X - start.X) * i / 24; var y = start.Y + (to.Y - start.Y) * i / 24;
                Send([new NativeInput { Data = new() { Mouse = new() { X = (int)(((x - left + .5) * 65536) / width), Y = (int)(((y - top + .5) * 65536) / height), Flags = 0xC001 } } }]);
            }
            await Task.Delay(200); Require(GetAncestor(WindowFromPoint(to), 2) == owner, "放下图片前发言窗口被遮挡");
        }
        catch { if (GetForegroundWindow() == explorer) Key(explorer, 0x1B); throw; }
        finally { Send([MouseInputFor(0x04)]); }
    }

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

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, System.Text.StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}

using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using Paomian.Core;

namespace Paomian.Desktop.Tests;

/// <summary>在 Shell 中按已注册图标的矩形和可访问名称定位，再点击系统的真实菜单。</summary>
internal static class BossKeyShellChecks
{
    internal static async Task CheckSwitchListsAsync(nint external, string title, bool visible)
    {
        await FocusDesktopInput.ActivateAsync(external);
        await Task.Run(async () =>
        {
            var bar = ShellView(FindWindowEx(0, 0, "Shell_TrayWnd", null));
            bool OnTaskbar() => bar.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>().Any(e => e.Current.Name.Contains(title, StringComparison.Ordinal) ||
                    e.Current.AutomationId.Contains("Paomian.Desktop.Tests", StringComparison.OrdinalIgnoreCase));
            for (var i = 0; i < 30 && OnTaskbar() != visible; i++) await Task.Delay(50);
            Require(OnTaskbar() == visible, "Windows 任务栏的测试窗口状态错误：" + visible);
            FocusDesktopInput.OpenTaskSwitcher(external);
            nint switcher = 0;
            for (var i = 0; i < 40 && switcher == 0; i++)
            {
                await Task.Delay(50);
                var foreground = GetForegroundWindow();
                var name = new StringBuilder(100);
                GetClassName(foreground, name, name.Capacity);
                if (name.ToString() is "MultitaskingViewFrame" or "XamlExplorerHostIslandWindow" or "TaskSwitcherWnd") switcher = foreground;
            }
            Require(switcher != 0, "无法识别 Windows 任务切换器");
            try
            {
                await Task.Delay(220);
                var items = ShellView(switcher).FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition).Cast<AutomationElement>().ToList();
                Require(items.Count > 2 && items.Any(e => e.Current.Name.Contains(title, StringComparison.Ordinal)) == visible, "Alt+Tab 的测试窗口状态错误：" + visible);
            }
            finally { if (GetForegroundWindow() == switcher) FocusDesktopInput.Key(switcher, 0x1b); }
        });
        await Task.Delay(150);
    }

    internal static Task UseIconAsync(string? menuItem, int? processId = null) => Task.Run(async () =>
    {
        var expectedProcess = processId ?? Environment.ProcessId;
        nint host = 0;
        do
        {
            host = FindWindowEx(0, host, null, "PaomianIsland.BossKey");
            GetWindowThreadProcessId(host, out var pid);
            if (pid == expectedProcess) break;
        } while (host != 0);
        Require(host != 0, "找不到本实例的托盘消息窗口");
        var id = new IconId { Size = (uint)Marshal.SizeOf<IconId>(), Window = host, Id = 1 };
        var taskbar = FindWindowEx(0, 0, "Shell_TrayWnd", null);
        // 连续隐藏、恢复时，Shell 会先收起旧面板再给图标分配位置。
        // 等图标在同一面板连续两次命中，才向该位置送鼠标输入。
        (Point, nint)? icon = null, previous = null;
        var nextExpand = 2;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            await Task.Delay(100);
            icon = IconPoint();
            if (icon.HasValue && icon == previous) break;
            previous = icon;
            icon = null;
            if (previous.HasValue || attempt < nextExpand || IsWindowVisible(OverflowWindow())) continue;
            var more = ShellView(taskbar).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button))
                .Cast<AutomationElement>().FirstOrDefault(e => e.Current.AutomationId.Contains("Chevron", StringComparison.OrdinalIgnoreCase) ||
                    e.Current.Name is "显示隐藏的图标" or "隐藏的图标菜单" or "Show hidden icons" or "Hidden icons menu");
            Require(more is not null, "找不到系统托盘展开按钮");
            var bounds = more!.Current.BoundingRectangle;
            FocusDesktopInput.ClickShell(new(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2), taskbar);
            nextExpand = attempt + 6;
        }
        var (point, shell) = icon ?? throw new InvalidOperationException("未能定位本实例的泡面岛托盘图标");
        FocusDesktopInput.ClickShell(point, shell, right: menuItem is not null, twice: menuItem is null);
        if (menuItem is not null)
        {
            var clicked = false;
            for (var i = 0; i < 60 && !clicked; i++)
            {
                await Task.Delay(50);
                var menu = FindWindowEx(0, 0, "#32768", null);
                GetWindowThreadProcessId(menu, out var pid);
                if (pid != expectedProcess) continue;
                var nativeMenu = SendMessage(menu, 0x01e1, 0, 0); // MN_GETHMENU
                for (var index = 0; index < 2; index++)
                {
                    var text = new StringBuilder(80);
                    GetMenuString(nativeMenu, (uint)index, text, text.Capacity, 0x400);
                    if (text.ToString() != menuItem || !GetMenuItemRect(0, nativeMenu, (uint)index, out var item)) continue;
                    FocusDesktopInput.ClickShell(item.Center, menu);
                    clicked = true; break;
                }
            }
            Require(clicked, "未找到托盘菜单项「" + menuItem + "」");
        }
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(50);
            if (Shell_NotifyIconGetRect(ref id, out var finalRect) != 0 || finalRect.Right <= finalRect.Left)
                return;
        }
        throw new InvalidOperationException("操作完成后 Shell 仍返回本实例的图标位置");

        (Point, nint)? IconPoint()
        {
            if (Shell_NotifyIconGetRect(ref id, out var rect) < 0 || rect.Right <= rect.Left) return null;
            foreach (var root in new[] { taskbar, OverflowWindow() })
            {
                if (root == 0 || !IsWindowVisible(root)) continue;
                var icon = ShellView(root).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, AppIdentity.DisplayName));
                if (icon is null || !icon.Current.BoundingRectangle.Contains(rect.Center)) continue;
                var p = rect.Center;
                if (GetAncestor(WindowFromPoint(new() { X = (int)p.X, Y = (int)p.Y }), 2) == root) return (p, root);
            }
            return null;
        }
        static nint OverflowWindow()
        {
            var window = FindWindowEx(0, 0, "TopLevelWindowForOverflowXamlIsland", null);
            return window != 0 ? window : FindWindowEx(0, 0, "NotifyIconOverflowWindow", null);
        }
    });

    // Windows 11 的托盘控件在任务栏的 XAML 子窗口中，Shell_TrayWnd 自身只公开原生子树。
    private static AutomationElement ShellView(nint hwnd)
    {
        var bridge = FindWindowEx(hwnd, 0, "Windows.UI.Composition.DesktopWindowContentBridge", null);
        return AutomationElement.FromHandle(bridge == 0 ? hwnd : bridge);
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct IconId { public uint Size; public nint Window; public uint Id; public Guid Guid; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Point Center => new((Left + Right) / 2, (Top + Bottom) / 2);
    }
    [DllImport("shell32.dll")] private static extern int Shell_NotifyIconGetRect(ref IconId id, out NativeRect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMenuString(nint menu, uint item, StringBuilder text, int size, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMenuItemRect(nint hwnd, nint menu, uint item, out NativeRect rect);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flag);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}

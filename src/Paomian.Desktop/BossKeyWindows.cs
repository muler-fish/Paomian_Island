using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Paomian.Desktop;

/// <summary>只改变顶级 HWND 的可见性。WPF 的 Hide 会结束 ShowDialog 的调度帧。</summary>
internal sealed class BossKeyWindows(MainWindow main)
{
    private List<nint>? _hidden;
    private nint _active;
    internal bool IsHidden => _hidden is not null;
    private nint MainHandle => new WindowInteropHelper(main).Handle;

    internal void Hide()
    {
        if (IsHidden) return;
        // 关闭瞬时弹层的逻辑状态，以免托盘恢复时出现已失去锚点的菜单或提示。
        foreach (var source in PresentationSource.CurrentSources.OfType<HwndSource>().ToArray())
        {
            if (source.RootVisual is not FrameworkElement { Parent: Popup popup }) continue;
            var anchor = popup.PlacementTarget is { } placement ? Window.GetWindow(placement) : null;
            if (!BelongsToMain(source.Handle) && (anchor is null || !BelongsToMain(new WindowInteropHelper(anchor).Handle))) continue;
            if (popup.Child is ContextMenu menu) menu.IsOpen = false;
            else if (popup.Child is ToolTip tip) tip.IsOpen = false;
            else popup.SetCurrentValue(Popup.IsOpenProperty, false);
        }
        _hidden = VisibleWindows();
        _active = GetActiveWindow();
        if (!_hidden.Contains(_active)) _active = GetLastActivePopup(MainHandle);
        if (!_hidden.Contains(_active) || !IsWindowEnabled(_active))
            _active = _hidden.FirstOrDefault(IsWindowEnabled);
        var foreground = GetForegroundWindow();
        foreach (var hwnd in _hidden)
            if (hwnd != foreground) SetVisible(hwnd, false);
        // 如果自身正在前台，最后才隐藏输入窗口，让 Windows 把输入交给下一扇可见窗口。
        if (_hidden.Contains(foreground)) ShowWindow(foreground, 0);
    }

    internal void Restore()
    {
        if (_hidden is not { } hidden) return;
        _hidden = null;
        // 所有者先恢复，各窗口保持自己的大小、位置、Z 序和启用状态。
        foreach (var hwnd in hidden.AsEnumerable().Reverse())
            if (BelongsToMain(hwnd) && HwndSource.FromHwnd(hwnd)?.RootVisual is not Window { Visibility: not Visibility.Visible })
                SetVisible(hwnd, true);
        if (main.WindowState == WindowState.Minimized) ShowWindow(MainHandle, 9); // SW_RESTORE
        var windows = VisibleWindows();
        var target = windows.Contains(_active) && IsWindowEnabled(_active) && !IsIconic(_active)
            ? _active : windows.FirstOrDefault(w => IsWindowEnabled(w) && !IsIconic(w));
        if (target != 0) SetForegroundWindow(target);
    }

    internal bool CloseForExit()
    {
        // 原生文件选择框也可能处于模态循环中。先让它走自己的取消路径。
        foreach (var hwnd in _hidden?.ToArray() ?? [])
            if (BelongsToMain(hwnd) && IsWindowEnabled(hwnd) && HwndSource.FromHwnd(hwnd) is null)
                SendMessage(hwnd, 0x0010, 0, 0); // WM_CLOSE
        return CloseOwned(main);
    }

    private static bool CloseOwned(Window window)
    {
        foreach (var child in window.OwnedWindows.Cast<Window>().ToArray())
            if (!CloseOwned(child)) return false;
        window.Close();
        return new WindowInteropHelper(window).Handle == 0;
    }

    private List<nint> VisibleWindows()
    {
        var result = new List<nint>();
        EnumThreadWindows(GetWindowThreadProcessId(MainHandle, out _), (hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && BelongsToMain(hwnd)) result.Add(hwnd);
            return true;
        }, 0);
        return result;
    }

    private bool BelongsToMain(nint hwnd)
    {
        for (var owner = hwnd; owner != 0; owner = GetWindow(owner, 4)) // GW_OWNER
            if (owner == MainHandle) return true;
        return false;
    }

    private static void SetVisible(nint hwnd, bool show) =>
        SetWindowPos(hwnd, 0, 0, 0, 0, 0, 0x217u | (show ? 0x40u : 0x80u)); // 不移动、缩放、重排或激活

    private delegate bool EnumWindow(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetLastActivePopup(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
}

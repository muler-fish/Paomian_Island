using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using Tide.Core;

namespace Tide.Desktop;

internal sealed class BossKeyTray(nint handle, Action restore, Action exit) : IDisposable
{
    private const int Callback = 0x8001;
    private readonly int _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private Icon? _icon;
    private bool _added;

    internal bool Show()
    {
        if (_added) return true;
        if (_icon is null)
        {
            using var stream = Application.GetResourceStream(new Uri("/Tide;component/Assets/paomian.ico", UriKind.Relative)).Stream;
            _icon = new Icon(stream, GetSystemMetrics(49), GetSystemMetrics(50)); // SM_CXSMICON / SM_CYSMICON
        }
        var data = Data();
        data.Flags = 1 | 2 | 4 | 0x80; // NIF_MESSAGE, ICON, TIP, SHOWTIP
        data.Callback = Callback; data.Icon = _icon.Handle; data.Tip = AppIdentity.DisplayName;
        if (!Shell_NotifyIcon(0, ref data)) return false;
        _added = true;
        data.Version = 4;
        Shell_NotifyIcon(4, ref data);
        return true;
    }

    internal bool HandleMessage(int message, nint wParam, nint lParam)
    {
        if (!_added) return false;
        if (message == _taskbarCreated)
        {
            _added = false;
            if (!Show()) restore();
            return true;
        }
        if (message != Callback) return false;
        switch ((int)lParam & 0xffff)
        {
            case 0x0203: // WM_LBUTTONDBLCLK
            case 0x0401: // NIN_KEYSELECT
                restore(); break;
            case 0x007b: // WM_CONTEXTMENU
                var point = new NativePoint { X = (short)(int)wParam, Y = (short)((int)wParam >> 16) };
                if (point.X == -1 && point.Y == -1) GetCursorPos(out point);
                ShowMenu(point); break;
        }
        return true;
    }

    private void ShowMenu(NativePoint point)
    {
        var menu = CreatePopupMenu();
        if (menu == 0) return;
        uint command;
        try
        {
            AppendMenu(menu, 0, 1, "显示泡面岛");
            AppendMenu(menu, 0, 2, "退出泡面岛");
            SetForegroundWindow(handle);
            command = TrackPopupMenuEx(menu, 0x102, point.X, point.Y, handle, 0); // TPM_RETURNCMD | TPM_RIGHTBUTTON
            PostMessage(handle, 0, 0, 0);
        }
        finally { DestroyMenu(menu); }
        if (command == 1) restore();
        else if (command == 2) exit();
        else { var data = Data(); Shell_NotifyIcon(3, ref data); }
    }

    private NotifyIconData Data() => new() { Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1 };
    internal void Remove()
    {
        if (!_added) return;
        var data = Data();
        Shell_NotifyIcon(2, ref data); // NIM_DELETE
        _added = false;
    }

    public void Dispose() { Remove(); _icon?.Dispose(); }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public nint Window; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string? Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string? InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint command, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint item, string label);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hwnd, nint parameters);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint hwnd, uint message, nint wParam, nint lParam);
}

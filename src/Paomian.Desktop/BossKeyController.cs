using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Paomian.Desktop;

/// <summary>用独立 HWND 接收系统热键和托盘消息。模态窗口禁用主窗时这条消息通道仍然可用。</summary>
internal sealed class BossKeyController : IDisposable
{
    private const int HotKeyMessage = 0x0312;
    private readonly MainWindow _main;
    private readonly BossKeyWindows _windows;
    private HwndSource? _source;
    private BossKeyTray? _tray;
    private ShortcutKey? _key;
    private int _id;
    private TaskCompletionSource<bool>? _restored;

    internal BossKeyController(MainWindow main)
    {
        _main = main;
        _windows = new(main);
        main.SourceInitialized += (_, _) =>
        {
            main.Model.Shortcuts.RegisterBossKey = Register;
            if (main.Model.Shortcuts.StartBossKey() is { } error) main.Model.SetError(error, FeedbackArea.General);
        };
    }

    internal bool IsHidden => _windows.IsHidden;
    internal Task<bool>? PendingRestore => _restored?.Task;

    private string? Register(ShortcutKey? key)
    {
        if (key is null)
        {
            if (IsHidden) Restore();
            ReleaseRegistration();
            return null;
        }
        if (_key == key) return null;
        _source ??= new HwndSource(new HwndSourceParameters("PaomianIsland.BossKey")
        {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x80, // WS_POPUP | WS_EX_TOOLWINDOW
            Width = 0, Height = 0, HwndSourceHook = OnMessage
        });
        // 使用另一个 ID 先取得新组合键。注册失败时旧 ID 和已保存的配置继续有效。
        var next = _id == 1 ? 2 : 1;
        if (!RegisterHotKey(_source.Handle, next, (uint)key.Value.Modifiers | 0x4000, (uint)KeyInterop.VirtualKeyFromKey(key.Value.Key)))
            return Marshal.GetLastWin32Error() == 1409 ? $"{key} 已被占用，请修改老板键。"
                : $"Windows 无法注册 {key}，请修改老板键。";
        if (_key.HasValue) UnregisterHotKey(_source.Handle, _id);
        _key = key;
        _id = next;
        return null;
    }

    private nint OnMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == HotKeyMessage && _key is { } key && wParam == _id &&
            ((int)lParam & 0xf) == (int)key.Modifiers && ((int)lParam >> 16) == KeyInterop.VirtualKeyFromKey(key.Key))
        {
            handled = true;
            if (IsHidden) Restore();
            else
            {
                _tray ??= new(hwnd, Restore, Exit);
                if (_tray.Show())
                {
                    _restored = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _windows.Hide();
                }
                else _main.Model.SetError("无法显示系统托盘图标，请稍后再试。", FeedbackArea.General);
            }
        }
        else if (_tray is not null) handled = _tray.HandleMessage(message, wParam, lParam);
        return 0;
    }

    private void Restore()
    {
        _windows.Restore();
        _tray?.Remove();
        _restored?.TrySetResult(true);
        _restored = null;
    }

    private void Exit()
    {
        // 显式退出才关闭子窗，让编辑器先保存草稿，也尊重正在发送时的取消关闭。
        if (!_windows.CloseForExit()) Restore();
    }

    private void ReleaseRegistration()
    {
        if (_key.HasValue && _source is not null) UnregisterHotKey(_source.Handle, _id);
        _key = null;
        _tray?.Dispose(); _tray = null;
        _source?.Dispose(); _source = null;
    }

    public void Dispose()
    {
        _main.Model.Shortcuts.RegisterBossKey = null;
        _restored?.TrySetResult(false);
        _restored = null;
        ReleaseRegistration();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}

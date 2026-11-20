using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

// 仅显式 --real-input：窗口、光标和系统剪贴板交互。所有发言仍经模拟传输。
internal static partial class ComposerImageChecks
{
    private static async Task RunRealAsync(ComposerWindow composer, string path, string output, Action<Window, string> capture, Func<DataObject, Task> copy, BitmapSource bitmap)
    {
        using var desktop = new FocusDesktopInput(); var handle = new WindowInteropHelper(composer).Handle;
        await FocusDesktopInput.ActivateAsync(handle);
        var card = All<ComposerImageCard>(composer).Single(); var editor = All<EmojiEditor>(composer).Single(); editor.Text = "实机附图";
        await OleDragAsync(composer, editor, path, cancel: true); Require(!card.IsVisible && !All<ImageDropSurface>(composer).Single().IsVisible, "取消系统拖放未还原");
        await OleDragAsync(composer, editor, path, cancel: false); await Until(() => card.Photo.Source is not null, "系统拖入失败");
        App.ApplyTheme("dark");
        await FocusDesktopInput.ActivateAsync(handle); editor.Focus(); card.BringIntoView(); await Task.Delay(200); FocusDesktopInput.Hover(card.Tile, handle);
        await Until(() => card.Shade.Opacity > .98 && card.Remove.IsHitTestVisible, "真实悬停未显示遮罩");
        var outside = new Point(card.Tile.ActualWidth + 4, -4);
        FocusDesktopInput.Hover(card.Tile, handle, outside); await Until(() => card.Remove.IsMouseOver, "框外半圆未命中");
        await Task.Delay(140); Require(card.Remove.Opacity > .98 && card.Shade.Opacity > .98, "从缩略图移到框外的 × 时遮罩消失");
        capture(composer, System.IO.Path.Combine(output, "card-hover.png"));
        FocusDesktopInput.Hover(editor, handle); await Until(() => card.Shade.Opacity < .02 && card.Remove.Opacity < .02, "移出后遮罩未关闭");
        FocusDesktopInput.Hover(card.Tile, handle); await Until(() => card.Remove.Opacity > .98, "无法再次悬停"); FocusDesktopInput.ClickAt(card.Tile, outside, handle);
        await Until(() => !card.IsVisible, "鼠标删除失败");
        editor.Focus(); var data = new DataObject(); data.SetImage(bitmap); await copy(data);
        FocusDesktopInput.Chord(handle, 0x11, 0x56); await Until(() => card.Photo.Source is not null, "真实 Ctrl+V 没有添加位图");
        FindButton(composer, "骰子").Focus(); FocusDesktopInput.Key(handle, 0x09);
        await Until(() => card.Remove.IsKeyboardFocused && card.Remove.Opacity > .98, "Tab 未显示或聚焦移除按钮");
        FocusDesktopInput.Key(handle, 0x20); await Until(() => !card.IsVisible, "空格没有删除附图");
        await InModal<ImageDropWindow>(composer, "添加图片", async picker =>
        {
            var popup = new WindowInteropHelper(picker).Handle;
            await FocusDesktopInput.ActivateAsync(popup);
            await OleDragAsync(picker, All<ImageDropSurface>(picker).Single(), path, cancel: false);
        });
        await Until(() => card.Photo.Source is not null, "拖入选图框后未生成缩略图");
        Click(card.Remove);
        await InModal<ImageDropWindow>(composer, "添加图片", async picker =>
        {
            await NativePickerAsync(picker, null);
            Require(picker.IsVisible && !card.IsVisible, "取消选图改变附件");
            var popup = new WindowInteropHelper(picker).Handle;
            await FocusDesktopInput.ActivateAsync(popup);
            FocusDesktopInput.Key(popup, 0x1B);
            await Until(() => !picker.IsVisible, "Esc 未关闭选图框");
        });
        Require(composer.IsVisible && !card.IsVisible && editor.Text == "实机附图", "取消选图框影响草稿");
        await InModal<ImageDropWindow>(composer, "添加图片", async picker =>
        {
            await NativePickerAsync(picker, path); await Until(() => !picker.IsVisible, "原生选文件没有返回发言窗");
        });
        await Until(() => card.Photo.Source is not null, "原生选图没有缩略图");
        Require(editor.Text == "实机附图", "原生选图影响正文");
    }

    // 使用真正的 OLE DoDragDrop 和 CF_HDROP，源提供和 Explorer 相同的格式；只在测试窗口内部移动。
    private static async Task OleDragAsync(Window window, FrameworkElement target, string path, bool cancel)
    {
        var hwnd = new WindowInteropHelper(window).Handle; target.BringIntoView(); await Task.Delay(100);
        FocusDesktopInput.Hover(target, hwnd);
        var seen = false; var stop = false; Exception? failed = null; var ticks = 0;
        DragEventHandler observe = (_, _) => seen = true;
        QueryContinueDragEventHandler keep = (_, e) => { e.Action = stop ? (cancel || failed is not null ? DragAction.Cancel : DragAction.Drop) : DragAction.Continue; e.Handled = true; };
        window.AddHandler(DragDrop.PreviewDragOverEvent, observe, true); window.QueryContinueDrag += keep;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
        timer.Tick += (_, _) =>
        {
            try
            {
                if (++ticks > 35) throw new TimeoutException("Windows OLE 拖放超时");
                stop = seen;
                var p = target.PointToScreen(new Point(target.ActualWidth / 2 + ticks % 3, target.ActualHeight / 2));
                Require(GetForegroundWindow() == hwnd && SetCursorPos((int)p.X, (int)p.Y), "输入桌面或测试窗口失去焦点");
            }
            catch (Exception ex) { failed = ex; stop = true; if (GetForegroundWindow() == hwnd) FocusDesktopInput.Key(hwnd, 0x1B); }
        };
        try
        {
            timer.Start(); var effect = DragDrop.DoDragDrop(window, Files(path), DragDropEffects.Copy); if (failed is not null) throw failed;
            Require(seen && (cancel ? effect == DragDropEffects.None : effect == DragDropEffects.Copy), "OLE 拖放未经过真实拖放目标");
        }
        finally { timer.Stop(); window.RemoveHandler(DragDrop.PreviewDragOverEvent, observe); window.QueryContinueDrag -= keep; }
        await Task.Delay(120);
    }

    private static async Task NativePickerAsync(Window owner, string? file)
    {
        var parent = new WindowInteropHelper(owner).Handle;
        var action = Task.Run(async () =>
        {
            nint hwnd = 0;
            await Until(() =>
            {
                hwnd = FindWindow("#32770", "选择本地图片"); GetWindowThreadProcessId(hwnd, out var pid);
                return hwnd != 0 && pid == Environment.ProcessId && GetWindow(hwnd, 4) == parent && IsWindowVisible(hwnd);
            }, "原生文件选择器未出现或 Owner 错误");
            try
            {
                if (file is null) { PostMessage(hwnd, 0x10, 0, 0); return; }
                await FocusDesktopInput.ActivateAsync(hwnd); await Task.Delay(250);
                FocusDesktopInput.Chord(hwnd, 0x12, 0x4E); // 系统「文件名(N)」助记键，不依赖 UIA 树。
                FocusDesktopInput.Chord(hwnd, 0x11, 0x41); FocusDesktopInput.TypeUnicode(hwnd, file);
                await Task.Delay(150);
                FocusDesktopInput.Key(hwnd, 0x0D);
                await Until(() => !IsWindow(hwnd), "原生文件选择器没有完成确认");
            }
            finally { if (IsWindow(hwnd)) PostMessage(hwnd, 0x10, 0, 0); }
        });
        Click(FindButton(owner, "选择本地图片")); await action;
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string cls, string title);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint w, uint cmd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint w, out uint pid);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint w, uint msg, nint wp, nint lp);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint w);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint w);
}

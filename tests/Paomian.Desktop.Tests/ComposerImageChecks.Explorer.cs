using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static partial class ComposerImageChecks
{
    // 不借用用户现有窗口：只调整/关闭为本轮随机命名的单文件目录新建的 HWND。
    private static async Task<string> ExplorerDropAsync(ComposerWindow composer, string root, byte[] bytes, Action clipboardChanged)
    {
        using var input = new FocusDesktopInput();
        var folder = Directory.CreateDirectory(Path.Combine(root, "Paomian-drag-" + Guid.NewGuid().ToString("N")));
        const string name = "拖入测试"; var path = Path.Combine(folder.FullName, name + ".png"); File.WriteAllBytes(path, bytes);
        var work = WindowPlacement.WorkArea(composer); var dpi = VisualTreeHelper.GetDpi(composer).DpiScaleX;
        composer.Width = Math.Max(composer.MinWidth, work.Width * .46); composer.Left = work.Right - composer.Width; composer.Top = work.Top;
        var editor = All<EmojiEditor>(composer).Single(); editor.BringIntoView(); await Task.Delay(120);
        var cabinets = new PropertyCondition(AutomationElement.ClassNameProperty, "CabinetWClass");
        static nint Handle(AutomationElement el) => el.Current.NativeWindowHandle;
        var prior = await Task.Run(() => AutomationElement.RootElement.FindAll(TreeScope.Children, cabinets).Cast<AutomationElement>().Select(Handle).ToHashSet());
        using var launch = Process.Start(new ProcessStartInfo("explorer.exe", $"/n,/select,\"{path}\"") { UseShellExecute = false });
        nint shell = 0;
        var topmost = composer.Topmost;
        try
        {
            composer.Topmost = true;
            await Task.Run(async () => await Until(() =>
            {
                var window = AutomationElement.RootElement.FindAll(TreeScope.Children, cabinets).Cast<AutomationElement>().FirstOrDefault(el =>
                    !prior.Contains(Handle(el)) && el.Current.Name.Contains(folder.Name, StringComparison.OrdinalIgnoreCase));
                if (window is null) return false;
                GetWindowThreadProcessId(Handle(window), out var pid); using var process = Process.GetProcessById((int)pid);
                if (!process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return false;
                shell = Handle(window); return true;
            }, "未找到本轮新建的资源管理器窗口；不会操作已有窗口"));
            ShowWindow(shell, 9); Require(SetWindowPos(shell, -1, (int)(work.Left * dpi), (int)(work.Top * dpi), (int)(work.Width * .48 * dpi), (int)(work.Height * .8 * dpi), 0x0040), "无法并排放置窗口");
            await Task.Delay(400);
            var ownerHandle = new WindowInteropHelper(composer).Handle;
            var point = await Task.Run(async () =>
            {
                var point = new Point();
                await Until(() =>
                {
                    var item = AutomationElement.FromHandle(shell).FindFirst(TreeScope.Descendants, new AndCondition(
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem), new OrCondition(
                            new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.NameProperty, name + ".png"))));
                    if (item is null || item.Current.BoundingRectangle is not { IsEmpty: false, Width: > 0, Height: > 0 } bounds) return false;
                    // 详细信息视图拖动行首图标，图标视图拖动图块中心。点击前另用 HWND 验证命中。
                    var x = bounds.Width > bounds.Height * 3 ? Math.Min(bounds.Height * .8, 25) : bounds.Width / 2;
                    point = new Point(bounds.Left + x, bounds.Top + bounds.Height / 2);
                    return true;
                }, "资源管理器没有显示测试图片"); return point;
            });
            // /select 预选本轮文件。用真实 Ctrl+C 核对选中的绝对路径。
            FocusDesktopInput.ClickExplorerFile(shell, point);
            await Until(() => GetForegroundWindow() == shell, "点击测试文件后资源管理器未激活");
            FocusDesktopInput.Chord(shell, 0x11, 0x43); await Task.Delay(160);
            Require(Clipboard.GetFileDropList().Cast<string>().Contains(path, StringComparer.OrdinalIgnoreCase), "资源管理器 Ctrl+C 没有将实际文件放入剪贴板");
            clipboardChanged();
            var card = All<ComposerImageCard>(composer).Single();
            var trace = new HashSet<string>();
            DragEventHandler observe = (_, e) => trace.Add($"{e.RoutedEvent.Name} {e.OriginalSource?.GetType().Name} {e.AllowedEffects} -> {e.Effects}");
            RoutedEvent[] events = [DragDrop.PreviewDragEnterEvent, DragDrop.PreviewDragOverEvent, DragDrop.PreviewDropEvent];
            foreach (var evt in events) composer.AddHandler(evt, observe, true);
            try
            {
                // 选中点击紧跟第二次按下会被 Explorer 当作双击打开图片。
                await Task.Delay((int)GetDoubleClickTime() + 80);
                await FocusDesktopInput.DragFileFromExplorer(shell, point, editor, ownerHandle);
                await Until(() => All<ComposerImageCard>(composer).Single().Photo.Source is not null, "资源管理器拖放未生成缩略图，WPF 事件：" + string.Join(" / ", trace));
                Require(Equals(card.ToolTip, Path.GetFileName(path)), "资源管理器拖入内容与所选文件不同");
                Console.WriteLine("PASS: Windows 资源管理器拖入正文");
            }
            finally { foreach (var evt in events) composer.RemoveHandler(evt, observe); }
            Click(card.Remove);
            await FocusDesktopInput.ActivateAsync(ownerHandle); editor.Focus(); FocusDesktopInput.Chord(ownerHandle, 0x11, 0x56);
            await Until(() => card.Photo.Source is not null, "资源管理器复制后 Ctrl+V 未添加图片");
            Require(Equals(card.ToolTip, Path.GetFileName(path)), "资源管理器粘贴内容与所选文件不同");
            Console.WriteLine("PASS: Windows 资源管理器 Ctrl+C → 正文 Ctrl+V");
            return path;
        }
        finally { if (shell != 0 && IsWindow(shell)) PostMessage(shell, 0x10, 0, 0); composer.Topmost = topmost; }
    }
    [DllImport("user32.dll")] private static extern uint GetDoubleClickTime();
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
}

using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Paomian.Desktop;
using Wpf.Ui.Controls;

namespace Paomian.Desktop.Tests;

/// <summary>检查自定义标题栏真实命中区域及窗口操作，防止看得见却不能点击或拖动。</summary>
internal static class FluentWindowChecks
{
    public static async Task RunAsync(MainWindow window, string output, Action<Window, string> capture)
    {
        if (!window.Model.ShowNewThreadAction) await window.Model.SelectForumAsync(new(30, "综合版1", "综合"));
        await Layout(window);
        var title = (TitleBar)window.FindName("WindowTitleBar");
        var jump = (FrameworkElement)window.FindName("JumpBox");
        var publish = (FrameworkElement)window.FindName("NewThreadButton");
        var handle = new WindowInteropHelper(window).Handle;
        var results = new Dictionary<string, object>();
        int Hit(FrameworkElement item) => HitScreen(handle, item.PointToScreen(new Point(item.ActualWidth / 2, item.ActualHeight / 2)));
        foreach (var (name, expected) in new[] { ("PART_MinimizeButton", 8), ("PART_MaximizeButton", 9), ("PART_CloseButton", 20) })
        {
            var button = (FrameworkElement)title.Template.FindName(name, title);
            var hit = Hit(button); results[name + "Hit"] = hit;
            Require(hit == expected, $"标题栏按钮 {name} 命中类型错误：{hit}。");
        }
        results["jumpHit"] = Hit(jump); results["newThreadHit"] = Hit(publish);
        Require((int)results["jumpHit"] == 1 && (int)results["newThreadHit"] == 1, "跳串输入/悬浮发串被误识别为拖动区域。");
        var drag = title.PointToScreen(new Point(230, 24));
        results["dragHit"] = HitScreen(handle, drag);
        Require((int)results["dragHit"] == 2, "标题栏空白处不能拖动窗口。");

        // 通过控件的自动化 Invoke 模式调用库原本的命令，不直接设置 WindowState 代替按钮测试。
        Invoke(title, "PART_MaximizeButton"); await Layout(window);
        Require(window.WindowState == WindowState.Maximized, "最大化按钮没有生效。");
        capture(window, Path.Combine(output, "fluent-maximized.png"));
        Invoke(title, "PART_MaximizeButton"); await Layout(window);
        Require(window.WindowState == WindowState.Normal, "还原按钮没有生效。");
        Invoke(title, "PART_MinimizeButton"); await Layout(window);
        Require(window.WindowState == WindowState.Minimized, "最小化按钮没有生效。");
        window.WindowState = WindowState.Normal; await Layout(window);
        results["maximizeRestoreMinimize"] = true;
        SendMessage(handle, 0x00A3, (IntPtr)2, Pack(title.PointToScreen(new Point(230, 24))));
        await Layout(window);
        Require(window.WindowState == WindowState.Maximized, "标题栏双击没有最大化。");
        SendMessage(handle, 0x00A3, (IntPtr)2, Pack(title.PointToScreen(new Point(230, 24))));
        await Layout(window);
        Require(window.WindowState == WindowState.Normal, "标题栏双击没有还原。");
        results["doubleClickMaximizeRestore"] = true;
        var closingRaised = false;
        System.ComponentModel.CancelEventHandler cancelClose = (_, e) => { closingRaised = true; e.Cancel = true; };
        window.Closing += cancelClose;
        try { Invoke(title, "PART_CloseButton"); await Layout(window); }
        finally { window.Closing -= cancelClose; }
        Require(closingRaised && window.IsVisible, "关闭按钮没有进入可取消的关闭流程。");
        results["closeCommand"] = true;
        foreach (var width in new[] { 900d, 1060d, 1280d })
        {
            window.Width = width; await Layout(window);
            var close = (FrameworkElement)title.Template.FindName("PART_CloseButton", title);
            foreach (var element in new[] { jump, close })
            {
                var bounds = element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));
                Require(bounds.Left >= 0 && bounds.Right <= window.ActualWidth && bounds.Bottom <= 55, "窄窗口的顶部控件被裁切。");
            }
            Require(Hit(jump) == 1 && Hit(publish) == 1, "窄窗口顶部控件不能点击。");
        }
        results["toolbarWidths"] = new[] { 900, 1060, 1280 };
        capture(window, Path.Combine(output, "fluent-toolbar.png"));
        File.WriteAllText(Path.Combine(output, "fluent-window-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: Fluent caption hit tests, toolbar, maximize/restore/minimize, double click, three widths.");
    }
    private static void Invoke(TitleBar title, string name)
    {
        var button = (System.Windows.Controls.Button)title.Template.FindName(name, title);
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static int HitScreen(IntPtr handle, Point point) => (int)SendMessage(handle, 0x0084, IntPtr.Zero, Pack(point));
    private static IntPtr Pack(Point point) => (IntPtr)(((int)point.X & 0xffff) | (((int)point.Y & 0xffff) << 16));
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(120); }
    private static void Require(bool value, string error) { if (!value) throw new InvalidOperationException(error); }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}

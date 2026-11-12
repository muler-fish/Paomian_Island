using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Tide.Desktop.Tests;

/// <summary>对真实子窗口进行离线检查：统一标题栏必须可用，并保留关闭保存草稿和模态归还焦点。</summary>
internal static class ChildWindowChecks
{
    public static async Task RunAsync(MainWindow owner, MainViewModel vm, string output, Action<Window, string> capture)
    {
        if (vm.State.Forums.Count == 0) vm.State.Forums.Add(new(4, "综合版1", "综合"));
        var cases = new (string Name, Func<Window> Create)[]
        {
            ("new-thread", () => new ComposerWindow(vm, 0)),
            ("reply", () => new ComposerWindow(vm, 123)),
            ("cookies", () => new CookieWindow(vm)),
            ("settings", () => new SettingsWindow(vm)),
            ("drafts", () => new DraftWindow(vm, (_, _) => { })),
            ("reference", () => new TextWindow("引用 · No.101", "引用窗口标题栏验证。")),
            ("send-preview", () => new TextWindow("发送预览", "仅验证窗口，不发送发言。")),
            // 缺失图片地址进入现有错误提示路径；标题栏测试不下载图片、不依赖公网。
            ("image", () => new ImageWindow(new Post { Id = 101 }))
        };
        var results = new Dictionary<string, object>();
        var failures = new List<string>();
        foreach (var (name, create) in cases)
        {
            App.ApplyTheme("light");
            var window = create(); window.Owner = owner; window.ShowInTaskbar = false;
            try
            {
                window.Show(); await Layout(window);
                capture(window, Path.Combine(output, name + "-light.png"));
                var title = UiRefinementChecks.Descendants<TitleBar>(window).SingleOrDefault();
                Require(window is FluentWindow { ExtendsContentIntoTitleBar: true }, name + " 仍使用系统标题栏，未使用统一的 Fluent 外壳。");
                // Refined Reader 弹窗只有自定义的 32×32 关闭键；看图窗口保留最小化、最大化与关闭。
                if (title is null)
                {
                    await CheckDialogCaption(window, name, vm, owner, output, capture);
                    results[name] = new { CustomDialogCaption = true, Themes = new[] { "light", "dark" }, MinimumWidth = window.MinWidth, ClosingGuard = true };
                    continue;
                }
                foreach (var theme in new[] { "light", "dark" })
                {
                    App.ApplyTheme(theme); await Layout(window);
                    CheckCaption(window, title!);
                    // 看图窗口使用局部深色画布；标题栏应遵循所属窗口的颜色，而非全局阅读主题。
                    var expected = ((SolidColorBrush)window.FindResource("Text")).Color;
                    Require(((SolidColorBrush)title!.ButtonsForeground).Color == expected, name + " 的窗口按钮没有跟随主题。");
                    capture(window, Path.Combine(output, name + "-" + theme + ".png"));
                }
                window.Width = window.MinWidth; await Layout(window);
                CheckCaption(window, title!);
                capture(window, Path.Combine(output, name + "-narrow.png"));
                Invoke(title!, "PART_MaximizeButton"); await Layout(window);
                Require(window.WindowState == WindowState.Maximized, name + " 最大化失效。");
                CheckCaption(window, title!);
                Invoke(title!, "PART_MaximizeButton"); await Layout(window);
                Require(window.WindowState == WindowState.Normal, name + " 还原失效。");
                Invoke(title!, "PART_MinimizeButton"); await Layout(window);
                Require(window.WindowState == WindowState.Minimized, name + " 最小化失效。");
                window.WindowState = WindowState.Normal; await Layout(window);
                // 仍应经过 Window.Closing，不能由自定义按钮直接销毁 HWND 而绕过编辑器保护。
                var cancelObserved = false;
                CancelEventHandler cancel = (_, e) => { cancelObserved = true; e.Cancel = true; };
                window.Closing += cancel;
                try { Invoke(title!, "PART_CloseButton"); await Layout(window); }
                finally { window.Closing -= cancel; }
                Require(cancelObserved && window.IsVisible, name + " 的关闭按钮绕过 Closing。");
                if (name == "new-thread")
                {
                    var content = UiRefinementChecks.Descendants<EmojiEditor>(window).Single(t =>
                        System.Windows.Automation.AutomationProperties.GetName(t) == "发言正文");
                    content.Text = "子窗口标题栏关闭后仍保存草稿。";
                }
                Invoke(title!, "PART_CloseButton"); await Layout(owner);
                Require(!window.IsVisible, name + " 的关闭按钮没有关闭窗口。");
                if (name == "new-thread") Require(vm.Store.Load().Drafts.Any(d => d.Content == "子窗口标题栏关闭后仍保存草稿。"), "点击关闭没有保存草稿。");
                results[name] = new { FluentTitleBar = true, Themes = new[] { "light", "dark" }, MinimumWidth = window.MinWidth, CaptionCommands = true, ClosingGuard = true };
            }
            catch (Exception ex) { failures.Add(ex.Message); }
            finally { if (window.IsVisible) window.Close(); }
        }
        if (failures.Count == 0)
        {
            var ownerHandle = new WindowInteropHelper(owner).Handle;
            var disabledWhileModal = false;
            Exception? modalError = null;
            var modal = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false };
            modal.ContentRendered += (_, _) => modal.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    disabledWhileModal = !IsWindowEnabled(ownerHandle);
                    Click(CloseButton(modal));
                }
                catch (Exception ex) { modalError = ex; modal.Close(); }
            }), DispatcherPriority.ApplicationIdle);
            modal.ShowDialog();
            Require(modalError is null && disabledWhileModal && IsWindowEnabled(ownerHandle), "模态窗口关闭后没有恢复主窗口交互。");
            results["modalOwnerRestored"] = true;
            results["draftSavedOnCaptionClose"] = true;
        }
        App.ApplyTheme("light");
        File.WriteAllText(Path.Combine(output, "child-window-results.json"),
            JsonSerializer.Serialize(new { Results = results, Failures = failures }, new JsonSerializerOptions { WriteIndented = true }));
        Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Console.WriteLine("PASS: eight child windows (custom dialog close key / image window caption), light/dark, minimum widths, Closing guards, draft saving, modal owner restored.");
    }

    private static Button CloseButton(Window window) => UiRefinementChecks.Descendants<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "关闭");
    private static void Click(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();

    private static async Task CheckDialogCaption(Window window, string name, MainViewModel vm, Window owner, string output, Action<Window, string> capture)
    {
        void CheckClose()
        {
            Program.RequireWindowFits(window);
            var close = CloseButton(window);
            var bounds = close.TransformToAncestor(window).TransformBounds(new Rect(close.RenderSize));
            Require(close.IsVisible && close.ActualWidth == 32 && close.ActualHeight == 32 && close.Padding == new Thickness(0) &&
                bounds.Left >= 0 && bounds.Right <= window.ActualWidth + 1 && bounds.Top >= 0,
                $"{name} 关闭键不是 32×32 零内边距或超出窗口：尺寸={close.ActualWidth}×{close.ActualHeight}，范围={bounds}。");
        }
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme); await Layout(window); CheckClose();
            capture(window, Path.Combine(output, name + "-" + theme + ".png"));
        }
        App.ApplyTheme("light");
        window.Width = window.MinWidth; await Layout(window); CheckClose();
        capture(window, Path.Combine(output, name + "-narrow.png"));
        // 仍应经过 Window.Closing，不能绕过编辑器的草稿保护。
        var cancelObserved = false;
        CancelEventHandler cancel = (_, e) => { cancelObserved = true; e.Cancel = true; };
        window.Closing += cancel;
        try { Click(CloseButton(window)); await Layout(window); }
        finally { window.Closing -= cancel; }
        Require(cancelObserved && window.IsVisible, name + " 的关闭键绕过 Closing。");
        if (name == "new-thread")
        {
            var content = UiRefinementChecks.Descendants<EmojiEditor>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "发言正文");
            content.Text = "子窗口标题栏关闭后仍保存草稿。";
        }
        Click(CloseButton(window)); await Layout(owner);
        Require(!window.IsVisible, name + " 的关闭键没有关闭窗口。");
        if (name == "new-thread") Require(vm.Store.Load().Drafts.Any(d => d.Content == "子窗口标题栏关闭后仍保存草稿。"), "点击关闭没有保存草稿。");
    }

    private static void CheckCaption(Window window, TitleBar title)
    {
        Program.RequireWindowFits(window);
        var handle = new WindowInteropHelper(window).Handle;
        foreach (var (name, expected) in new[] { ("PART_MinimizeButton", 8), ("PART_MaximizeButton", 9), ("PART_CloseButton", 20) })
        {
            var button = (FrameworkElement)title.Template.FindName(name, title);
            // 最大化时 WPF 外壳包含不可见缩放边框；按钮先相对标题栏校验，再独立检查屏幕工作区。
            var bounds = button.TransformToAncestor(title).TransformBounds(new Rect(button.RenderSize));
            Require(button.IsVisible && button.ActualWidth >= 32 && button.ActualHeight >= 32 &&
                bounds.Left >= 0 && bounds.Right <= title.ActualWidth + 1 && bounds.Top >= 0 && bounds.Bottom <= title.ActualHeight + 1,
                $"{window.Title} / {window.WindowState} / {name}：可见={button.IsVisible}，尺寸={button.ActualWidth}×{button.ActualHeight}，范围={bounds}，窗口={window.ActualWidth}×{window.ActualHeight}。");
            var hit = (int)SendMessage(handle, 0x0084, IntPtr.Zero, Pack(button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight / 2))));
            Require(hit == expected, "子窗口标题栏按钮命中类型错误。");
        }
        var dragPoint = title.PointToScreen(new Point(Math.Min(220, title.ActualWidth / 2), title.ActualHeight / 2));
        Require((int)SendMessage(handle, 0x0084, IntPtr.Zero, Pack(dragPoint)) == 2, "子窗口标题栏不能拖动。");
    }
    private static void Invoke(TitleBar title, string name) =>
        ((IInvokeProvider)new ButtonAutomationPeer((Button)title.Template.FindName(name, title)).GetPattern(PatternInterface.Invoke)).Invoke();
    private static IntPtr Pack(Point point) => (IntPtr)(((int)point.X & 0xffff) | (((int)point.Y & 0xffff) << 16));
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(80); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr window);
}


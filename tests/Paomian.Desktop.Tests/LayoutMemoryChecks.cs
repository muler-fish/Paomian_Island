using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class LayoutMemoryChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var root = Path.Combine(output, "layout-profile");
        Directory.CreateDirectory(root);
        // 曾在 preview.27 收起导航的资料也应直接显示导航，并保留其他尺寸。
        File.WriteAllText(Path.Combine(root, "state.json"), "{\"WindowLayout\":{\"Width\":1100,\"Height\":680,\"NavigationVisible\":false,\"NavigationWidth\":184,\"ListWidth\":320}}");
        MainWindow Open()
        {
            var vm = new MainViewModel(UiRefinementChecks.CreateApi(), new LocalStore(root));
            var w = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false }; w.Show(); return w;
        }
        var window = Open();
        try
        {
            await Layout(window);
            window.Width = 1100; window.Height = 680; await Layout(window);
            var grid = (Grid)window.FindName("ContentColumns");
            var navigation = (FrameworkElement)window.FindName("NavigationPane");
            var navSplitter = (GridSplitter)window.FindName("NavigationSplitter");
            var listSplitter = (GridSplitter)window.FindName("ListSplitter");
            Require(navigation.IsVisible && navSplitter.IsVisible && window.FindName("NavigationToggle") is null, "旧隐藏状态未恢复显示，或仍保留收起按钮");
            var originalNav = grid.ColumnDefinitions[0].ActualWidth;
            Nudge(navSplitter, Key.Right); await Layout(window);
            Require(grid.ColumnDefinitions[0].ActualWidth > originalNav, "导航分隔条不能通过键盘调整");
            listSplitter.KeyboardIncrement = 240;
            Nudge(listSplitter, Key.Right); await Layout(window);
            Require(grid.ColumnDefinitions[2].ActualWidth > 480, "串列表区仍被旧的 480 DIP 上限限制");
            var expectedNav = grid.ColumnDefinitions[0].ActualWidth; var expectedList = grid.ColumnDefinitions[2].ActualWidth;
            var saved = new LocalStore(root).Load().WindowLayout!;
            Require(Near(saved.NavigationWidth, expectedNav) && Near(saved.ListWidth, expectedList), "键盘调栏宽没有保存");
            capture(window, Path.Combine(output, "navigation-visible.png"));
            window.Close(); window = Open(); await Layout(window);
            grid = (Grid)window.FindName("ContentColumns");
            Require(((FrameworkElement)window.FindName("NavigationPane")).IsVisible && Near(window.Width, 1100) && Near(window.Height, 680), "重启没有恢复窗口尺寸或导航不可见");
            Require(Near(grid.ColumnDefinitions[0].ActualWidth, expectedNav) && Near(grid.ColumnDefinitions[2].ActualWidth, expectedList), "重启丢失栏宽");
            // 保存较宽的分栏，再缩窄窗口：适配不能覆盖用户的宽度偏好。
            window.Model.State.WindowLayout!.NavigationWidth = 260; window.Model.State.WindowLayout.ListWidth = 600;
            window.Width = 1200; await Layout(window); window.Width = 900; await Layout(window);
            Require(grid.ColumnDefinitions[4].ActualWidth >= 279 && grid.ColumnDefinitions.Sum(c => c.ActualWidth) <= grid.ActualWidth + 1, "窄窗口分栏越界或挤没阅读区");
            window.Width = 1200; await Layout(window);
            Require(Near(grid.ColumnDefinitions[0].ActualWidth, 260) && Near(grid.ColumnDefinitions[2].ActualWidth, 600), "临时缩窄覆盖原栏宽");
            App.ApplyTheme("dark"); capture(window, Path.Combine(output, "layout-dark.png")); App.ApplyTheme("light");
            window.WindowState = WindowState.Maximized; await Layout(window);
            window.WindowState = WindowState.Minimized; await Layout(window); window.Close();
            saved = new LocalStore(root).Load().WindowLayout!;
            Require(saved.Maximized && Near(saved.Width, 1200) && Near(saved.Height, 680), "最小化覆盖了正常窗口尺寸或最大化状态");
            window = Open(); await Layout(window);
            Require(window.WindowState == WindowState.Maximized, "重启没有恢复最大化");
            window.WindowState = WindowState.Normal; await Layout(window);
            Require(Near(window.ActualWidth, 1200) && Near(window.ActualHeight, 680), "最大化还原后丢失正常尺寸");
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                var work = new Rect(-1366, 40, 1366, 728);
                var fit = WindowPlacement.Calculate(work, scale, new(8000, 5000), new(900, 600), true);
                Require(work.Contains(new Rect(fit.Bounds.X * scale, fit.Bounds.Y * scale, fit.Bounds.Width * scale, fit.Bounds.Height * scale)), "恢复布局在小屏/DPI变化时越界");
            }
            var exact = WindowPlacement.Calculate(new(0, 0, 1920, 1040), 1, new(1800, 1000), new(900, 600), true);
            Require(exact.Bounds.Width == 1800 && exact.Bounds.Height == 1000, "恢复时再次缩小仍能容纳的用户尺寸");
            File.WriteAllText(Path.Combine(output, "layout-memory-results.json"), JsonSerializer.Serialize(new
            { NavigationAlwaysVisible = true, LegacyHiddenStateIgnored = true, KeyboardSplitters = true, Restart = true, NarrowLayout = true, MaximizeMinimizeRestore = true, DpiSafety = true }));
            Console.WriteLine("PASS: always-visible navigation, legacy hidden state ignored, keyboard splitters, restart dimensions, narrow layout, maximize/minimize and DPI safety.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }
    private static bool Near(double a, double b) => Math.Abs(a - b) <= 1;
    private static void Nudge(GridSplitter splitter, Key key)
    {
        splitter.Focus();
        splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter), 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(splitter), 0, key) { RoutedEvent = Keyboard.KeyUpEvent });
    }
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(180); }
    private static void Require(bool value, string why) { if (!value) throw new InvalidOperationException(why); }
}

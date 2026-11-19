using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

/// <summary>仅验证本轮气泡的运动、计时和调用起点，不重跑屏蔽、订阅或网络套件。</summary>
internal static class FeedbackMotionChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var vm = owner.Model; vm.State.ShowImages = false; vm.AppearanceChanged();
        await vm.OpenThreadAsync(101); owner.UpdateLayout();
        var overlay = (FeedbackOverlay)owner.FindName("FeedbackLayer");
        var copy = (Button)owner.FindName("CopyThreadLinkButton");
        var clipboard = Clipboard.GetDataObject();
        try
        {
            copy.Focus(); var origin = overlay.CaptureOrigin(copy)!.Value;
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var bubble = overlay.Children.OfType<Border>().Single();
            Require(!UiRefinementChecks.Descendants<Button>(bubble).Any(), "成功气泡仍有关闭按钮");
            if (SystemParameters.ClientAreaAnimation) Require(Near(Center(bubble), origin), "复制气泡没有从按钮处起步");
            Require(copy.IsKeyboardFocused, "动画抢走按钮焦点");
            await Task.Delay(310); capture(owner, Path.Combine(output, "copy-bubble-preview34.png"));
            await Task.Delay(700); Require(overlay.Count == 0, "成功气泡未及时消失");

            // 快捷收藏行移除后，动画仍从消失前的星标位置起步，覆盖鼠标/键盘/UIA 共用入口。
            var forum = new Forum(4, "动画验证板块", "综合");
            vm.State.FavoriteForums.Add(forum); vm.FavoriteForums.Add(forum); owner.UpdateLayout();
            var star = UiRefinementChecks.Descendants<ForumFavoriteToggleButton>(owner).First(b => b.Tag is Forum f && f.Id == 4);
            origin = overlay.CaptureOrigin(star)!.Value;
            ((IToggleProvider)new ToggleButtonAutomationPeer(star).GetPattern(PatternInterface.Toggle)).Toggle();
            Require(overlay.Count == 1 && !vm.FavoriteForums.Any(f => f.Id == 4), "取消收藏无动画或未移除入口");
            if (SystemParameters.ClientAreaAnimation) Require(Near(Center(overlay.Children.OfType<Border>().Single()), origin), "按钮消失后丢失动画起点");
            overlay.Clear();
        }
        finally { if (clipboard is not null) Clipboard.SetDataObject(clipboard, true); }

        // 用可控动画策略验证两条分支，不修改用户的 Windows 动画设置。
        var region = new Border { Margin = new(12, 48, 12, 12), Background = Brushes.WhiteSmoke };
        var source = new Button { Content = "动画起点", Width = 100, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new(0, 0, 0, 60) };
        region.Child = source;
        var layer = new FeedbackOverlay(() => true); var reduced = new FeedbackOverlay(() => false);
        var grid = new Grid(); grid.Children.Add(region); grid.Children.Add(layer); grid.Children.Add(reduced);
        var host = new Window { Title = "气泡专项", Owner = owner, ShowInTaskbar = false, Width = 650, Height = 480, Content = grid };
        host.Show(); host.UpdateLayout();
        try
        {
            var origin = layer.CaptureOrigin(source)!.Value;
            layer.Show(new("成功", FeedbackArea.List), region, source);
            var bubble = layer.Children.OfType<Border>().Single(); var shift = (TranslateTransform)bubble.RenderTransform;
            var start = shift.Y;
            Require(start > 0 && Near(Center(bubble), origin), "成功动画方向或起点错误");
            await Task.Delay(100); Require(shift.Y < start && shift.Y >= 0, "成功气泡未向上移动");
            await Task.Delay(220); Require(Math.Abs(shift.Y) < 0.1 && bubble.Opacity > 0.99 && layer.Count == 1, "成功气泡未到位");
            // 悬停也必须在新规定时间内收起。
            bubble.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
            await Task.Delay(230); Require(layer.Count == 1, "成功气泡到位后未停留约 0.5 秒");
            await Task.Delay(420); Require(layer.Count == 0, "成功气泡因悬停而延长");

            layer.Show(new("旧提示", FeedbackArea.List), region, source);
            await Task.Delay(400); layer.Show(new("新提示", FeedbackArea.List), region, source);
            await Task.Delay(500); Require(layer.Count == 1 && Text(layer).Contains("新提示"), "旧动画结束误删了新提示"); layer.Clear();

            layer.Show(new("列表错误", FeedbackArea.List, true), region, source);
            bubble = layer.Children.OfType<Border>().Single(); shift = (TranslateTransform)bubble.RenderTransform;
            Require(shift.Y < 0 && !UiRefinementChecks.Descendants<Button>(bubble).Any(), "错误气泡未从上方进入或仍有关闭按钮");
            await Task.Delay(340); Require(Math.Abs(shift.Y) < 0.1 && bubble.Opacity > 0.99, "错误气泡未滑入到位");
            var rootPosition = region.TranslatePoint(new Point(), layer);
            Require(Math.Abs(Canvas.GetTop(bubble) - rootPosition.Y - 12) < 1, "错误被错误地定位到操作按钮");
            App.ApplyTheme("dark"); capture(host, Path.Combine(output, "error-bubble-preview34.png")); App.ApplyTheme("light");
            bubble.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
            await Task.Delay(4100); Require(layer.Count == 1, "错误气泡不到五秒就消失");
            await Task.Delay(1200); Require(layer.Count == 0, "错误气泡超过五秒仍未收起");

            reduced.Show(new("关闭系统动画", FeedbackArea.List), region, source);
            var still = reduced.Children.OfType<Border>().Single();
            Require(((TranslateTransform)still.RenderTransform).Y == 0 && still.Opacity == 1, "关闭系统动画后仍运动");
            await Task.Delay(650); Require(reduced.Count == 0, "无动画模式未使用新停留时长");
            layer.Show(new("关闭窗口取消动画", FeedbackArea.List, true), region); host.Close();
            await host.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            Require(layer.Count == 0 && reduced.Count == 0, "窗口关闭后遗留动画");
            File.WriteAllText(Path.Combine(output, "feedback-motion-results.json"), JsonSerializer.Serialize(new { Passed = true, SuccessHoldMs = 500, ErrorHoldMs = 5000, CloseButtons = 0, SystemAnimations = SystemParameters.ClientAreaAnimation }));
        }
        finally { if (host.IsVisible) host.Close(); overlay.Clear(); App.ApplyTheme("light"); }
    }
    private static Point Center(Border bubble)
    {
        var shift = (TranslateTransform)bubble.RenderTransform;
        return new(Canvas.GetLeft(bubble) + shift.X + bubble.DesiredSize.Width / 2, Canvas.GetTop(bubble) + shift.Y + bubble.DesiredSize.Height / 2);
    }
    private static bool Near(Point a, Point b) => (a - b).Length < 3;
    private static IEnumerable<string> Text(DependencyObject root) => UiRefinementChecks.Descendants<TextBlock>(root).Select(t => t.Text);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

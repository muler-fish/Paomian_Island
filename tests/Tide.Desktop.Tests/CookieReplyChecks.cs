using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class CookieReplyChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture, bool selectorOnly = false)
    {
        var model = owner.Model;
        var first = new CookieIdentity("usage-a", "饼干原名A", SecretProtection.Protect("synthetic-reply-cookie-a"));
        var second = new CookieIdentity("usage-b", "饼干原名B", SecretProtection.Protect("synthetic-reply-cookie-b"));
        model.State.Cookies = [first, second]; model.State.ActiveCookieId = first.Id; model.Save();
        const long thread = 87654321;
        var time = DateTimeOffset.Now.AddMinutes(-10);
        void Record(string id, string cookie, long target, int minute) => model.Store.AddSentPost(
            new(id, true, target, 4, "综合版1", "原名", "", "", "回应正文", false, time.AddMinutes(minute)) { CookieId = cookie });
        Record("reply-a1", first.Id, thread, 1); Record("reply-a2", first.Id, thread, 2);
        Record("reply-b1", second.Id, thread, 3); Record("other-thread", first.Id, thread + 1, 9);

        if (!selectorOnly)
        {
        var manager = new CookieWindow(model) { Owner = owner, ShowInTaskbar = false };
        manager.Show(); await Pause();
        try
        {
            var list = All<ListBox>(manager).Single(); list.SelectedIndex = 0;
            All<TextBox>(manager).Single(t => AutomationProperties.GetName(t) == "饼干备注").Text = "常用 · 工作日";
            Click(manager, "保存备注"); await Pause();
            Require(model.Store.Load().Cookies[0].Remark == "常用 · 工作日" && model.State.Cookies[0].Name == first.Name, "备注修改了原名或没有保存");
            Click(manager, "导入饼干"); await Pause();
            var value = All<PasswordBox>(manager).Single();
            value.Password = JsonSerializer.Serialize(new { cookie = "synthetic-reply-cookie-a", name = "更新后的原名A" });
            Click(manager, "导入并使用"); await Pause();
            Require(model.State.Cookies.Count == 2 && model.State.Cookies[0].Id == first.Id && model.State.Cookies[0].Remark == "常用 · 工作日", "重新导入丢失身份或备注");
            list.SelectedIndex = 1; Click(manager, "设置为默认饼干"); await Pause();
            Require(model.Store.Load().ActiveCookieId == second.Id, "默认饼干没有保存");
            capture(manager, Path.Combine(output, "cookies-remarks-light.png"));
            App.ApplyTheme("dark"); await Pause(); capture(manager, Path.Combine(output, "cookies-remarks-dark.png"));
        }
        finally { manager.Close(); App.ApplyTheme("light"); }
        }
        else
        {
            model.State.Cookies[0] = first with { Remark = "常用 · 工作日", Name = "更新后的原名A" };
            model.State.ActiveCookieId = second.Id;
        }
        // A longer list and a long remark exercise scrolling/trimming without touching personal data.
        model.State.Cookies.AddRange(Enumerable.Range(0, 20).Select(i => new CookieIdentity("extra-" + i, "备用饼干" + i, "")
            { Remark = i == 0 ? "一个很长很长用于检查窄窗口显示的本机饼干备注名称" : "" }));
        var recordsBefore = JsonSerializer.Serialize(model.Store.ReadSentPosts());

        foreach (var target in new[] { thread, thread + 1, 0 })
        {
            var composer = new ComposerWindow(model, target) { Owner = owner, ShowInTaskbar = false };
            composer.Show(); await Pause();
            try
            {
                var selector = All<ComboBox>(composer).Single(c => AutomationProperties.GetName(c) == "发言使用的饼干");
                Require((selector.SelectedItem as CookieIdentity)?.Id == second.Id, "未默认选择默认饼干");
                selector.SelectedIndex = 0; selector.IsDropDownOpen = true; await Pause();
                var itemA = (ComboBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(0);
                var itemB = (ComboBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(1);
                var textA = string.Join(" / ", All<TextBlock>(itemA).Select(TextContent));
                var textB = string.Join(" / ", All<TextBlock>(itemB).Select(TextContent));
                Require(textA.Contains("更新后的原名A（常用 · 工作日）") && textB.Contains("饼干原名B") && textB.Contains("默认"), $"ID 和括号备注或默认标记错误: A={textA}; B={textB}");
                Require(!textA.Contains("最近") && !textB.Contains("未备注饼干") && !textA.Contains("/" + DateTime.Now.Day), "选择器仍显示重复名称或日期");
                if (target == thread) Require(textA.Contains("2 次") && !textA.Contains("上次") && textB.Contains("1 次") && textB.Contains("上次"), "统计混入其他串或上次使用错误");
                else if (target > 0) Require(textA.Contains("1 次") && textA.Contains("上次") && !textB.Contains(" 次"), "切换串后统计没有切换或显示零次");
                else Require(!textA.Contains(" 次") && !textB.Contains(" 次"), "发串显示了回应统计");
                var popup = All<Popup>(selector).Single(p => p.IsOpen);
                var popupBody = (FrameworkElement)popup.Child;
                Require(popupBody.ActualHeight <= selector.MaxDropDownHeight + 20 && All<ScrollViewer>(popupBody).Any(s => s.ScrollableHeight > 0), "大量饼干没有限制高度或无法滚动");
                foreach (var item in new[] { itemA, itemB })
                {
                    Require(item.ActualHeight <= 40, "选项不是紧凑单行");
                    var check = All<System.Windows.Shapes.Path>(item).Single();
                    Require(check.IsVisible == item.IsSelected, "勾选与当前选择不符");
                    var row = All<Border>(item).Single(b => b.Name == "Row");
                    Require(row.BorderThickness == new Thickness(0), "选中项出现了高亮边框");
                }
                if (target == thread)
                {
                    RenderPopup(popupBody, System.IO.Path.Combine(output, "cookie-options-light.png"));
                    App.ApplyTheme("dark"); await Pause(); RenderPopup(popupBody, System.IO.Path.Combine(output, "cookie-options-dark.png"));
                    App.ApplyTheme("light");
                }
                selector.IsDropDownOpen = false; await Pause();
                Require(model.State.ActiveCookieId == second.Id, "临时选择修改了默认饼干");
                Require(JsonSerializer.Serialize(model.Store.ReadSentPosts()) == recordsBefore, "切换选择更新了使用次数或上次使用记录");
                var collapsed = All<TextBlock>(selector).Where(t => t.IsVisible).ToArray();
                Require(!collapsed.Any(t => t.Text.Contains("默认")) && selector.ActualHeight <= 40, "收起状态未隐藏默认标记或高度过大");
                Require(collapsed.Any(t => t.Text == "上次") == (target == thread + 1), "收起状态的上次使用标记不正确");
                foreach (var label in All<TextBlock>(selector).Where(t => t.IsVisible))
                {
                    var bounds = label.TransformToAncestor(selector).TransformBounds(new Rect(label.RenderSize));
                    Require(bounds.Top >= 0 && bounds.Bottom <= selector.ActualHeight + 1, "选中饼干的多行文字被裁切");
                }
                if (target == thread)
                {
                    capture(composer, System.IO.Path.Combine(output, "reply-cookie-current-thread.png"));
                    composer.Width = composer.MinWidth; await Pause();
                    selector.IsDropDownOpen = true; await Pause(); RenderPopup(popupBody, System.IO.Path.Combine(output, "cookie-options-narrow.png"));
                    Require(popupBody.ActualWidth <= composer.ActualWidth, $"窄窗口下拉框横向溢出: popup={popup.Name} actual={popupBody.ActualWidth} min={popupBody.MinWidth} max={popupBody.MaxWidth} selector={selector.ActualWidth} window={composer.ActualWidth}");
                    var narrowFirst = (ComboBoxItem)selector.ItemContainerGenerator.ContainerFromIndex(0);
                    foreach (var label in All<TextBlock>(narrowFirst).Where(t => t.IsVisible))
                    {
                        var rect = label.TransformToAncestor(popupBody).TransformBounds(new Rect(label.RenderSize));
                        Require(rect.Right <= popupBody.ActualWidth + 1 && rect.Left >= 0, $"窄选项文字溢出: {label.Text} / {rect} / popup width {popupBody.ActualWidth}");
                    }
                    selector.IsDropDownOpen = false;
                }
            }
            finally { composer.Close(); }
        }
        Console.WriteLine("PASS: cookie remark/reimport/default persistence, per-thread reply selectors, latest cookie and temporary selection isolation.");
    }
    private static void RenderPopup(FrameworkElement root, string path)
    {
        root.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(root);
        var image = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(root), null, new Rect(root.RenderSize));
        image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static string TextContent(TextBlock text) => new System.Windows.Documents.TextRange(text.ContentStart, text.ContentEnd).Text;
    private static void Click(Window window, string label) => All<Button>(window).Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task Pause() => Task.Delay(180);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

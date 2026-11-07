using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class CookieManagerChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var vm = owner.Model; vm.State.Cookies = []; vm.State.ActiveCookieId = ""; vm.Save();
        const string secret = "synthetic-cookie-manager-credential";
        var window = new CookieWindow(vm) { Owner = owner, ShowInTaskbar = false };
        window.Show(); await Pause();
        try
        {
            var list = All<ListBox>(window).Single();
            var import = Button(window, "导入并使用");
            Require(!All<PasswordBox>(window).Any(p => p.IsVisible) && list.Items.Count == 0 && !Default(window).IsEnabled && Default(window).SelectedValue as string == "", "空管理页暴露导入表单或默认身份不是游客");
            Require(!All<Button>(window).Any(b => b.Content is "切换为游客" or "设置为默认饼干"), "仍保留与默认身份下拉重复的操作");
            capture(window, Path.Combine(output, "manager-empty-light.png"));
            App.ApplyTheme("dark"); await Pause(); capture(window, Path.Combine(output, "manager-empty-dark.png")); App.ApplyTheme("light");
            Click(window, "导入饼干"); await Pause();
            var value = All<PasswordBox>(window).Single();
            Require(value.IsVisible && !import.IsEnabled && !list.IsVisible, "导入未切换视图或空内容可提交");
            value.Password = "{bad json"; Require(!import.IsEnabled, "无效 JSON 可提交");
            value.Password = "1234567"; Require(!import.IsEnabled, "公开短 ID 可提交");
            foreach (var label in new[] { "选择二维码图片", "粘贴二维码", "官方用户系统", "‹ 返回管理" }) Require(Button(window, label).IsVisible, "导入入口缺失: " + label);
            var qr = CookieImportChecks.Qr(JsonSerializer.Serialize(new { cookie = secret, name = "UGiQJMZ" }));
            var qrPath = Path.Combine(output, "synthetic-qr.png");
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(qr)); using (var file = File.Create(qrPath)) png.Save(file);
            await window.ReadQrAsync(() => CookieImport.ReadFileAsync(qrPath));
            Require(import.IsEnabled && All<TextBlock>(window).Any(t => t.Text == "UGiQJMZ"), "QR 文件未通过现有解析与校验");
            capture(window, Path.Combine(output, "import-light.png"));
            App.ApplyTheme("dark"); await Pause(); capture(window, Path.Combine(output, "import-dark.png")); App.ApplyTheme("light");
            SetDpi(window, 1.5, 540, 400); await Pause();
            var importBounds = import.TransformToAncestor(window).TransformBounds(new Rect(import.RenderSize));
            Require(importBounds.Bottom <= window.ActualHeight && importBounds.Right <= window.ActualWidth, "窄窗口下导入操作被裁切");
            Require(All<ScrollViewer>(window).Any(s => s.IsVisible && s.ScrollableHeight > 0), "窄窗口导入表单不可滚动");
            capture(window, Path.Combine(output, "import-narrow-150.png")); SetDpi(window, 1, 760, 540); await Pause();
            Click(window, "导入并使用"); await Pause();
            Require(!value.IsVisible && value.Password == "" && vm.State.Cookies.Count == 1 && vm.ActiveCookie() == secret, "导入成功未返回管理、清除敏感输入或使用身份");
            Require(!File.ReadAllText(Path.Combine(vm.Store.Root, "state.json")).Contains(secret), "凭据明文保存");
            Require(!All<TextBlock>(window).Any(t => t.Text.Contains(secret)), "凭据回显到普通文字");
            Require(Default(window).IsEnabled && Default(window).SelectedValue as string == vm.State.ActiveCookieId, "导入后默认身份下拉未显示新饼干");
            var id = vm.State.ActiveCookieId;
            var remark = All<TextBox>(window).Single(t => AutomationProperties.GetName(t) == "饼干备注");
            remark.Text = "常用"; Click(window, "保存备注");
            Require(vm.Store.Load().Cookies.Single().Remark == "常用" && !Button(window, "保存备注").IsEnabled, "备注未保存或保存状态未更新");
            capture(window, Path.Combine(output, "manager-single-light.png"));
            Click(window, "导入饼干");
            await window.ReadQrAsync(() => CookieImport.ReadImageAsync(qr)); Click(window, "导入并使用");
            Require(vm.State.Cookies.Count == 1 && vm.State.ActiveCookieId == id && vm.State.Cookies[0].Remark == "常用", "重复二维码导入丢失身份或备注");
            Click(window, "导入饼干"); value.Password = "userhash=" + secret; Click(window, "导入并使用");
            Require(vm.State.Cookies.Count == 1 && vm.State.Cookies[0].Name == "UGiQJMZ", "Cookie 文本重复导入丢失原名");
            Click(window, "导入饼干");
            await window.ReadQrAsync(() => Task.FromException<CookieImport.Value>(new ArgumentException("没有识别到二维码")));
            Require(!import.IsEnabled && All<TextBlock>(window).Any(t => t.Text == "没有识别到二维码"), "识别失败未反馈或仍能导入");
            var gate = new TaskCompletionSource<CookieImport.Value>(); var pending = window.ReadQrAsync(() => gate.Task);
            Click(window, "‹ 返回管理"); gate.SetResult(new(secret, "迟到识别")); await pending;
            Require(!value.IsVisible && value.Password == "" && vm.State.Cookies[0].Name == "UGiQJMZ", "返回管理后迟到识别改变了内容");

            ConfirmRemoval(window, false); Require(vm.State.Cookies.Count == 1, "取消移除删除了饼干");
            ConfirmRemoval(window, true); Require(vm.State.Cookies.Count == 0 && vm.State.ActiveCookieId == "", "确认移除未保存");
        }
        finally { window.Close(); }

        vm.State.Cookies = Enumerable.Range(1, 40).Select(i => new CookieIdentity("item" + i, "Cookie" + i, SecretProtection.Protect("synthetic-multi-cookie-" + i)) { Remark = i % 2 == 0 ? "备注 " + i : "" }).ToList();
        vm.State.ActiveCookieId = "item1"; vm.Save();
        var many = new CookieWindow(vm, "item2") { Owner = owner, ShowInTaskbar = false };
        many.Show(); await Pause();
        try
        {
            var list = All<ListBox>(many).Single(); list.SelectedIndex = 2; await Pause();
            Require(vm.State.ActiveCookieId == "item1" && list.SelectedItem is CookieIdentity { Id: "item3" }, "列表选中改变了默认身份");
            Require(All<TextBlock>(many).Any(t => t.Text == "当前发言：Cookie2"), "当前发言身份与默认混淆");
            Require(Default(many).SelectedValue as string == "item1" && Default(many).Items.Count == 41, "默认身份下拉未列出游客和全部饼干");
            await SelectDefault(many, "item5");
            Require(vm.State.ActiveCookieId == "item5" && vm.Store.Load().ActiveCookieId == "item5", "下拉选择未设为默认或未保存");
            Require(list.SelectedItem is CookieIdentity { Id: "item3" }, "设置默认改变了列表选中项");
            Require(All<TextBlock>(many).Any(t => t.Text == "当前发言：Cookie2"), "默认设置覆盖临时发言选择");
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme);
                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                {
                    SetDpi(many, scale, scale == 1.5 ? 560 : 760, scale == 1.5 ? 450 : 540); await Pause();
                    Require(Math.Abs(VisualTreeHelper.GetDpi(many).DpiScaleX - scale) < .01, "WPF 未采用测试 DPI");
                    var scroll = All<ScrollViewer>(list).First(); Require(scroll.ScrollableHeight > 0, "大量饼干列表没有独立滚动");
                    list.ScrollIntoView(vm.State.Cookies.Last()); await Pause();
                    Require(scroll.VerticalOffset > 0, "大量饼干列表不能滚动到底");
                    scroll.ScrollToTop(); await Pause();
                    foreach (var control in new FrameworkElement[] { Button(many, "完成"), Button(many, "导入饼干"), All<Button>(many).Single(b => AutomationProperties.GetName(b) == "关闭") })
                    {
                        var rect = control.TransformToAncestor(many).TransformBounds(new Rect(control.RenderSize));
                        Require(rect.Bottom <= many.ActualHeight + 1 && rect.Right <= many.ActualWidth + 1, "主要操作在小窗口或 DPI 下被裁切");
                    }
                    capture(many, Path.Combine(output, $"manager-{theme}-{scale * 100:0}.png"));
                }
            }
            list.SelectedItem = vm.State.Cookies.Find(c => c.Id == "item2");
            ConfirmRemoval(many, true);
            Require(many.RequestedCookieId == "item5" && All<TextBlock>(many).Any(t => t.Text == "当前发言：Cookie5"), "移除临时使用饼干后未回退到默认身份");
            list.SelectedItem = vm.State.Cookies.Find(c => c.Id == "item9"); await Pause();
            await SelectDefault(many, "");
            Require(vm.State.ActiveCookieId == "" && vm.Store.Load().ActiveCookieId == "" && Default(many).SelectedValue as string == "", "游客切换没有解除默认饼干");
            Require(many.RequestedCookieId == "item5" && All<TextBlock>(many).Any(t => t.Text == "当前发言：Cookie5"), "切换游客改变了本次发言的饼干");
            Require(list.SelectedItem is CookieIdentity { Id: "item9" }, "切换游客改变了列表选中项");
            capture(many, Path.Combine(output, "manager-guest-dark.png"));
            await SelectDefault(many, "item7");
            Require(vm.State.ActiveCookieId == "item7" && vm.Store.Load().ActiveCookieId == "item7" && list.SelectedItem is CookieIdentity { Id: "item9" }, "从游客设回默认饼干失败或改变了选中项");
            // 重新从资料目录载入，相当于重启后打开饼干管理。
            using (var reopened = new MainViewModel(new XIslandApi(new System.Net.Http.HttpClient()), new LocalStore(vm.Store.Root)))
            {
                var again = new CookieWindow(reopened) { Owner = owner, ShowInTaskbar = false };
                again.Show(); await Pause();
                try { Require(reopened.State.ActiveCookieId == "item7" && Default(again).SelectedValue as string == "item7", "重启后默认饼干未恢复"); }
                finally { again.Close(); }
            }
            list.SelectedItem = vm.State.Cookies.Find(c => c.Id == "item7"); ConfirmRemoval(many, true); await Pause();
            Require(vm.State.ActiveCookieId == "" && vm.Store.Load().ActiveCookieId == "" && Default(many).SelectedValue as string == "", "删除默认饼干后未回到游客");
        }
        finally { many.Close(); App.ApplyTheme("light"); }
        Console.WriteLine("PASS: manager/import views; empty/single/many identities; remark, default identity dropdown (guest/cookie both ways, persisted, independent of list and composer selection, default removal), QR/file/image/text validation, encrypted persistence, duplicate import, stale decode, removal confirmation, light/dark and 100/125/150% DPI.");
    }
    private static void ConfirmRemoval(Window parent, bool accept)
    {
        parent.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (Action)(() =>
        {
            var confirm = Application.Current.Windows.OfType<ConfirmWindow>().Single(); Click(confirm, accept ? "确定" : "取消");
        }));
        Click(parent, "移除饼干");
    }
    private static void SetDpi(Window window, double scale, double width, double height)
    {
        var rect = new NativeRect { Right = (int)(width * scale), Bottom = (int)(height * scale) };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rect, memory, false); var dpi = (int)(96 * scale); SendMessage(new WindowInteropHelper(window).Handle, 0x02E0, new((dpi << 16) | dpi), memory); window.Width = width; window.Height = height; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static ComboBox Default(Window root) => All<ComboBox>(root).Single(c => AutomationProperties.GetName(c) == "默认发言身份");
    // 模拟用户在下拉中选择；保存在选择事件之后执行，所以等待一次调度。
    private static async Task SelectDefault(Window window, string id) { var box = Default(window); Require(box.IsEnabled && box.IsVisible, "默认身份下拉不可用"); box.SelectedValue = id; await Pause(); }
    private static Button Button(Window root, string text) => All<Button>(root).Single(b => Equals(b.Content, text));
    private static void Click(Window window, string text) { var button = Button(window, text); Require(button.IsEnabled && button.IsVisible, "操作不可用: " + text); button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
    private static Task Pause() => Task.Delay(150);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

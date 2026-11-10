using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>界面文案精简后的检查：没有残留技术说明和不统一的引号/用词，删掉说明的行仍垂直居中，窄窗口和高 DPI 不溢出。</summary>
/// <remarks>只打开设置、饼干管理、发言窗口和颜文字编辑窗口；不点击会打开浏览器或资源管理器的按钮，不移动鼠标。</remarks>
internal static class UiCopyChecks
{
    // 已删除或统一掉的说法；出现在这些窗口的可见文字里就算回退。
    private static readonly string[] Banned =
    [
        "state.json", "licenses", "微软雅黑", "备用字体", "TUN", "GitHub 正式版", "接口", "含串列表预览", "黑条保留", "仅展示效果",
        "中栏", "淘汰", "隐藏输入", "原标识", "版块", "“", "”", "‘", "’", "切换应用的浅色", "保证足够的对比度", "使用的饼干", "发到哪个",
    ];

    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var profile = Path.Combine(output, "copy-profile");
        if (Directory.Exists(profile)) Directory.Delete(profile, true);
        var vm = new MainViewModel(UiRefinementChecks.CreateApi(), new LocalStore(profile));
        vm.State.Forums = [new Forum(4, "综合版1", "综合"), new Forum(25, "ROLL点", "综合")];
        vm.State.Cookies = [new CookieIdentity("a", "AbCd123", SecretProtection.Protect("synthetic-copy-check-a")) { Remark = "常用" }, new CookieIdentity("b", "EfGh456", SecretProtection.Protect("synthetic-copy-check-b"))];
        vm.State.ActiveCookieId = "a"; vm.State.AutoCheckUpdates = false; vm.State.InstallUpdatesOnExit = false;
        var report = new List<object>();
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                vm.State.Theme = theme; App.ApplyTheme(theme);
                foreach (var scale in new[] { 1d, 1.25, 1.5 })
                {
                    report.Add(await SettingsAsync(owner, vm, output, capture, theme, scale));
                    report.Add(await CookiesAsync(owner, vm, output, capture, theme, scale));
                    report.Add(await ComposerAsync(owner, vm, output, capture, theme, scale));
                    report.Add(await EmoticonEditAsync(owner, vm, output, capture, theme, scale));
                }
            }
            File.WriteAllText(Path.Combine(output, "ui-copy-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS ui copy: settings (7 categories), cookie management/import, composer (new/reply), emoticon editor; no removed technical wording or curly quotes, single-line rows centred, no overflow, licence entry, light/dark at 100/125/150%.");
        }
        finally { App.ApplyTheme("light"); }
    }

    private static async Task<object> SettingsAsync(MainWindow owner, MainViewModel vm, string output, Action<Window, string> capture, string theme, double scale)
    {
        var settings = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false };
        settings.Show(); await Pause();
        try
        {
            SetDpi(settings, scale, 880, 690); await Pause();
            var nav = Named<ListBox>(settings, "设置分类"); var scroll = Named<ScrollViewer>(settings, "设置内容");
            var pages = new List<object>();
            foreach (var page in nav.Items.Cast<string>().ToArray())
            {
                nav.SelectedItem = page; await Pause();
                if (page == "外观") { All<Expander>(settings).Single(e => Equals(e.Header, "标识颜色")).IsExpanded = true; await Pause(); }
                CheckText(settings, $"设置·{page}");
                Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"设置·{page} 横向溢出（{theme}/{scale}）");
                var rows = CheckRows(settings, $"设置·{page}");
                pages.Add(new { Page = page, Rows = rows, Height = Math.Round(scroll.ExtentHeight) });
                if (scale == 1.25 || page is "阅读" or "关于") capture(settings, Path.Combine(output, $"settings-{page}-{theme}-{scale:0.##}.png"));
            }
            nav.SelectedItem = "关于"; await Pause();
            var license = All<Button>(settings).Single(b => Equals(b.Content, "开源许可"));
            Require(license.IsVisible && license.IsEnabled, "关于页缺少开源许可入口");
            var location = SettingsWindow.LicensesLocation();
            Require(Directory.Exists(location) || location == ReleaseUpdater.Repository + "/blob/main/LICENSE", "开源许可入口的位置不正确");
            Require(All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.Contains("非官方")), "关于页缺少非官方客户端声明");
            nav.SelectedItem = "数据与记录"; await Pause();
            Require(All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.StartsWith("最近浏览 ")) && All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.Contains("加密保存")), "数据与记录的统计用词或隐私说明不正确");
            nav.SelectedItem = "缓存"; await Pause();
            Require(All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.StartsWith("清理缓存只删除")) && All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.Contains("迁移")), "缓存页缺少清理范围或迁移说明");
            // 窄窗口：说明文字换行，不横向溢出。
            SetDpi(settings, scale, 660, 470); await Pause();
            foreach (var page in new[] { "外观", "缓存", "数据与记录", "关于" })
            {
                nav.SelectedItem = page; await Pause();
                Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"窄窗口 设置·{page} 横向溢出（{theme}/{scale}）");
                CheckRows(settings, $"窄窗口 设置·{page}");
            }
            capture(settings, Path.Combine(output, $"settings-关于-compact-{theme}-{scale:0.##}.png"));
            return new { Window = "Settings", Theme = theme, Scale = scale, Pages = pages };
        }
        finally { settings.Close(); }
    }

    private static async Task<object> CookiesAsync(MainWindow owner, MainViewModel vm, string output, Action<Window, string> capture, string theme, double scale)
    {
        var cookies = new CookieWindow(vm) { Owner = owner, ShowInTaskbar = false };
        cookies.Show(); await Pause();
        try
        {
            SetDpi(cookies, scale, 760, 540); await Pause();
            CheckText(cookies, "饼干管理");
            Require(All<TextBlock>(cookies).Any(t => t.IsVisible && t.Text == "浏览时使用，也是发言时默认选中的饼干。"), "默认发言身份缺少用途说明");
            Require(Named<ComboBox>(cookies, "默认发言身份").ToolTip is null, "默认发言身份仍有重复的悬停说明");
            capture(cookies, Path.Combine(output, $"cookies-{theme}-{scale:0.##}.png"));
            All<Button>(cookies).Single(b => Equals(b.Content, "导入饼干")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Pause();
            CheckText(cookies, "导入饼干");
            Require(All<TextBlock>(cookies).Any(t => t.IsVisible && t.Text.Contains("只在本机加密保存")), "导入页缺少本机加密说明");
            capture(cookies, Path.Combine(output, $"cookie-import-{theme}-{scale:0.##}.png"));
            return new { Window = "Cookies", Theme = theme, Scale = scale };
        }
        finally { cookies.Close(); }
    }

    private static async Task<object> ComposerAsync(MainWindow owner, MainViewModel vm, string output, Action<Window, string> capture, string theme, double scale)
    {
        var labels = new List<string>();
        foreach (var thread in new long[] { 0, 101 })
        {
            var composer = new ComposerWindow(vm, thread) { Owner = owner, ShowInTaskbar = false };
            composer.Show(); await Pause();
            try
            {
                SetDpi(composer, scale, UiMetrics.DialogWidth, 820); await Pause();
                CheckText(composer, thread == 0 ? "发串" : "回应");
                var shown = All<TextBlock>(composer).Where(t => t.IsVisible && t.FontWeight == FontWeights.Medium).Select(t => t.Text).ToList();
                Require(shown.Contains("饼干") && shown.Contains("正文") && (thread > 0 || shown.Contains("发串板块")), $"发言窗口的字段标题不正确：{string.Join("/", shown)}");
                labels.AddRange(shown);
                if (scale == 1.25) capture(composer, Path.Combine(output, $"composer-{(thread == 0 ? "new" : "reply")}-{theme}.png"));
            }
            finally { composer.Close(); }
        }
        // 关闭发言窗口会保存草稿；清掉，免得影响其他检查。
        vm.State.Drafts.Clear(); vm.Save();
        return new { Window = "Composer", Theme = theme, Scale = scale, Labels = labels.Distinct() };
    }

    private static async Task<object> EmoticonEditAsync(MainWindow owner, MainViewModel vm, string output, Action<Window, string> capture, string theme, double scale)
    {
        var editor = new EmoticonEditWindow(vm.State, vm.Save) { Owner = owner, ShowInTaskbar = false };
        editor.Show(); await Pause();
        try
        {
            SetDpi(editor, scale, UiMetrics.DialogWidth, 560); await Pause();
            CheckText(editor, "颜文字编辑");
            Require(All<TextBlock>(editor).Any(t => t.IsVisible && t.Text == "空格和换行会原样保留。"), "颜文字编辑窗口的说明不正确");
            if (scale == 1.25) capture(editor, Path.Combine(output, $"emoticon-edit-{theme}.png"));
            return new { Window = "EmoticonEdit", Theme = theme, Scale = scale };
        }
        finally { editor.Close(); }
    }

    private static void CheckText(Window window, string where)
    {
        foreach (var text in All<TextBlock>(window).Where(t => t.IsVisible).Select(t => t.Text).Concat(All<Button>(window).Where(b => b.IsVisible).Select(b => b.Content as string ?? "")))
            foreach (var word in Banned)
                Require(!text.Contains(word, StringComparison.Ordinal), $"{where} 仍有「{word}」：{text}");
    }

    // 设置行：左侧标题（可带说明）、右侧控件。只有一行标题时，标题和控件的垂直中心必须对齐。
    private static int CheckRows(Window window, string where)
    {
        var count = 0;
        foreach (var grid in All<Grid>(window).Where(g => g.IsVisible && g.ColumnDefinitions.Count == 2 && g.Children.Count == 2
                     && g.Children[0] is StackPanel && g.Children[1] is FrameworkElement && Grid.GetColumn(g.Children[1]) == 1))
        {
            var labels = (StackPanel)grid.Children[0]; var control = (FrameworkElement)grid.Children[1];
            if (labels.Children.Count != 1 || labels.Children[0] is not TextBlock title) continue;
            var a = title.TranslatePoint(new Point(0, title.ActualHeight / 2), grid).Y;
            var b = control.TranslatePoint(new Point(0, control.ActualHeight / 2), grid).Y;
            Require(Math.Abs(a - b) < 1.5, $"{where}「{title.Text}」标题与控件没有垂直居中（{a:0.0} / {b:0.0}）");
            count++;
        }
        return count;
    }

    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => All<T>(root).Single(c => AutomationProperties.GetName(c) == name);
    private static async Task Pause()
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void SetDpi(Window window, double scale, double width, double height)
    {
        var rect = new NativeRect { Right = (int)(width * scale), Bottom = (int)(height * scale) };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rect, memory, false); var dpi = (int)(96 * scale); SendMessage(new WindowInteropHelper(window).Handle, 0x02E0, new((dpi << 16) | dpi), memory); window.Width = width; window.Height = height; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);
}

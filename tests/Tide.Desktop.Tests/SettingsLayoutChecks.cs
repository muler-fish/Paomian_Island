using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class SettingsLayoutChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var vm = owner.Model;
        vm.State.FontSize = 16; vm.State.ReadingLineHeight = 1.85; vm.State.ReadingLetterSpacing = 0;
        vm.State.Theme = "light"; vm.State.AutoCheckUpdates = false; vm.State.InstallUpdatesOnExit = false;
        App.ApplyTheme("light"); vm.AppearanceChanged();
        var originalCache = vm.Store.Cache.Root;
        // Only synthetic files below this run's isolated profile are ever migrated or cleared.
        var customPath = Path.Combine(vm.Store.Root, "缓存路径测试", "包含较长目录名称以验证自动换行和完整路径复制", "reading-cache");
        vm.Store.Cache.Configure(customPath, vm.State.CacheLimitMb); vm.State.CacheParentPath = customPath; vm.Save();
        var initialState = File.ReadAllText(Path.Combine(vm.Store.Root, "state.json"));
        var settings = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false };
        settings.Show(); await Pause();
        try
        {
            var nav = Named<ListBox>(settings, "设置分类");
            var scroll = Named<ScrollViewer>(settings, "设置内容");
            async Task Select(string page) { nav.SelectedItem = page; await Pause(); Require(scroll.VerticalOffset < 1, "category did not reset scroll"); }
            Require(nav.Items.Cast<string>().SequenceEqual(new[] { "阅读", "外观", "网络", "缓存", "数据与记录", "关于" }), "categories incomplete or reordered");
            Require(Equals(nav.SelectedItem, "阅读"), "default category not reading");
            Require(File.ReadAllText(Path.Combine(vm.Store.Root, "state.json")) == initialState, "opening settings modified saved state");
            var font = Named<Slider>(settings, "正文字号"); var line = Named<Slider>(settings, "行间距"); var spacing = Named<Slider>(settings, "字间距");
            Require(font.Minimum == 13 && font.Maximum == 24 && font.TickFrequency == 1
                && line.Minimum == 1.3 && line.Maximum == 2.5 && line.TickFrequency == .05
                && spacing.Minimum == 0 && spacing.Maximum == 3 && spacing.TickFrequency == .25, "reading range/precision changed");
            font.Value = 19; line.Value = 2.1; spacing.Value = 1.25; await Pause();
            var preview = All<PostPreview>(settings).Single();
            Require(preview.FontSize == 19 && Math.Abs(preview.LineHeight - 39.9) < .01 && ReadingTypography.GetLetterSpacing(preview) == 1.25, "preview not live");
            Require(preview.Inlines.OfType<InlineUIContainer>().Any(), "tracking absent in preview");
            var saved = vm.Store.Load();
            Require(saved.FontSize == 19 && saved.ReadingLineHeight == 2.1 && saved.ReadingLetterSpacing == 1.25, "reading changes not persisted");
            Named<CheckBox>(settings, "启用防剧透").IsChecked = true;
            Require(vm.Store.Load().HideSpoilers, "spoiler did not save");
            await Select("缓存"); await Wait(() => Button(settings, "刷新占用").IsEnabled);
            var limit = Named<TextBox>(settings, "缓存容量上限"); limit.Text = "123";
            await Select("外观"); var theme = Named<ComboBox>(settings, "外观主题"); theme.SelectedIndex = 1; await Pause();
            Require(vm.State.Theme == "dark" && vm.Store.Load().Theme == "dark", "theme not applied and saved");
            Named<CheckBox>(settings, "自动加载正文缩略图").IsChecked = false;
            Require(!vm.Store.Load().ShowImages, "image preference lost");
            await Select("缓存");
            Require(limit.Text == "123" && vm.State.CacheLimitMb != 123, "switch/theme lost or committed pending cache limit");
            await Select("网络");
            Named<ComboBox>(settings, "访问 X 岛").SelectedIndex = 1;
            Named<ComboBox>(settings, "检查与下载更新").SelectedIndex = 0;
            saved = vm.Store.Load();
            Require(!saved.IslandUseProxy && saved.UpdateUseProxy && !NetworkClients.IslandUseProxy && NetworkClients.UpdateUseProxy, "independent routes not saved");
            await Select("缓存");
            var usage = Named<TextBlock>(settings, "缓存占用");
            limit.Text = "4097"; var oldLimit = vm.State.CacheLimitMb;
            Click(settings, "应用上限"); await Wait(() => usage.Text.StartsWith("缓存操作未完成"));
            Require(vm.State.CacheLimitMb == oldLimit && limit.Text == "4097" && Button(settings, "应用上限").IsEnabled, "invalid input changed state or could not recover");
            limit.Text = "2"; Click(settings, "应用上限"); await Wait(() => vm.State.CacheLimitMb == 2 && Button(settings, "刷新占用").IsEnabled);
            vm.Store.Cache.Write("images/" + new string('C', 64) + ".bin", new byte[125000]);
            Click(settings, "刷新占用"); await Wait(() => usage.Text.Contains("0.13 MB / 2 MB"));
            var path = Named<TextBox>(settings, "缓存存储路径");
            Require(path.Text == vm.Store.Cache.Root && path.ToolTip as string == path.Text && path.IsReadOnly, "full cache path unavailable");
            path.SelectAll(); Require(path.SelectedText == vm.Store.Cache.Root, "path not selectable");

            // Capture all categories; reading/cache/about are the primary acceptance views.
            font.Value = 16; line.Value = 1.85; spacing.Value = 0;
            foreach (var mode in new[] { "light", "dark" })
            {
                await Select("外观"); theme.SelectedIndex = mode == "dark" ? 1 : 0; await Pause();
                foreach (var entry in new[] { ("阅读", "reading"), ("外观", "appearance"), ("网络", "network"), ("缓存", "cache"), ("数据与记录", "data"), ("关于", "about") })
                {
                    await Select(entry.Item1);
                    FixedActions(settings);
                    capture(settings, Path.Combine(output, $"settings-{entry.Item2}-{mode}.png"));
                }
            }
            await Select("缓存");
            Click(settings, "恢复默认路径"); await Wait(() => path.Text == originalCache && Button(settings, "刷新占用").IsEnabled);
            Require(vm.State.CacheParentPath == "" && vm.Store.Cache.SizeBytes() >= 125000, "reset path lost cache or setting");
            Click(settings, "清理缓存"); await Wait(() => vm.Store.Cache.SizeBytes() == 0 && Button(settings, "刷新占用").IsEnabled);
            Require(vm.Store.Load().CacheLimitMb == 2 && vm.State.SubscriptionAccounts.Count > 0, "clear damaged saved data");

            await Select("关于");
            Named<CheckBox>(settings, "启动时自动检查更新").IsChecked = true;
            Require(vm.Store.Load().AutoCheckUpdates, "auto-check setting not persisted");
            // Drive the existing controller's UI notifications only: no GitHub calls, downloads or installation.
            void Update(string property, object? value) => typeof(UpdateController).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm.Updates, value);
            void Status(string message) => typeof(UpdateController).GetMethod("SetStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm.Updates, new object[] { message });
            Update("Busy", true); Status("正在检查更新…"); Require(!Button(settings, "检查更新").IsEnabled, "busy update state not shown");
            Update("Available", new AppRelease("9.9.9", "", new("https://example.invalid"), new("https://example.invalid/test.zip"), "", 10));
            Update("Busy", false); Status("发现新版本 9.9.9"); await Pause(); Require(Button(settings, "下载更新").IsVisible, "download entry missing");
            Update("Staged", new StagedUpdate(Path.Combine(output, "not-installed"), "")); Status("更新已准备好，可重启安装"); await Pause();
            Require(Button(settings, "重启安装").IsVisible && !Button(settings, "下载更新").IsVisible, "install entry missing");
            Update("Staged", null); Update("Available", null); Status("尚未检查更新");

            await Select("数据与记录"); var count = vm.State.Drafts.Count;
            vm.State.Drafts.Add(new Draft("settings-layout-fixture", 4, 0, "", "隔离的布局检查草稿", "", false));
            await Select("阅读"); await Select("数据与记录");
            Require(All<TextBlock>(settings).Any(t => t.Text.Contains($"草稿 {count + 1} 份")), "data counts stale after category change");

            font.Value = 24; line.Value = 2.5; spacing.Value = 3;
            foreach (var mode in new[] { "light", "dark" })
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                await Select("外观"); theme.SelectedIndex = mode == "dark" ? 1 : 0;
                // Simulate per-monitor DPI messages with actual WPF measure/arrange at a compact size.
                SetDpi(settings, scale, 660, 470); await Pause();
                Require(Math.Abs(VisualTreeHelper.GetDpi(settings).DpiScaleX - scale) < .01, "DPI not applied");
                Require(double.IsFinite(settings.MaxHeight) && double.IsFinite(settings.MaxWidth), "work-area limits missing");
                foreach (var entry in new[] { ("阅读", "reading"), ("缓存", "cache"), ("关于", "about") })
                {
                    await Select(entry.Item1); FixedActions(settings);
                    Require(scroll.ScrollableHeight > 0, "compact content is not independently scrollable");
                    var footerTop = Button(settings, "完成").TranslatePoint(new(), settings).Y;
                    scroll.ScrollToEnd(); await Pause(); FixedActions(settings);
                    Require(Math.Abs(footerTop - Button(settings, "完成").TranslatePoint(new(), settings).Y) < 1, "footer moved with scroll");
                    Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, "horizontal content overflow");
                    capture(settings, Path.Combine(output, $"settings-{entry.Item2}-compact-{mode}-{scale:0.##}.png"));
                }
            }
            SetDpi(settings, 1, 880, 690); await Select("阅读");
            font.Value = 16; line.Value = 1.85; spacing.Value = 0;
            font.Focus(); Slider.IncreaseSmall.Execute(null, font); await Pause();
            Require(vm.State.FontSize == 17, "keyboard slider change failed");
            nav.Focus(); Require(nav.IsKeyboardFocusWithin, "category navigation cannot receive keyboard focus");
            capture(settings, Path.Combine(output, "settings-keyboard-focus.png"));
            Click(settings, "完成"); Require(!settings.IsVisible, "done did not close");
        }
        finally { settings.Close(); }
        var reopened = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false };
        reopened.Show(); await Pause();
        Require(Equals(Named<ListBox>(reopened, "设置分类").SelectedItem, "阅读") && Named<Slider>(reopened, "正文字号").Value == 17, "reopen reset saved reading values or wrong default category");
        reopened.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(reopened)!, 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Require(!reopened.IsVisible, "Esc no longer closes");
        Console.WriteLine("PASS settings: six categories, live preview/ranges/persistence, pending input across theme/category changes, network isolation, cache validation/usage/migration/clear, update states, record counts, fixed actions/scrolling at 100/125/150%, keyboard and reopen/Esc.");
    }
    private static void FixedActions(Window window)
    {
        foreach (var control in new[] { Button(window, "完成"), Named<Button>(window, "关闭") })
        {
            var p = control.TranslatePoint(new(), window);
            Require(control.IsVisible && control.ActualWidth >= 30 && control.ActualHeight >= 30 && p.X >= 0 && p.Y >= 0 && p.X + control.ActualWidth <= window.ActualWidth && p.Y + control.ActualHeight <= window.ActualHeight, "fixed action clipped");
        }
    }
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => All<T>(root).Single(c => AutomationProperties.GetName(c) == name);
    private static Button Button(Window window, string text) => All<Button>(window).Single(b => Equals(b.Content, text));
    private static void Click(Window window, string text) { var button = Button(window, text); Require(button.IsEnabled && button.IsVisible, "button unavailable: " + text); button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }
    private static Task Pause() => Task.Delay(120);
    private static async Task Wait(Func<bool> ready) { for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(30); Require(ready(), "operation timed out"); }
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

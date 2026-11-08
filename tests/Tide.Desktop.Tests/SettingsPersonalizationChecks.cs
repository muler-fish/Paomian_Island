using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>设置页个性化：主题强调色、身份标识颜色、图片加载模式、缓存与个人数据隔离、旧资料兼容。只用隔离资料和模拟接口。</summary>
internal static class SettingsPersonalizationChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var root = Path.Combine(output, "personal-profile");
        var store = new LocalStore(root);
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        vm.State.Theme = "light"; vm.State.AccentColor = ""; vm.State.IdentityColors = new(); vm.State.AutoCheckUpdates = false;
        vm.State.CustomEmoticons = [new("keep", "(・∀・)保留", "保留")];
        vm.State.Drafts = [new("draft-keep", 4, 0, "", "保留的草稿", "", false)];
        vm.State.Cookies = [new("c1", "测试饼干", SecretProtection.Protect("synthetic-cookie")) { Remark = "备注保留" }];
        vm.Save();
        Personalization.Load(vm.State); App.ApplyTheme("light");
        var report = new Dictionary<string, object>();
        var themeAccent = Brush("Accent"); var themeCookie = Brush("CookieForeground");
        Require(themeAccent == Personalization.Rgb("#286D59") && !Overridden("Accent"), "默认强调色不是主题原值或仍有覆盖");
        // 另开一个主窗口加载模拟的板块和串（含红名），核对串列表、阅读区身份标签的颜色同步。图片设为点击加载，不联网；视图模型由窗口关闭时释放。
        var readerVm = new MainViewModel(RefinedReaderChecks.CreateApi(), new LocalStore(Path.Combine(output, "reader-profile")));
        readerVm.State.ListImages = readerVm.State.ReaderImages = ImageModes.Click; readerVm.AppearanceChanged();
        readerVm.State.Forums = [new(1, "综合线", "时间线", IsTimeline: true), new(4, "综合版1", "综合")];
        var main = new MainWindow(readerVm) { Owner = owner, ShowInTaskbar = false, Width = 1280, Height = 800 };
        main.Show(); await Pause(300);
        await readerVm.SelectForumAsync(readerVm.State.Forums[0]); await readerVm.OpenThreadAsync(52752005); await Pause(600);
        main.UpdateLayout();
        var settings = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false };
        settings.Show(); await Pause();
        try
        {
            var nav = Named<ListBox>(settings, "设置分类");
            Require(nav.Items.Cast<string>().SequenceEqual(new[] { "阅读", "外观", "内容与图片", "网络", "缓存", "数据与记录", "关于" }), "分类不是七类");
            async Task Select(string page) { nav.SelectedItem = page; await Pause(); }
            await Select("外观");
            var expander = All<Expander>(settings).Single(e => Equals(e.Header, "标识颜色"));
            Require(!expander.IsExpanded, "标识颜色没有默认折叠");
            Require(!All<Slider>(settings).Any(s => s.IsVisible && (AutomationProperties.GetName(s) ?? "").EndsWith("色相")), "调色滑块默认就展开了");
            capture(settings, Path.Combine(output, "appearance-default-light.png"));

            // 1. 预设强调色：浅色/深色分别取预设值，身份标识颜色不受影响
            Click(Named<Button>(settings, "强调色：湖蓝")); await Pause();
            Require(vm.State.AccentColor == "#2F6690" && store.Load().AccentColor == "#2F6690", "预设强调色没有保存");
            Require(Brush("Accent") == Personalization.Rgb("#2F6690") && Brush("CookieForeground") == themeCookie, "强调色没有生效或改动了饼干标识");
            Require(Brush("AccentButtonBackground") == Brush("Accent"), "WPF UI 主要按钮没有同步强调色");
            var preview = All<Wpf.Ui.Controls.Button>(settings).Single(b => Equals(b.Content, "主要按钮"));
            Require(((SolidColorBrush)preview.Background).Color == Personalization.Rgb("#2F6690"), "实时预览没有更新");
            capture(settings, Path.Combine(output, "appearance-accent-blue-light.png"));
            await Theme(settings, "dark");
            Require(Brush("Accent") == Personalization.Rgb("#9DBBD6"), "深色主题没有使用预设的深色取值");
            capture(settings, Path.Combine(output, "appearance-accent-blue-dark.png"));
            await Theme(settings, "light");

            // 2. 自定义 HEX：浅色、深色都保证对比度；无效输入不改变设置
            var hex = Named<TextBox>(settings, "自定义强调色 HEX");
            hex.Text = "#E6B800"; await Pause(); // 浅黄色：浅色主题下必须自动加深
            Require(vm.State.AccentColor == "#E6B800", "HEX 没有保存");
            var accentContrast = new Dictionary<string, double>();
            foreach (var mode in new[] { "light", "dark" })
            {
                await Theme(settings, mode);
                var surface = Brush("Surface"); var accent = Brush("Accent"); var on = Brush("OnAccent");
                accentContrast[mode + "-accent/surface"] = Math.Round(Personalization.Contrast(accent, surface), 2);
                accentContrast[mode + "-onAccent/accent"] = Math.Round(Personalization.Contrast(on, accent), 2);
                Require(Personalization.Contrast(accent, surface) >= 4.5 && Personalization.Contrast(on, accent) >= 4.5, $"{mode} 强调色对比度不足");
            }
            report["CustomAccentContrast"] = accentContrast;
            await Theme(settings, "light");
            hex.Text = "#GGGGGG"; Keyboard(hex); await Pause();
            Require(vm.State.AccentColor == "#E6B800" && All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.StartsWith("请输入 #RRGGBB")), "无效 HEX 改变了设置或没有提示");
            // 调色滑块
            var adjust = Named<ToggleButton>(settings, "自定义强调色调色"); adjust.IsChecked = true; await Pause();
            var hue = Named<Slider>(settings, "自定义强调色色相"); hue.Value = 280; await Pause();
            Require(vm.State.AccentColor != "#E6B800" && Math.Abs(Personalization.ToHsl(Personalization.Rgb(vm.State.AccentColor)).H - 280) < 3, "色相滑块没有改变颜色");
            capture(settings, Path.Combine(output, "appearance-mixer-light.png"));
            adjust.IsChecked = false;
            // 恢复默认
            Click(Named<Button>(settings, "自定义强调色恢复默认")); await Pause();
            Require(vm.State.AccentColor == "" && !Overridden("Accent") && Brush("Accent") == themeAccent, "恢复默认强调色失败");

            // 3. 标识颜色：单项设置、主色变化不影响强调色、单项与全部恢复
            expander.IsExpanded = true; await Pause();
            Named<TextBox>(settings, "饼干 ID颜色 HEX").Text = "#8E44AD"; await Pause();
            Named<TextBox>(settings, "PO颜色 HEX").Text = "#D35400"; await Pause();
            Named<TextBox>(settings, "SAGE颜色 HEX").Text = "#7F8C8D"; await Pause();
            Named<TextBox>(settings, "Admin（红名）颜色 HEX").Text = "#C0392B"; await Pause();
            var saved = store.Load().IdentityColors;
            Require(saved is { Cookie: "#8E44AD", Po: "#D35400", Sage: "#7F8C8D", Admin: "#C0392B" }, "标识颜色没有保存");
            Require(Brush("Accent") == themeAccent, "改标识颜色影响了强调色");
            var identityContrast = new Dictionary<string, double>();
            foreach (var mode in new[] { "light", "dark" })
            {
                await Theme(settings, mode);
                foreach (var (fg, bg) in new[] { ("CookieForeground", "CookieBackground"), ("AdminCookieForeground", "AdminCookieBackground"), ("OnSage", "SageBackground"), ("PoForeground", "PoBackground") })
                {
                    var ratio = Personalization.Contrast(Brush(fg), Brush(bg));
                    identityContrast[$"{mode}-{fg}"] = Math.Round(ratio, 2);
                    Require(ratio >= 4.5, $"{mode} {fg} 对比度不足：{ratio:0.00}");
                }
                // 预览与主窗口的身份标签使用同一资源
                var badge = All<Border>(settings).First(b => b.Name == "CookieBadge" && b.IsVisible);
                Require(((SolidColorBrush)badge.Background).Color == Brush("CookieBackground"), "预览标签没有同步颜色");
                foreach (var badgeInMain in All<Border>(main).Where(b => b.Name == "CookieBadge" && b.IsVisible))
                    Require(((SolidColorBrush)badgeInMain.Background).Color == Brush(badgeInMain.DataContext is Post { IsAdmin: true } ? "AdminCookieBackground" : "CookieBackground"), "主窗口身份标签没有同步颜色");
                capture(settings, Path.Combine(output, $"appearance-identity-{mode}.png"));
                capture(main, Path.Combine(output, $"main-identity-{mode}.png"));
            }
            report["IdentityContrast"] = identityContrast;
            report["MainWindowBadges"] = All<Border>(main).Count(b => b.Name == "CookieBadge" && b.IsVisible);
            Require((int)report["MainWindowBadges"] > 0, "主窗口没有可核对的身份标签");
            await Theme(settings, "light");
            Click(Named<Button>(settings, "PO颜色恢复默认")); await Pause();
            Require(store.Load().IdentityColors is { Po: "", Cookie: "#8E44AD" } && Brush("PoForeground") == Personalization.Rgb("#456481"), "单项恢复默认失败");
            Click(All<Button>(settings).Single(b => Equals(b.Content, "全部恢复默认"))); await Pause();
            Require(store.Load().IdentityColors is { Cookie: "", Admin: "", Sage: "", Po: "" } && Brush("CookieForeground") == themeCookie, "全部恢复默认失败");

            // 4. 再设置一组颜色，检查重启后（重新载入资料目录、重新套用主题）仍然有效
            Click(Named<Button>(settings, "强调色：玫红")); Named<TextBox>(settings, "饼干 ID颜色 HEX").Text = "#1F7A8C"; await Pause();
            var reloaded = new LocalStore(root).Load();
            Personalization.Load(new UserState()); App.ApplyTheme("light");
            Require(Brush("Accent") == themeAccent, "清空个性化后没有回到默认");
            Personalization.Load(reloaded); App.ApplyTheme("light");
            Require(Brush("Accent") == Personalization.Rgb("#A3456B") && Brush("CookieForeground") != themeCookie, "重启后个性化颜色失效");

            // 5. 内容与图片
            await Select("内容与图片");
            var list = Named<ComboBox>(settings, "串列表缩略图"); var reader = Named<ComboBox>(settings, "串内图片");
            Require(list.SelectedIndex == 0 && reader.SelectedIndex == 0, "默认图片模式不是自动加载");
            list.SelectedIndex = 2; reader.SelectedIndex = 1; await Pause();
            var images = store.Load();
            Require(images is { ListImages: ImageModes.Hidden, ReaderImages: ImageModes.Click } && vm.ListImagesHidden && !vm.ListImagesAuto && !vm.ReaderImagesAuto, "图片模式没有保存");
            capture(settings, Path.Combine(output, "content-images-light.png"));
            list.SelectedIndex = 0; reader.SelectedIndex = 0; await Pause();
            Require(vm.ListImagesAuto && vm.ReaderImagesAuto, "图片模式不能切回自动");

            // 6. 缓存与个人数据隔离
            await Select("缓存"); await Wait(() => All<Button>(settings).Single(b => Equals(b.Content, "刷新占用")).IsEnabled);
            store.Cache.Write("images/" + new string('D', 64) + ".bin", new byte[4096]);
            Click(All<Button>(settings).Single(b => Equals(b.Content, "清理缓存"))); await Wait(() => store.Cache.SizeBytes() == 0);
            var after = new LocalStore(root).Load();
            Require(after.CustomEmoticons.Single().Id == "keep" && after.Drafts.Single().Content == "保留的草稿" && after.Cookies.Single().Remark == "备注保留"
                && after.AccentColor == "#A3456B" && after.IdentityColors.Cookie == "#1F7A8C", "清理缓存删除了个人数据或设置");
            await Select("数据与记录");
            Require(All<TextBlock>(settings).Any(t => t.IsVisible && t.Text.Contains("自定义颜文字 1 个")), "数据与记录没有显示个人数据统计");
            capture(settings, Path.Combine(output, "data-light.png"));

            // 7. 模拟 100/125/150% DPI 下外观页（标识颜色展开）的布局
            await Select("外观");
            var scroll = Named<ScrollViewer>(settings, "设置内容");
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                SetDpi(settings, scale, 660, 520); await Pause(200);
                Require(scroll.ExtentWidth <= scroll.ViewportWidth + 1, $"{scale:P0} 外观页横向溢出");
                capture(settings, Path.Combine(output, $"appearance-dpi-{(int)(scale * 100)}.png"));
            }
            SetDpi(settings, 1, SettingsLayout.Width, SettingsLayout.Height); await Pause();
            await Theme(settings, "dark"); capture(settings, Path.Combine(output, "appearance-custom-dark.png")); await Theme(settings, "light");
        }
        finally
        {
            settings.Close(); main.Close();
            Personalization.Load(new UserState()); App.ApplyTheme("light");
        }

        // 8. 旧资料兼容：没有新字段时沿用旧的图片总开关，颜色为默认；格式错误的颜色回到默认
        var legacy = Path.Combine(output, "legacy-profile"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "state.json"), """{ "Theme": "dark", "ShowImages": false, "FontSize": 18 }""");
        var old = new LocalStore(legacy).Load();
        Require(old.AccentColor == "" && old.IdentityColors is { Cookie: "", Po: "" } && ImageModes.List(old) == ImageModes.Click && ImageModes.Reader(old) == ImageModes.Click && old.FontSize == 18 && old.Theme == "dark", "旧资料不兼容");
        File.WriteAllText(Path.Combine(legacy, "state.json"), """{ "AccentColor": "red", "IdentityColors": { "Cookie": "#12345", "Po": "abcdef" }, "ListImages": "bogus" }""");
        var odd = new LocalStore(legacy).Load();
        Require(odd.AccentColor == "" && odd.IdentityColors is { Cookie: "", Po: "#ABCDEF" } && odd.ListImages == "" && ImageModes.List(odd) == ImageModes.Auto, "格式错误的设置没有回到默认");

        // 9. PostImage：不显示、点击后就地加载、再次点击才打开原图；快速滚过不发请求
        var post = new Post { Id = 901, ImagePath = "fixture/a", Extension = ".png" };
        var bitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new byte[64], 16);
        var calls = 0; var host = new Window { Width = 300, Height = 300, Owner = owner, ShowInTaskbar = false };
        var hidden = new PostImage((_, _) => { calls++; return Task.FromResult(bitmap); }) { Post = post, Suppressed = true };
        host.Content = hidden; host.Show(); await Pause();
        Require(hidden.Visibility == Visibility.Collapsed && calls == 0, "不显示模式仍占位或请求图片");
        var opened = 0; void Opened(PostImage _, Post __) => opened++;
        PostImage.ImageClicked += Opened;
        try
        {
            var manual = new PostImage((_, _) => { calls++; return Task.FromResult(bitmap); }) { Post = post, ShowImages = false };
            host.Content = manual; await Pause();
            var button = (Button)manual.Content;
            Require(calls == 0 && Equals(button.Content, "显示这张图片"), "点击加载模式提前请求或没有入口");
            Click(button); await Pause();
            Require(calls == 1 && UiRefinementChecks.Descendants<Image>(manual).Any() && opened == 0, "点击后没有就地加载，或直接打开了看图窗口");
            Click((Button)manual.Content); await Pause();
            Require(opened == 1, "加载后再次点击没有打开看图窗口");
        }
        finally { PostImage.ImageClicked -= Opened; host.Close(); }
        using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30)))
        {
            // 非图片站地址：若真的发出请求会抛出 ArgumentException；在等待期内被取消则说明没有请求。
            var cancelled = false;
            try { await ImageService.LoadPreviewAsync("https://example.invalid/never.png", cts.Token); }
            catch (OperationCanceledException) { cancelled = true; }
            catch (ArgumentException) { }
            Require(cancelled, "滚动经过的图片没有延迟请求");
        }
        File.WriteAllText(Path.Combine(output, "settings-personalization.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS settings personalization: seven categories, accent presets/custom/HSL/reset with contrast in light+dark, identity colors per item/all reset with contrast and badge sync, persistence after reload, image modes, cache clear keeps personal data, legacy/invalid state, PostImage hidden/click-to-load/settle delay, DPI.");
        Console.WriteLine(JsonSerializer.Serialize(report));

        async Task Theme(SettingsWindow window, string mode)
        {
            var nav = Named<ListBox>(window, "设置分类"); var previous = nav.SelectedItem;
            nav.SelectedItem = "外观"; await Pause();
            Named<ComboBox>(window, "外观主题").SelectedIndex = mode == "dark" ? 1 : 0; await Pause();
            nav.SelectedItem = previous; await Pause();
        }
    }

    // ResourceDictionary.Contains 会连合并字典一起查，这里只看应用层的覆盖项。
    private static bool Overridden(string key) => Application.Current.Resources.Keys.Cast<object>().Contains(key);
    private static Color Brush(string key) => ((SolidColorBrush)Application.Current.FindResource(key)).Color;
    private static void Keyboard(TextBox box) => box.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(box)!, 0, System.Windows.Input.Key.Enter) { RoutedEvent = UIElement.KeyDownEvent });
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => All<T>(root).Single(c => AutomationProperties.GetName(c) == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static Task Pause(int ms = 120) => Task.Delay(ms);
    private static async Task Wait(Func<bool> ready) { for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(30); Require(ready(), "操作超时"); }
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

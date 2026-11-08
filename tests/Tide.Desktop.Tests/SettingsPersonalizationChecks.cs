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
        // 主题文件里的默认标签颜色就是默认主色按算法生成的结果（浅色、深色都核对）。
        foreach (var dark in new[] { false, true })
        {
            App.ApplyTheme(dark ? "dark" : "light");
            foreach (var (identity, fg, bg) in new[] { (Personalization.Identity.Cookie, "CookieForeground", "CookieBackground"), (Personalization.Identity.Admin, "AdminCookieForeground", "AdminCookieBackground"), (Personalization.Identity.Sage, "OnSage", "SageBackground"), (Personalization.Identity.Po, "PoForeground", "PoBackground") })
            {
                var (efg, ebg) = Personalization.IdentityPalette(Personalization.DefaultIdentity(identity), dark);
                Require(Brush(fg) == efg && Brush(bg) == ebg, $"{(dark ? "深色" : "浅色")} {fg} 默认值与默认主色生成的颜色不一致：{Personalization.Hex(Brush(fg))}/{Personalization.Hex(Brush(bg))}，应为 {Personalization.Hex(efg)}/{Personalization.Hex(ebg)}");
            }
        }
        App.ApplyTheme("light");
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
            Require(All<ColorField>(settings).All(f => !f.Popup.IsOpen), "调色板默认就打开了");
            var previewBox = Named<Border>(settings, "效果预览");
            Require(!previewBox.IsHitTestVisible && previewBox.BorderThickness.Left == 1 && All<TextBlock>(previewBox).Any(t => t.Text == "效果预览")
                && !All<Control>(previewBox).Any(c => c.Focusable), "效果预览没有独立的区域，或仍可点击、获得焦点");
            var toggles = All<ToggleButton>(previewBox).Where(t => t is not CheckBox).ToList();
            Require(toggles.Count == 2 && !Equals(toggles[0].Background, toggles[1].Background) && All<CheckBox>(previewBox).Count(c => c.IsChecked == true) == 1, "效果预览没有区分选中与未选中");
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
            // 调色板：点击「调色」弹出；色域、色相条、HEX、预设、恢复默认双向同步
            var accentField = Field(settings, "自定义强调色");
            Click(Named<Button>(settings, "自定义强调色调色")); await Pause();
            var picker = accentField.Picker;
            Require(accentField.Popup.IsOpen && picker.Area.ActualWidth >= ColorPicker.AreaWidth - .5 && picker.HueBar.ActualWidth >= ColorPicker.AreaWidth - .5, "调色板没有打开或色域、色相条没有正常尺寸");
            Require(picker.Hex == "#E6B800", "调色板没有显示当前颜色");
            var popupRect = ScreenRect((FrameworkElement)accentField.Popup.Child); var windowRect = ScreenRect(settings);
            Require(popupRect.Top >= windowRect.Top - 1 && popupRect.Bottom <= windowRect.Bottom + 1 && popupRect.Left >= windowRect.Left - 1 && popupRect.Right <= windowRect.Right + 1, $"调色板超出设置窗口：{popupRect} / {windowRect}");
            picker.PickHue(0); picker.PickArea(new(ColorPicker.AreaWidth, 0)); picker.Flush(); await Pause();
            Require(vm.State.AccentColor == "#FF0000" && hex.Text == "#FF0000", "色域右上角不是纯红，或 HEX 没有同步");
            // 纯红在白底上对比度不够：实际显示会被加深，但用户选的原始值不变
            var effective = Named<TextBlock>(accentField.Picker, "自定义强调色实际颜色").Text;
            Require(effective != "#FF0000" && Brush("Accent") == Personalization.Rgb(effective) && store.Load().AccentColor == "#FF0000", "对比度调整覆盖了原始颜色，或实际显示与主题资源不一致");
            // 连续拖动：中途不立即保存，结束时提交最后位置
            foreach (var x in new[] { 200d, 160, 120, 80 }) picker.PickArea(new(x, 150 - x / 2));
            picker.Flush(); await Pause();
            var (_, ds, dv) = Personalization.ToHsv(Personalization.Rgb(vm.State.AccentColor));
            Require(Math.Abs(ds - 80 / ColorPicker.AreaWidth) < .02 && Math.Abs(dv - (1 - 110 / ColorPicker.AreaHeight)) < .02 && Brush("Accent") != themeAccent, "拖动后颜色不对");
            // HEX → 调色板
            hex.Text = "#3366CC"; await Pause();
            var (ph, ps, pv) = picker.Hsv;
            Require(picker.Hex == "#3366CC" && Math.Abs(ph - 220) < 1 && Math.Abs(ps - .75) < .01 && Math.Abs(pv - .8) < .01, "HEX 没有同步到调色板位置");
            // 灰色没有色相：保留原色相，色域不跳回红色
            hex.Text = "#808080"; await Pause();
            Require(Math.Abs(picker.Hsv.H - 220) < 1 && picker.Hex == "#808080", "灰色让色相跳动");
            // 色相条
            picker.PickHue(ColorPicker.AreaWidth / 3); picker.Flush(); await Pause();
            Require(vm.State.AccentColor == "#808080", "灰色时改色相不应改变颜色");
            picker.PickArea(new(ColorPicker.AreaWidth, 0)); picker.Flush(); await Pause();
            Require(Math.Abs(Personalization.ToHsv(Personalization.Rgb(vm.State.AccentColor)).H - 120) < 2, "色相条没有改变色域色相");
            capture(settings, Path.Combine(output, "appearance-picker-light.png"));
            // 预设 → 调色板
            Click(Named<Button>(settings, "强调色：靛紫")); await Pause();
            Require(picker.Hex == "#5B57A2" && hex.Text == "#5B57A2", "预设没有同步到调色板");
            if (Environment.GetCommandLineArgs().Contains("--real-mouse")) report["RealMouse"] = await RealMouse(settings, accentField, vm, output);
            accentField.Popup.IsOpen = false; await Pause();
            // 恢复默认
            Click(Named<Button>(settings, "自定义强调色恢复默认")); await Pause();
            Require(vm.State.AccentColor == "" && !Overridden("Accent") && Brush("Accent") == themeAccent && picker.Hex == "#286D59" && hex.Text == "#286D59", "恢复默认强调色失败，或调色板、HEX 没有复位");

            // 3. 标识颜色：单项设置、主色变化不影响强调色、单项与全部恢复
            expander.IsExpanded = true; await Pause();
            Named<TextBox>(settings, "饼干 ID颜色 HEX").Text = "#8E44AD"; await Pause();
            Named<TextBox>(settings, "PO颜色 HEX").Text = "#D35400"; await Pause();
            Named<TextBox>(settings, "SAGE颜色 HEX").Text = "#7F8C8D"; await Pause();
            Named<TextBox>(settings, "Admin（红名）颜色 HEX").Text = "#C0392B"; await Pause();
            // 标识颜色用同一个调色板，各自独立
            var cookieField = Field(settings, "饼干 ID颜色");
            Click(Named<Button>(settings, "饼干 ID颜色调色")); await Pause();
            Require(cookieField.Popup.IsOpen && cookieField.Picker.Hex == "#8E44AD", "饼干 ID 调色板没有打开或颜色不对");
            var adminBefore = Brush("AdminCookieForeground");
            cookieField.Picker.PickArea(new(ColorPicker.AreaWidth * .8, ColorPicker.AreaHeight * .3)); cookieField.Picker.Flush(); await Pause();
            Require(vm.State.IdentityColors.Cookie != "#8E44AD" && Brush("AdminCookieForeground") == adminBefore && Brush("Accent") == themeAccent, "标识调色板影响了其他颜色");
            capture(settings, Path.Combine(output, "identity-picker-light.png"));
            cookieField.Popup.IsOpen = false;
            Named<TextBox>(settings, "饼干 ID颜色 HEX").Text = "#8E44AD"; await Pause();
            Require(cookieField.Picker.Hex == "#8E44AD", "HEX 没有同步到标识调色板");
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
                    // 深色主题不能把用户选的颜色洗成接近灰的淡色（0.3.10 的问题）：文字和底色都要保留明显的饱和度。
                    var chosen = fg switch { "CookieForeground" => vm.State.IdentityColors.Cookie, "AdminCookieForeground" => vm.State.IdentityColors.Admin, "OnSage" => vm.State.IdentityColors.Sage, _ => vm.State.IdentityColors.Po };
                    var chosenS = Personalization.ToHsl(Personalization.Rgb(chosen)).S;
                    if (mode == "dark") Require(Personalization.ToHsl(Brush(fg)).S >= Math.Min(chosenS, .9) * .85 && Personalization.ToHsl(Brush(bg)).S >= chosenS * .45, $"深色 {fg} 颜色发灰：所选 {chosen}，生成 {Personalization.Hex(Brush(fg))} / {Personalization.Hex(Brush(bg))}");
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
            Require(store.Load().IdentityColors is { Po: "", Cookie: "#8E44AD" } && Brush("PoForeground") == Personalization.Rgb("#0068C9"), "单项恢复默认失败");
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
        Console.WriteLine("PASS settings personalization: seven categories, accent presets/custom/2D picker sync/reset, preview area with contrast in light+dark, identity colors per item/all reset with contrast and badge sync, persistence after reload, image modes, cache clear keeps personal data, legacy/invalid state, PostImage hidden/click-to-load/settle delay, DPI.");
        Console.WriteLine(JsonSerializer.Serialize(report));

        async Task Theme(SettingsWindow window, string mode)
        {
            var nav = Named<ListBox>(window, "设置分类"); var previous = nav.SelectedItem;
            nav.SelectedItem = "外观"; await Pause();
            Named<ComboBox>(window, "外观主题").SelectedIndex = mode == "dark" ? 1 : 0; await Pause();
            nav.SelectedItem = previous; await Pause();
        }
    }

    private static ColorField Field(Window window, string name) => All<ColorField>(window).Single(f => AutomationProperties.GetName(f.Picker) == name + "调色板");
    private static Rect ScreenRect(FrameworkElement element) => new(element.PointToScreen(new Point()), element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));

    // 真实鼠标：移动系统光标，在色域里点击、按住拖动、拖出边界，再点击和拖动色相条。只在显式加 --real-mouse 时运行，结束后光标放回原处。
    private static async Task<object> RealMouse(SettingsWindow settings, ColorField field, MainViewModel vm, string output)
    {
        // 系统的前台锁定会拒绝直接切换；与 --real-ime 相同，临时挂到当前前台线程的输入队列上再切换。
        var handle = new WindowInteropHelper(settings).Handle;
        var foreignThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero); var ownThread = GetCurrentThreadId();
        if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, true);
        try { BringWindowToTop(handle); SetForegroundWindow(handle); }
        finally { if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, false); }
        settings.Activate(); await Pause(300);
        if (!field.Popup.IsOpen) { field.Popup.IsOpen = true; await Pause(200); }
        GetCursorPos(out var original);
        var picker = field.Picker;
        Point At(FrameworkElement element, double fx, double fy) => element.PointToScreen(new Point(element.ActualWidth * fx, element.ActualHeight * fy));
        void Move(Point p) => SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
        void Down() => mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        void Up() => mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        try
        {
            Require(GetForegroundWindow() == new WindowInteropHelper(settings).Handle, "设置窗口不在前台，放弃真实鼠标操作");
            // 单击色域 70% / 30%
            Move(At(picker.Area, .7, .3)); await Pause(80); Down(); await Pause(60); Up(); await Pause(200);
            var click = vm.State.AccentColor; var (_, cs, cv) = picker.Hsv;
            Require(Math.Abs(cs - .7) < .03 && Math.Abs(cv - .7) < .03 && click == picker.Hex, $"真实点击色域没有选到对应位置：{cs:0.00}/{cv:0.00} {click}");
            // 按住从 20%/20% 拖到 90%/80%，途中记录颜色，检查连续变化
            Move(At(picker.Area, .2, .2)); await Pause(60); Down(); await Pause(60);
            var trail = new List<string>();
            for (var i = 1; i <= 10; i++) { Move(At(picker.Area, .2 + .07 * i, .2 + .06 * i)); await Pause(35); trail.Add(picker.Hex); }
            ScreenShot(settings, field, Path.Combine(output, "real-mouse-dragging.png"));
            Up(); await Pause(200);
            var (_, ds, dv) = picker.Hsv;
            Require(Math.Abs(ds - .9) < .03 && Math.Abs(dv - .2) < .03 && vm.State.AccentColor == picker.Hex && trail.Distinct().Count() >= 8, $"真实拖动没有连续更新：{ds:0.00}/{dv:0.00} {string.Join(",", trail)}");
            // 拖出色域：停在边缘
            Move(At(picker.Area, .5, .5)); await Pause(40); Down(); await Pause(40); Move(At(picker.Area, 1.5, -.5)); await Pause(80); Up(); await Pause(150);
            Require(picker.Hsv.S == 1 && picker.Hsv.V == 1, "拖出色域后没有停在边缘");
            // 色相条：点击三分之二处（约 240°）
            Move(At(picker.HueBar, 2d / 3, .5)); await Pause(60); Down(); await Pause(40); Up(); await Pause(200);
            Require(Math.Abs(picker.Hsv.H - 240) < 4 && Math.Abs(Personalization.ToHsv(Personalization.Rgb(vm.State.AccentColor)).H - 240) < 4, $"真实点击色相条没有生效：{picker.Hsv.H:0}");
            // 色相条拖动到约 60°
            Down(); await Pause(40);
            for (var i = 1; i <= 5; i++) { Move(At(picker.HueBar, 2d / 3 - i * .1, .5)); await Pause(35); }
            Up(); await Pause(200);
            Require(Math.Abs(picker.Hsv.H - 60) < 6, $"真实拖动色相条没有生效：{picker.Hsv.H:0}");
            Require(field.Popup.IsOpen, "操作中调色板被意外关闭");
            ScreenShot(settings, field, Path.Combine(output, "real-mouse-after.png"));
            // 点击调色板外部（设置页空白处）关闭
            Move(At(settings, .5, .06)); await Pause(60); Down(); await Pause(40); Up(); await Pause(200);
            Require(!field.Popup.IsOpen, "点击外部没有关闭调色板");
            return new { Click = click, DragTrail = trail, Final = vm.State.AccentColor, Hue = Math.Round(picker.Hsv.H) };
        }
        finally { SetCursorPos(original.X, original.Y); }
    }
    // 设置窗口和弹出的调色板是两个窗口，按屏幕区域截图才能拍到一起。
    private static void ScreenShot(Window window, ColorField field, string path)
    {
        var area = ScreenRect(window);
        if (field.Popup.IsOpen) area.Union(ScreenRect((FrameworkElement)field.Popup.Child));
        var w = (int)Math.Ceiling(area.Width); var h = (int)Math.Ceiling(area.Height);
        var dc = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, w, h); var old = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, w, h, dc, (int)area.Left, (int)area.Top, 0x00CC0020);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, dc); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, IntPtr process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool value);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

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

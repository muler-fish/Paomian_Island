using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Tide.Core;

namespace Tide.Desktop;

public sealed class SettingsWindow : DialogWindow
{
    public SettingsWindow(MainViewModel vm) : base("设置")
    {
        Height = 760; Width = UiMetrics.DialogWidth;
        StackPanel Section(string title)
        {
            var group = new StackPanel { Margin = new(0, 12, 0, 20) };
            var heading = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new(0, 8, 0, 12) };
            var border = new Border { BorderThickness = new(0, Body.Children.Count == 0 ? 0 : 1, 0, 0), Child = group };
            border.SetResourceReference(Border.BorderBrushProperty, "Line"); group.Children.Add(heading); Body.Children.Add(border); return group;
        }
        void Toggle(StackPanel group, string title, bool value, Action<bool> save, string? hint = null)
        {
            var box = new CheckBox { Content = title, IsChecked = value, Margin = new(0, 6, 0, 6), ToolTip = hint };
            System.Windows.Automation.AutomationProperties.SetName(box, title);
            void Changed(object sender, RoutedEventArgs e) => save(box.IsChecked == true);
            box.Checked += Changed; box.Unchecked += Changed; group.Children.Add(box);
        }
        var reading = Section("阅读");
        var current = Hint($"正文字号（含串列表预览） · {vm.State.FontSize:0} px"); reading.Children.Add(current);
        var size = new Slider { Minimum = 13, Maximum = 24, TickFrequency = 1, IsSnapToTickEnabled = true, Value = vm.State.FontSize, Margin = new(0, 10, 0, 10) };
        size.ValueChanged += (_, _) => { vm.State.FontSize = size.Value; current.Text = $"正文字号（含串列表预览） · {size.Value:0} px"; vm.AppearanceChanged(); }; reading.Children.Add(size);
        void Spacing(string label, double value, double min, double max, double step, string suffix, Action<double> save)
        {
            var hint = Hint($"{label} · {value:0.##}{suffix}"); reading.Children.Add(hint);
            var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Value = value, Margin = new(0, 6, 0, 10) };
            System.Windows.Automation.AutomationProperties.SetName(slider, label);
            slider.ValueChanged += (_, _) => { hint.Text = $"{label} · {slider.Value:0.##}{suffix}"; save(slider.Value); vm.AppearanceChanged(); };
            reading.Children.Add(slider);
        }
        Spacing("行间距", vm.State.ReadingLineHeight, 1.3, 2.5, .05, " 倍", value => vm.State.ReadingLineHeight = value);
        Spacing("字间距", vm.State.ReadingLetterSpacing, 0, 3, .25, " px", value => vm.State.ReadingLetterSpacing = value);
        var sample = new PostPreview { Html = "正文预览 Aa 123", Margin = new(0, 8, 0, 12) };
        sample.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding(nameof(MainViewModel.ReadingFontSize)) { Source = vm }); reading.Children.Add(sample);
        Toggle(reading, "启用防剧透", vm.State.HideSpoilers, value => { vm.State.HideSpoilers = value; vm.AppearanceChanged(); }, "黑条保留原文排版，串内点击展开。");

        var appearance = Section("外观");
        var themes = new ComboBox { ItemsSource = new[] { "浅色", "深色" }, SelectedIndex = vm.State.Theme == "dark" ? 1 : 0, MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Left };
        System.Windows.Automation.AutomationProperties.SetName(themes, "外观主题");
        themes.SelectionChanged += (_, _) => { vm.State.Theme = themes.SelectedIndex == 1 ? "dark" : "light"; App.ApplyTheme(vm.State.Theme); vm.AppearanceChanged(); };
        appearance.Children.Add(themes);
        Toggle(appearance, "自动加载正文缩略图", vm.State.ShowImages, value => { vm.State.ShowImages = value; vm.AppearanceChanged(); });

        var network = Section("网络");
        void Route(string title, bool proxy, Action<bool> save)
        {
            var row = new Grid { Margin = new(0, 6, 0, 6) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(160) });
            row.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
            var choices = new ComboBox { ItemsSource = new[] { "系统代理", "直连" }, SelectedIndex = proxy ? 0 : 1 };
            System.Windows.Automation.AutomationProperties.SetName(choices, title);
            choices.SelectionChanged += (_, _) => save(choices.SelectedIndex == 0); Grid.SetColumn(choices, 1); row.Children.Add(choices); network.Children.Add(row);
        }
        Route("访问 X 岛", vm.State.IslandUseProxy, value => { vm.State.IslandUseProxy = NetworkClients.IslandUseProxy = value; vm.Save(); });
        Route("检查与下载更新", vm.State.UpdateUseProxy, value => { vm.State.UpdateUseProxy = NetworkClients.UpdateUseProxy = value; vm.Save(); });
        network.Children.Add(Hint("分别应用于后续请求，无需重启。直连跳过系统 HTTP 代理；全局 VPN / TUN 仍由系统控制。"));

        var cache = Section("缓存");
        var usage = Hint("正在计算占用…"); cache.Children.Add(usage);
        var path = new TextBox { Text = vm.Store.Cache.Root, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 10) }; cache.Children.Add(path);
        var limitRow = new WrapPanel { Margin = new(0, 6, 0, 10) };
        limitRow.Children.Add(new TextBlock { Text = "容量上限", VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 12, 0) });
        var limit = new TextBox { Text = vm.State.CacheLimitMb.ToString(), Width = 90, VerticalContentAlignment = VerticalAlignment.Center };
        limit.GotKeyboardFocus += (_, _) => limit.SelectAll(); limitRow.Children.Add(limit);
        limitRow.Children.Add(new TextBlock { Text = "MB", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) });
        cache.Children.Add(limitRow);
        async Task Measure()
        {
            var bytes = await Task.Run(vm.Store.Cache.SizeBytes);
            usage.Text = $"磁盘占用 {bytes / 1_000_000d:0.00} MB / {vm.State.CacheLimitMb} MB";
            path.Text = vm.Store.Cache.Root;
        }
        async Task CacheAction(Func<Task> action)
        {
            cache.IsEnabled = false;
            try { await action(); await Measure(); }
            catch (Exception ex) { usage.Text = "缓存操作未完成：" + ex.Message; }
            finally { cache.IsEnabled = true; }
        }
        limitRow.Children.Add(Button("应用上限", async () => await CacheAction(async () =>
        {
            if (!int.TryParse(limit.Text, out var mb) || mb is < 0 or > 4096) throw new ArgumentException("请输入 0–4096 MB；0 表示不保留磁盘缓存。");
            await Task.Run(() => vm.Store.Cache.Configure(vm.State.CacheParentPath, mb));
            vm.State.CacheLimitMb = mb; vm.Save();
        })));
        var cacheButtons = new WrapPanel(); cache.Children.Add(cacheButtons);
        cacheButtons.Children.Add(Button("刷新占用", async () => await CacheAction(() => Task.CompletedTask)));
        cacheButtons.Children.Add(Button("清理缓存", async () => await CacheAction(async () => { await Task.Run(vm.Store.Cache.Clear); vm.ClearTemporaryCache(); })));
        cacheButtons.Children.Add(Button("更改路径", async () =>
        {
            var picker = new OpenFolderDialog { Title = "选择缓存存放位置", Multiselect = false };
            if (picker.ShowDialog(this) != true) return;
            await CacheAction(async () =>
            {
                await Task.Run(() => vm.Store.Cache.Configure(picker.FolderName, vm.State.CacheLimitMb));
                vm.State.CacheParentPath = picker.FolderName; vm.Save();
            });
        }));
        cacheButtons.Children.Add(Button("恢复默认路径", async () => await CacheAction(async () =>
        {
            await Task.Run(() => vm.Store.Cache.Configure("", vm.State.CacheLimitMb)); vm.State.CacheParentPath = ""; vm.Save();
        })));
        cache.Children.Add(Hint("包含正文页、订阅列表快照和图片。超过上限时淘汰较久未使用的缓存；更改路径会迁移现有缓存。清理不影响饼干、订阅号、草稿、阅读历史和本机发言。"));
        Loaded += async (_, _) => await CacheAction(() => Task.CompletedTask);

        var data = Section("账号与本机记录");
        var dataButtons = new WrapPanel(); data.Children.Add(dataButtons);
        dataButtons.Children.Add(Button("管理订阅号", async () => { new SubscriptionWindow(vm) { Owner = this }.ShowDialog(); await vm.LoadSubscriptionsAsync(); }));
        dataButtons.Children.Add(Button("屏蔽管理", () => new BlockedThreadsWindow(vm) { Owner = this }.ShowDialog()));
        data.Children.Add(Hint($"历史 {vm.State.History.Count} 个串 · 草稿 {vm.State.Drafts.Count} 份"));
        data.Children.Add(Hint("个人资料目录：" + vm.Store.Root));
        data.Children.Add(Button("打开资料目录", () => Open(vm.Store.Root)));

        var about = Section("关于");
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 0, 8) };
        var logo = new Image { Width = 48, Height = 48, Margin = new(0, 0, 12, 0) };
        logo.SetResourceReference(Image.SourceProperty, "AppLogo");
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(logo, System.Windows.Media.BitmapScalingMode.HighQuality);
        brand.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, FontSize = 24, FontWeight = FontWeights.SemiBold });
        names.Children.Add(Hint(AppIdentity.EnglishName)); brand.Children.Add(names); about.Children.Add(brand);
        about.Children.Add(Hint("版本 " + UpdateController.Version + " · X 岛匿名版非官方 Windows 客户端"));
        var links = new WrapPanel(); about.Children.Add(links);
        links.Children.Add(Button("作者 · muler-fish", () => Open("https://github.com/muler-fish")));
        links.Children.Add(Button("项目仓库", () => Open(ReleaseUpdater.Repository)));
        links.Children.Add(Button("反馈问题", () => Open(ReleaseUpdater.Repository + "/issues")));
        about.Children.Add(Hint("使用 MiSans 字体。第三方组件及字体许可见程序目录下的 licenses 文件夹。"));
        var updateStatus = Hint(vm.Updates.Status); about.Children.Add(updateStatus);
        var updateButtons = new WrapPanel(); about.Children.Add(updateButtons);
        var check = Button("检查更新", async () => await vm.Updates.CheckAsync()); updateButtons.Children.Add(check);
        var download = Button("下载更新", async () => await vm.Updates.DownloadAsync()); updateButtons.Children.Add(download);
        var install = Button("重启安装", vm.Updates.RestartToInstall); updateButtons.Children.Add(install);
        void UpdateState()
        {
            updateStatus.Text = vm.Updates.Status;
            check.IsEnabled = !vm.Updates.Busy && vm.Updates.Staged is null;
            download.Visibility = vm.Updates.Available is not null && vm.Updates.Staged is null ? Visibility.Visible : Visibility.Collapsed;
            download.IsEnabled = !vm.Updates.Busy;
            install.Visibility = vm.Updates.Staged is null ? Visibility.Collapsed : Visibility.Visible;
        }
        vm.Updates.Changed += UpdateState; Closed += (_, _) => vm.Updates.Changed -= UpdateState; UpdateState();
        Toggle(about, "启动时自动检查更新", vm.State.AutoCheckUpdates, value => { vm.State.AutoCheckUpdates = value; vm.Save(); });
        Toggle(about, "自动下载，退出时安装更新", vm.State.InstallUpdatesOnExit, async value =>
        {
            vm.State.InstallUpdatesOnExit = value; vm.Save();
            if (value) await vm.Updates.CheckAsync(automatic: true);
        });
        about.Children.Add(Hint("仅检查 GitHub 正式版，下载后校验文件。更新网络可在上方“网络”中单独设置。"));
        AddAction("完成", Close, true);
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

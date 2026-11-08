using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Tide.Core;

namespace Tide.Desktop;

public sealed class SettingsWindow : DialogWindow
{
    public SettingsWindow(MainViewModel vm) : base("设置")
    {
        Width = SettingsLayout.Width; Height = SettingsLayout.Height; MinWidth = 640; MinHeight = 420;
        WindowPlacement.ConstrainToWorkArea(this);
        var layout = new Grid { Margin = new(24, 0, 0, 0) };
        layout.ColumnDefinitions.Add(new() { Width = new(SettingsLayout.NavigationWidth) }); layout.ColumnDefinitions.Add(new());
        var navigation = new ListBox { Margin = new(0, 0, 12, 0) };
        AutomationProperties.SetName(navigation, "设置分类");
        navigation.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "SettingsNavigationItem"); layout.Children.Add(navigation);
        var pages = new Grid();
        var scroll = new ScrollViewer { Content = pages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(scroll, "设置内容"); UseReadingScroll(scroll);
        var divider = new Border { Child = scroll, BorderThickness = new(1, 0, 0, 0) }; divider.SetResourceReference(Border.BorderBrushProperty, "Line");
        Grid.SetColumn(divider, 1); layout.Children.Add(divider); SetBodyContent(layout);
        var categories = new List<StackPanel>();
        StackPanel Page(string title)
        {
            var page = SettingsLayout.Page(title); categories.Add(page); pages.Children.Add(page);
            navigation.Items.Add(title); return page;
        }
        // Keep controls alive: switching categories must not recreate edits or re-run saves.
        navigation.SelectionChanged += (_, _) =>
        {
            for (var i = 0; i < categories.Count; i++) categories[i].Visibility = i == navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
            scroll.ScrollToTop();
        };
        Button Action(string label, Action action)
        {
            var button = Button(label, action); button.Margin = new(0, 0, 8, 6); return button;
        }
        void Toggle(StackPanel page, string title, bool value, Action<bool> save, string? hint = null)
        {
            var box = new CheckBox { IsChecked = value, ToolTip = hint, Margin = new(0), MinWidth = 24 };
            void Changed(object sender, RoutedEventArgs e) => save(box.IsChecked == true);
            box.Checked += Changed; box.Unchecked += Changed;
            SettingsLayout.Row(page, title, hint, box);
        }

        var reading = Page("阅读");
        SettingsLayout.Slider(reading, "正文字号", "含串列表预览", vm.State.FontSize, 13, 24, 1, " px", value => { vm.State.FontSize = value; vm.AppearanceChanged(); });
        SettingsLayout.Slider(reading, "行间距", null, vm.State.ReadingLineHeight, 1.3, 2.5, .05, " 倍", value => { vm.State.ReadingLineHeight = value; vm.AppearanceChanged(); });
        SettingsLayout.Slider(reading, "字间距", null, vm.State.ReadingLetterSpacing, 0, 3, .25, " px", value => { vm.State.ReadingLetterSpacing = value; vm.AppearanceChanged(); });
        var preview = new StackPanel(); preview.Children.Add(SettingsLayout.Text("阅读效果预览", true));
        var sample = new PostPreview { Html = "留一点空白，让阅读慢下来。<br>中文  Aa 123  ( ´ ▽ ` )ﾉ", Margin = new(0, 8, 0, 0) };
        sample.SetBinding(TextBlock.FontSizeProperty, new System.Windows.Data.Binding(nameof(MainViewModel.ReadingFontSize)) { Source = vm });
        AutomationProperties.SetName(sample, "阅读效果预览"); preview.Children.Add(sample); SettingsLayout.Block(reading, preview);
        Toggle(reading, "启用防剧透", vm.State.HideSpoilers, value => { vm.State.HideSpoilers = value; vm.AppearanceChanged(); }, "黑条保留原文排版，串内点击展开。");

        var appearance = Page("外观");
        var themes = new ComboBox { ItemsSource = new[] { "浅色", "深色" }, SelectedIndex = vm.State.Theme == "dark" ? 1 : 0, Width = 140 };
        themes.SelectionChanged += (_, _) =>
        {
            vm.State.Theme = themes.SelectedIndex == 1 ? "dark" : "light"; App.ApplyTheme(vm.State.Theme); vm.AppearanceChanged();
            SettingsPersonalization.NotifyThemeChanged();
        };
        SettingsLayout.Row(appearance, "外观主题", "切换应用的浅色与深色模式。", themes);
        SettingsPersonalization.Accent(appearance, vm, (label, action) => Button(label, action));
        SettingsPersonalization.Identity(appearance, vm, (label, action) => Button(label, action));

        // 内容与图片：列表缩略图、串内图片分开设置；原图只在看图窗口按需加载。
        var content = Page("内容与图片");
        ComboBox Mode(string title, string hint, string[] labels, string[] values, string current, Action<string> save)
        {
            var choices = new ComboBox { ItemsSource = labels, SelectedIndex = Math.Max(0, Array.IndexOf(values, current)), Width = 140 };
            choices.SelectionChanged += (_, _) => save(values[choices.SelectedIndex]);
            SettingsLayout.Row(content, title, hint, choices); return choices;
        }
        void SaveImages(string list, string reader)
        {
            // 明确写入两项，此后不再沿用旧的总开关；总开关同步为串内图片的状态，供旧版本读取。
            vm.State.ListImages = list; vm.State.ReaderImages = reader; vm.State.ShowImages = reader == ImageModes.Auto; vm.AppearanceChanged();
        }
        Mode("串列表缩略图", "中栏每个串下方的小图。选择不显示时，进入串后仍可在正文中查看。",
            ["自动加载", "点击后加载", "不显示"], [ImageModes.Auto, ImageModes.Click, ImageModes.Hidden], ImageModes.List(vm.State),
            value => SaveImages(value, ImageModes.Reader(vm.State)));
        Mode("串内图片", "主楼、回复和引用预览中的图片。",
            ["自动加载", "点击后加载"], [ImageModes.Auto, ImageModes.Click], ImageModes.Reader(vm.State),
            value => SaveImages(ImageModes.List(vm.State), value));
        SettingsLayout.Block(content, SettingsLayout.Text("列表和正文显示的是站点缩略图，只在滚动到附近时才请求，快速滚过的条目不会下载。点击图片会打开看图窗口，这时才加载完整原图。加载失败的图片可以点击「重试」。图片走「网络」中访问 X 岛的线路，缓存计入「缓存」容量。", true), false);

        var network = Page("网络");
        void Route(string title, bool proxy, Action<bool> save)
        {
            var choices = new ComboBox { Width = 140, ItemsSource = new[] { "系统代理", "直连" }, SelectedIndex = proxy ? 0 : 1 };
            choices.SelectionChanged += (_, _) => save(choices.SelectedIndex == 0);
            SettingsLayout.Row(network, title, null, choices);
        }
        Route("访问 X 岛", vm.State.IslandUseProxy, value => { vm.State.IslandUseProxy = NetworkClients.IslandUseProxy = value; vm.Save(); });
        Route("检查与下载更新", vm.State.UpdateUseProxy, value => { vm.State.UpdateUseProxy = NetworkClients.UpdateUseProxy = value; vm.Save(); });
        SettingsLayout.Block(network, SettingsLayout.Text("分别应用于后续请求，无需重启。直连跳过系统 HTTP 代理；全局 VPN / TUN 仍由系统控制。", true), false);

        var cache = Page("缓存");
        var usage = SettingsLayout.Text("正在计算占用…"); AutomationProperties.SetName(usage, "缓存占用"); SettingsLayout.Block(cache, usage);
        var limitRow = new StackPanel(); limitRow.Children.Add(SettingsLayout.Text("容量上限"));
        var limitControls = new WrapPanel { Margin = new(0, 8, 0, 0) };
        var limit = new TextBox { Text = vm.State.CacheLimitMb.ToString(), Width = 90, VerticalContentAlignment = VerticalAlignment.Center, Margin = new(0, 0, 0, 6) };
        AutomationProperties.SetName(limit, "缓存容量上限"); limit.GotKeyboardFocus += (_, _) => limit.SelectAll(); limitControls.Children.Add(limit);
        limitControls.Children.Add(new TextBlock { Text = "MB", VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 16, 6) });
        limitRow.Children.Add(limitControls); limitRow.Children.Add(SettingsLayout.Text("范围 0–4096 MB；0 表示不保留磁盘缓存。", true)); SettingsLayout.Block(cache, limitRow);
        var pathRow = new StackPanel(); pathRow.Children.Add(SettingsLayout.Text("缓存存储路径"));
        var path = SettingsLayout.Path(vm.Store.Cache.Root, "缓存存储路径"); pathRow.Children.Add(path); SettingsLayout.Block(cache, pathRow);
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
        limitControls.Children.Add(Action("应用上限", async () => await CacheAction(async () =>
        {
            if (!int.TryParse(limit.Text, out var mb) || mb is < 0 or > 4096) throw new ArgumentException("请输入 0–4096 MB；0 表示不保留磁盘缓存。");
            await Task.Run(() => vm.Store.Cache.Configure(vm.State.CacheParentPath, mb)); vm.State.CacheLimitMb = mb; vm.Save();
        })));
        var pathButtons = new WrapPanel(); pathRow.Children.Add(pathButtons);
        pathButtons.Children.Add(Action("更改路径", async () =>
        {
            var picker = new OpenFolderDialog { Title = "选择缓存存放位置", Multiselect = false };
            if (picker.ShowDialog(this) != true) return;
            await CacheAction(async () => { await Task.Run(() => vm.Store.Cache.Configure(picker.FolderName, vm.State.CacheLimitMb)); vm.State.CacheParentPath = picker.FolderName; vm.Save(); });
        }));
        pathButtons.Children.Add(Action("恢复默认路径", async () => await CacheAction(async () =>
        {
            await Task.Run(() => vm.Store.Cache.Configure("", vm.State.CacheLimitMb)); vm.State.CacheParentPath = ""; vm.Save();
        })));
        var cacheManagement = new StackPanel(); var cacheButtons = new WrapPanel(); cacheManagement.Children.Add(cacheButtons);
        cacheButtons.Children.Add(Action("刷新占用", async () => await CacheAction(() => Task.CompletedTask)));
        var clear = Action("清理缓存", async () => await CacheAction(async () => { await Task.Run(vm.Store.Cache.Clear); vm.ClearTemporaryCache(); }));
        clear.SetResourceReference(Control.ForegroundProperty, "Danger"); cacheButtons.Children.Add(clear);
        cacheManagement.Children.Add(SettingsLayout.Text("包含正文页、订阅列表快照和图片。超过上限时淘汰较久未使用的缓存；更改路径会迁移现有缓存。", true));
        var clearHint = SettingsLayout.Text("清理只删除可以重新获取的缓存文件，不影响饼干及备注、订阅号、草稿、阅读历史、本机发言、自定义颜文字和各项设置（这些属于「数据与记录」）。", true); clearHint.Margin = new(0, 8, 0, 0); cacheManagement.Children.Add(clearHint);
        SettingsLayout.Block(cache, cacheManagement, false);
        Loaded += async (_, _) => await CacheAction(() => Task.CompletedTask);

        var data = Page("数据与记录");
        var dataHint = SettingsLayout.Text("这里是需要长期保留的个人数据，保存在个人资料目录的 state.json 等文件中；饼干和订阅号只在本机加密保存。「缓存」中的清理不会删除它们。", true);
        dataHint.Margin = new(0, 0, 0, 4); data.Children.Add(dataHint);
        SettingsLayout.Row(data, "订阅号", "管理订阅号、备注及当前使用的订阅号。", Action("管理订阅号", async () => { new SubscriptionWindow(vm) { Owner = this }.ShowDialog(); await vm.LoadSubscriptionsAsync(); }));
        SettingsLayout.Row(data, "屏蔽内容", "管理已屏蔽的串与板块。", Action("屏蔽管理", () => new BlockedThreadsWindow(vm) { Owner = this }.ShowDialog()));
        var records = SettingsLayout.Text(""); SettingsLayout.Block(data, records);
        void RefreshRecords() => records.Text = $"历史 {vm.State.History.Count} 个串 · 草稿 {vm.State.Drafts.Count} 份 · 饼干 {vm.State.Cookies.Count} 个 · 自定义颜文字 {vm.State.CustomEmoticons.Count} 个";
        navigation.SelectionChanged += (_, _) => RefreshRecords(); Activated += (_, _) => RefreshRecords(); RefreshRecords();
        var profile = new StackPanel(); profile.Children.Add(SettingsLayout.Text("个人资料目录")); profile.Children.Add(SettingsLayout.Path(vm.Store.Root, "个人资料目录"));
        var openProfile = Action("打开资料目录", () => Open(vm.Store.Root)); openProfile.HorizontalAlignment = HorizontalAlignment.Left; profile.Children.Add(openProfile); SettingsLayout.Block(data, profile, false);

        var about = Page("关于");
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 4, 0, 12) };
        var logo = new Image { Width = 48, Height = 48, Margin = new(0, 0, 12, 0) }; logo.SetResourceReference(Image.SourceProperty, "AppLogo");
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(logo, System.Windows.Media.BitmapScalingMode.HighQuality); brand.Children.Add(logo);
        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = AppIdentity.DisplayName, FontSize = 24, FontWeight = FontWeights.SemiBold }); names.Children.Add(SettingsLayout.Text(AppIdentity.EnglishName, true)); brand.Children.Add(names); about.Children.Add(brand);
        about.Children.Add(SettingsLayout.Text("版本 " + UpdateController.Version + " · X 岛匿名版非官方 Windows 客户端", true));
        var links = new WrapPanel { Margin = new(0, 12, 0, 4) }; about.Children.Add(links);
        links.Children.Add(Action("作者 · muler-fish", () => Open("https://github.com/muler-fish"))); links.Children.Add(Action("项目仓库", () => Open(ReleaseUpdater.Repository))); links.Children.Add(Action("反馈问题", () => Open(ReleaseUpdater.Repository + "/issues")));
        about.Children.Add(SettingsLayout.Text("优先使用 Windows 自带的微软雅黑，特殊字符由备用字体补足。第三方组件及随包字体许可见程序目录下的 licenses 文件夹。", true));
        SettingsLayout.Section(about, "软件更新");
        var updateStatus = SettingsLayout.Text(vm.Updates.Status, true); updateStatus.Margin = new(0, 8, 0, 12); about.Children.Add(updateStatus);
        var updateButtons = new WrapPanel(); about.Children.Add(updateButtons);
        var check = Action("检查更新", async () => await vm.Updates.CheckAsync()); updateButtons.Children.Add(check);
        var download = Action("下载更新", async () => await vm.Updates.DownloadAsync()); updateButtons.Children.Add(download);
        var install = Action("重启安装", vm.Updates.RestartToInstall); updateButtons.Children.Add(install);
        void UpdateState()
        {
            updateStatus.Text = vm.Updates.Status; check.IsEnabled = !vm.Updates.Busy && vm.Updates.Staged is null;
            download.Visibility = vm.Updates.Available is not null && vm.Updates.Staged is null ? Visibility.Visible : Visibility.Collapsed; download.IsEnabled = !vm.Updates.Busy;
            install.Visibility = vm.Updates.Staged is null ? Visibility.Collapsed : Visibility.Visible;
        }
        vm.Updates.Changed += UpdateState; Closed += (_, _) => vm.Updates.Changed -= UpdateState; UpdateState();
        Toggle(about, "启动时自动检查更新", vm.State.AutoCheckUpdates, value => { vm.State.AutoCheckUpdates = value; vm.Save(); });
        Toggle(about, "自动下载，退出时安装更新", vm.State.InstallUpdatesOnExit, async value => { vm.State.InstallUpdatesOnExit = value; vm.Save(); if (value) await vm.Updates.CheckAsync(automatic: true); });
        var updateHint = SettingsLayout.Text("仅检查 GitHub 正式版，下载后校验文件。更新网络可在“网络”分类中单独设置。", true); updateHint.Margin = new(0, 12, 0, 0); about.Children.Add(updateHint);
        navigation.SelectedIndex = 0;
        AddAction("完成", Close, true);
    }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>颜文字选择器打开耗时：首次与连续十次打开，分别记录同步构建＋布局时间和到界面空闲的总时间。只用隔离资料。</summary>
internal static class EmoticonPerfChecks
{
    internal static async Task RunAsync(MainWindow owner, string output)
    {
        var store = new LocalStore(Path.Combine(output, "perf-profile"));
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        vm.State.Forums = [new(4, "综合版1", "综合")];
        vm.State.Cookies = [new("test", "测试饼干", SecretProtection.Protect("synthetic-cookie"))]; vm.State.ActiveCookieId = "test";
        vm.State.CustomEmoticons = []; vm.State.HiddenEmoticons = []; vm.State.RecentEmoticons = [];
        for (var i = 0; i < 6; i++) EmoticonCatalog.Save(vm.State, null, $"自定义{i} (・∀・)" + new string('长', i * 8), i % 2 == 0 ? "" : $"备注{i}");
        var composer = new ComposerWindow(vm, 0) { Owner = owner, ShowInTaskbar = false };
        // --cold：窗口一显示就立即打开，不给空闲预排的机会，测最坏情况。
        var cold = Environment.GetCommandLineArgs().Contains("--cold");
        composer.Show(); if (!cold) await Task.Delay(300);
        var results = new List<object>();
        try
        {
            var menu = composer.Emoticons;
            // 测量打开「全部」分类；每次都先关闭，确保不是复用已打开的弹出层。
            for (var i = 0; i < 11; i++)
            {
                var watch = Stopwatch.StartNew();
                menu.Toggle();
                var surface = (FrameworkElement)menu.Popup.Child;
                surface.UpdateLayout();
                var sync = watch.Elapsed.TotalMilliseconds;
                await composer.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var idle = watch.Elapsed.TotalMilliseconds;
                var buttons = UiRefinementChecks.Descendants<Button>(surface).Count(b => b.Tag is EmoticonEntry);
                results.Add(new { Run = i == 0 ? "first" : $"repeat{i}", SyncMs = Math.Round(sync, 1), IdleMs = Math.Round(idle, 1), Buttons = buttons, Elements = Count(surface) });
                Console.WriteLine($"{(i == 0 ? "first " : $"repeat{i,2}")}: sync {sync,7:F1} ms, idle {idle,7:F1} ms, item buttons {buttons}, elements {Count(surface)}");
                menu.Toggle(); await Task.Delay(120);
            }
            // 快速切换分类
            menu.Toggle(); await Task.Delay(150);
            var switchWatch = Stopwatch.StartNew();
            foreach (var tab in new[] { "最近使用", "全部", "自定义", "全部", "最近使用", "全部" })
                UiRefinementChecks.Descendants<RadioButton>(menu.Popup.Child).Single(r => Equals(r.Content, tab)).IsChecked = true;
            ((FrameworkElement)menu.Popup.Child).UpdateLayout();
            var switchMs = switchWatch.Elapsed.TotalMilliseconds;
            Console.WriteLine($"switch 6 categories: {switchMs:F1} ms");
            results.Add(new { Run = "switch6", SyncMs = Math.Round(switchMs, 1) });
            menu.Toggle();
        }
        finally { composer.Close(); }
        if (Environment.GetCommandLineArgs().Contains("--breakdown")) await Breakdown(owner);
        File.WriteAllText(Path.Combine(output, "emoticon-perf.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }

    // 分项测量：同样 203 个条目，逐项加入按钮样式、彩色 emoji 文字、「⋯」图标和右键菜单。
    private static async Task Breakdown(Window owner)
    {
        var entries = EmoticonCatalog.Entries.Take(32).ToList();
        var host = new Window { Owner = owner, Width = 700, Height = 500, ShowInTaskbar = false };
        host.Show(); await Task.Delay(200);
        var variants = new (string Name, Func<EmoticonEntry, FrameworkElement> Make)[]
        {
            ("TextBlock", e => new TextBlock { Text = e.Display }),
            ("AppFont NoWrap", e => new EmojiLabel { Text = e.Display, FontFamily = AppFonts.Family, FontSize = 15 }),
            ("AppFont Wrap", e => new EmojiLabel { Text = e.Display, FontFamily = AppFonts.Family, FontSize = 15, TextWrapping = TextWrapping.Wrap, Width = 100 }),
            ("AppFont Wrap+Trim", e => new EmojiLabel { Text = e.Display, FontFamily = AppFonts.Family, FontSize = 15, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Width = 100, MaxHeight = 60 }),
            ("YaHei Wrap+Trim", e => new EmojiLabel { Text = e.Display, FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"), FontSize = 15, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Width = 100, MaxHeight = 60 }),
            ("EmojiLabel", e => new EmojiLabel { Text = e.Display }),
            ("QuietButton+TextBlock", e => Quiet(new TextBlock { Text = e.Display })),
            ("QuietButton+EmojiLabel", e => Quiet(new EmojiLabel { Text = e.Display })),
            ("SymbolIcon button", e => Quiet(new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.MoreHorizontal24))),
            ("ContextMenu", e => { var b = Quiet(new TextBlock { Text = e.Display }); var m = new ContextMenu(); m.Items.Add(new MenuItem { Header = "隐藏" }); b.ContextMenu = m; return b; }),
        };
        for (var round = 0; round < 3; round++)
            foreach (var (name, make) in variants)
            {
                var watch = Stopwatch.StartNew();
                var wrap = new WrapPanel();
                foreach (var entry in entries) wrap.Children.Add(make(entry));
                var built = watch.Elapsed.TotalMilliseconds;
                host.Content = new ScrollViewer { Content = wrap }; host.UpdateLayout();
                Console.WriteLine($"breakdown r{round} {name,-24}: build {built,6:F1} ms, build+layout {watch.Elapsed.TotalMilliseconds,6:F1} ms");
                host.Content = null; await Task.Delay(50);
            }
        host.Close();
        static Button Quiet(object content) { var b = new Button { Content = content }; b.SetResourceReference(FrameworkElement.StyleProperty, "Quiet"); return b; }
    }

    private static int Count(DependencyObject root)
    {
        var count = 1;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) count += Count(System.Windows.Media.VisualTreeHelper.GetChild(root, i));
        return count;
    }
}

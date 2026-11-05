using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>检查实际绘制的字体文件，避免主字体声明与控件实际显示不一致。</summary>
internal static class TypographyChecks
{
    internal sealed record Glyphs(string Text, string Font, bool Simulated);
    internal static List<Glyphs> Runs(Visual root)
    {
        var result = new List<Glyphs>();
        void Drawing(Drawing? drawing)
        {
            if (drawing is DrawingGroup group) foreach (var child in group.Children) Drawing(child);
            if (drawing is GlyphRunDrawing run)
                result.Add(new(new string(run.GlyphRun.Characters?.ToArray() ?? []), run.GlyphRun.GlyphTypeface.FontUri.ToString(), run.GlyphRun.GlyphTypeface.StyleSimulations != StyleSimulations.None));
        }
        void Visit(Visual node)
        {
            Drawing(VisualTreeHelper.GetDrawing(node));
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                if (VisualTreeHelper.GetChild(node, i) is Visual child) Visit(child);
        }
        Visit(root); return result;
    }

    public static async Task RunAsync(MainWindow main, string output, Action<Window, string> capture)
    {
        var reports = new Dictionary<string, object>();
        var unexpected = new List<string>();
        void Inspect(Window window, string key)
        {
            var runs = Runs(window);
            // 仅检查普通界面文字；图标由原有 Fluent 字体绘制，特殊语种使用补充字体。
            var wrong = runs.Where(r => r.Text.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or >= '\u4e00' and <= '\u9fff') && !r.Font.Contains("/msyh", StringComparison.OrdinalIgnoreCase)).ToArray();
            unexpected.AddRange(wrong.Select(r => key + ": " + r.Text + " => " + r.Font));
            reports[key] = new { Fonts = runs.Select(r => r.Font).Distinct(), Unexpected = wrong };
            capture(window, Path.Combine(output, key + ".png"));
        }
        await main.Model.OpenThreadAsync(101, 1);
        foreach (var post in main.Model.ReadingPosts) main.Model.Posts.Add(new(post.Post, "综合版1"));
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme); main.UpdateLayout(); await Task.Delay(100); Inspect(main, "main-" + theme);
            foreach (var window in new Window[] { new ComposerWindow(main.Model, 0), new SettingsWindow(main.Model), new CookieWindow(main.Model), new SubscriptionWindow(main.Model) })
            {
                try { window.Owner = main; window.ShowInTaskbar = false; window.Show(); await Task.Delay(100); Inspect(window, window.GetType().Name + "-" + theme); }
                finally { window.Close(); }
            }
        }
        App.ApplyTheme("light");
        var normal = new TextBlock { Text = "中文阅读 Microsoft YaHei 123", FontFamily = AppFonts.Family, FontSize = 20 };
        var strong = new TextBlock { Text = normal.Text, FontFamily = AppFonts.Family, FontSize = 20, FontWeight = FontWeights.SemiBold };
        var panel = new StackPanel(); panel.Children.Add(normal); panel.Children.Add(strong);
        var global = new TextBlock { Text = "ฅ ง ว ໒ ༝", FontFamily = AppFonts.Family, FontSize = 20 };
        panel.Children.Add(global);
        var sample = new Window { Owner = main, Width = 500, Height = 200, Content = panel, ShowInTaskbar = false };
        try
        {
            sample.Show(); await Task.Delay(100);
            var regularRuns = Runs(normal).Where(r => r.Text.Any(char.IsLetterOrDigit)).ToArray();
            var semiboldRuns = Runs(strong).Where(r => r.Text.Any(char.IsLetterOrDigit)).ToArray();
            reports["weights"] = new { Regular = regularRuns, Semibold = semiboldRuns };
            var globalRuns = Runs(global).Where(r => !string.IsNullOrWhiteSpace(r.Text)).ToArray();
            reports["global"] = globalRuns;
            foreach (var font in new[] { "leelawui.ttf", "himalaya.ttf" })
                if (!globalRuns.Any(r => r.Font.EndsWith(font, StringComparison.OrdinalIgnoreCase))) unexpected.Add("系统补充字体未生效：" + font);
            if (regularRuns.Length == 0 || regularRuns.Any(r => !r.Font.Contains("/msyh.ttc", StringComparison.OrdinalIgnoreCase) || r.Simulated) ||
                semiboldRuns.Length == 0 || semiboldRuns.Any(r => !r.Font.Contains("/msyhbd.ttc", StringComparison.OrdinalIgnoreCase) || r.Simulated))
                unexpected.Add("微软雅黑常规 / 粗体未按预期选择实际字体文件。");
        }
        finally { sample.Close(); }
        void ResourceFonts(ResourceDictionary dictionary)
        {
            foreach (var child in dictionary.MergedDictionaries) ResourceFonts(child);
            foreach (var key in dictionary.Keys)
                if (key is string name && name.Contains("Font", StringComparison.OrdinalIgnoreCase) && dictionary[key] is FontFamily font)
                    reports["resource:" + key] = font.Source;
        }
        ResourceFonts(Application.Current.Resources);
        File.WriteAllText(Path.Combine(output, "typography-results.json"), JsonSerializer.Serialize(new { Reports = reports, Unexpected = unexpected }, new JsonSerializerOptions { WriteIndented = true }));
        if (unexpected.Count > 0) throw new InvalidOperationException(string.Join("\n", unexpected));
        Console.WriteLine("PASS: Microsoft YaHei light/dark main and four dialogs, actual regular/bold glyphs without simulation, special-script fallbacks.");
    }
}

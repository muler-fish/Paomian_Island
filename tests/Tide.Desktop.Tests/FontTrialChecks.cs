using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>字体试验只检查实际字形来源、正文行为并捕获对照，不以断言代替用户判断观感。</summary>
internal static class FontTrialChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var oldPath = Path.GetFullPath("artifacts/font-otf-trial/ttf/");
        // 本地对照入口需要保留原版字体；常规 typography 检查不依赖这份额外资料。
        if (!File.Exists(Path.Combine(oldPath, "MiSans-Regular.ttf")) || !File.Exists(Path.Combine(oldPath, "MiSans-Semibold.ttf")))
            throw new InvalidOperationException("请将原版 MiSans Regular/Semibold TTF 放入 artifacts/font-otf-trial/ttf，再运行 --font-trial。");
        var oldFamily = new FontFamily(new Uri(oldPath), "./#MiSans");
        var reports = new List<object>();
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
            grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
            var variants = new[] {
                ("旧版 TTF · Grayscale / Fixed", oldFamily, TextRenderingMode.Grayscale, TextHintingMode.Fixed),
                ("只改参数 TTF · Auto / Animated", oldFamily, TextRenderingMode.Auto, TextHintingMode.Animated),
                ("只换字体 OTF · Grayscale / Fixed", AppFonts.Family, TextRenderingMode.Grayscale, TextHintingMode.Fixed),
                ("本次试验 OTF · Auto / Animated", AppFonts.Family, TextRenderingMode.Auto, TextHintingMode.Animated)
            };
            var samples = new List<PostPreview>();
            for (var i = 0; i < variants.Length; i++)
            {
                var (title, family, render, hint) = variants[i];
                var panel = new StackPanel { Margin = new(16) };
                panel.Children.Add(new TextBlock { Text = title, FontSize = 13, Margin = new(0,0,0,8) });
                foreach (var size in new[] { 14d,16d,20d,24d })
                {
                    var preview = new PostPreview { FontFamily = family, FontSize = size,
                        Html = $"{size}px 千 看 七 战 越 三千……先生。<br>南京，第七十九旅，栖霞区。", Margin = new(0,4,0,4) };
                    TextOptions.SetTextRenderingMode(preview, render); TextOptions.SetTextHintingMode(preview, hint);
                    panel.Children.Add(preview); samples.Add(preview);
                }
                var frame = new Border { Child = panel, BorderThickness = new(1) };
                frame.SetResourceReference(Border.BackgroundProperty, "Surface"); frame.SetResourceReference(Border.BorderBrushProperty, "Line");
                Grid.SetColumn(frame, i % 2); Grid.SetRow(frame, i / 2); grid.Children.Add(frame);
            }
            var window = new Window { Owner = owner, ShowInTaskbar = false, Content = grid, Width = Math.Min(1240, SystemParameters.WorkArea.Width-40),
                Height = Math.Min(820, SystemParameters.WorkArea.Height-40), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            try
            {
                window.Show(); window.Activate(); await Task.Delay(250);
                capture(window, Path.Combine(output, "comparison-" + theme + ".png"));
                ReadingPolishChecks.CaptureScreen(window, Path.Combine(output, "comparison-native-" + theme + ".png"));
                reports.Add(new { Theme=theme, Dpi=VisualTreeHelper.GetDpi(window).PixelsPerInchX,
                    Samples=samples.Select(s => new { s.FontSize, Font=s.FontFamily.Source, Runs=TypographyChecks.Runs(s) }).ToArray() });
            }
            finally { window.Close(); }
        }
        App.ApplyTheme("light");
        var panel2 = new StackPanel();
        var weights = new[] { FontWeights.Normal, FontWeights.Medium, FontWeights.SemiBold, FontWeights.Bold };
        var weightSamples = weights.Select(w => new TextBlock { Text = "千看七战越 中文 Aa123", FontFamily = AppFonts.Family, FontWeight=w, FontSize=20 }).ToArray();
        var baselineSamples = weights.Select(w => new TextBlock { Text = "千看七战越 中文 Aa123", FontFamily = oldFamily, FontWeight=w, FontSize=20 }).ToArray();
        foreach (var text in weightSamples) panel2.Children.Add(text);
        foreach (var text in baselineSamples) panel2.Children.Add(text);
        var previewBody = new PostPreview { Html = string.Join("<br>", Enumerable.Repeat("千看七战越 中文阅读", 12)), FontSize=16 };
        var body = new PostBody { Html="千看七战越<br>正文选择😀<br>ฅ ༝ ໒", FontSize=16 };
        panel2.Children.Add(previewBody); panel2.Children.Add(body);
        var testWindow = new Window { Owner=owner, Content=panel2, Width=650, Height=650, ShowInTaskbar=false };
        try
        {
            testWindow.Show(); await Task.Delay(200);
            var weightRuns = weightSamples.Select((s,i) => new { Weight=weights[i].ToOpenTypeWeight(), Runs=TypographyChecks.Runs(s) }).ToArray();
            var baselineRuns = baselineSamples.Select((s,i) => new { Weight=weights[i].ToOpenTypeWeight(), Runs=TypographyChecks.Runs(s) }).ToArray();
            reports.Add(new { Weights=weightRuns, BaselineWeights=baselineRuns });
            File.WriteAllText(Path.Combine(output,"font-trial-results.json"),JsonSerializer.Serialize(reports,new JsonSerializerOptions { WriteIndented=true }));
            // 空格按现有复合字体规则交给 Segoe UI；检查实际中英文字形，避免误报。
            foreach (var weight in weightRuns)
            {
                var letters = weight.Runs.Where(r => r.Text.Any(char.IsLetterOrDigit)).ToArray();
                Require(letters.Length > 0 && letters.All(r => r.Font.EndsWith(".otf")), "OTF 未实际生效");
                var prior = baselineRuns.Single(b => b.Weight == weight.Weight).Runs.Where(r => r.Text.Any(char.IsLetterOrDigit)).ToArray();
                Require(prior.Length > 0 && letters.Any(r => r.Simulated) == prior.Any(r => r.Simulated), "字体替换改变了原有字重模拟行为");
                if (weight.Weight <= 600) Require(letters.All(r => !r.Simulated), "常规/中等/半粗字重出现伪加粗");
            }
            Require(previewBody.ActualHeight <= previewBody.FontSize * 10.5 + 1, "七行预览上限发生变化");
            foreach(var target in new DependencyObject[] { previewBody, body, body.Document })
                Require(TextOptions.GetTextFormattingMode(target)==TextFormattingMode.Ideal && TextOptions.GetTextRenderingMode(target)==TextRenderingMode.Auto && TextOptions.GetTextHintingMode(target)==TextHintingMode.Animated,"渲染参数没有覆盖真实阅读控件");
            body.SelectAll(); Require(EmojiText.Read(body.Selection.Start,body.Selection.End).Contains("😀"),"选区丢失原文");
            Require(body.Document.Blocks.OfType<Paragraph>().SelectMany(p=>p.Inlines).Any(i=>i is Emoji.Wpf.EmojiInline),"彩色 emoji 未独立绘制");
            Require(TypographyChecks.Runs(body).Count > 0,"正文没有绘制字形");
            capture(testWindow,Path.Combine(output,"weights-and-reading.png"));
        }
        finally { testWindow.Close(); }
        Console.WriteLine("PASS: actual OTF glyphs, no simulation for weights 400/500/600, weight 700 matches TTF baseline, reading modes, eight-line cap, selected text and independent emoji. Native A/B screenshots captured; visual preference requires acceptance.");
    }
    private static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
}

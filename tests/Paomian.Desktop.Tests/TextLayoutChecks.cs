using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

/// <summary>实际测量输入文字的可见区域和正文换行高度，捕获截图中的裁切及重复空行。</summary>
internal static class TextLayoutChecks
{
    public static async Task RunAsync(MainWindow main, string output, Action<Window, string> capture)
    {
        var input = (TextBox)main.FindName("JumpBox");
        input.Text = "No.69655993";
        await LayoutAsync(main);
        var host = (ScrollViewer)input.Template.FindName("PART_ContentHost", input);
        var viewport = UiRefinementChecks.Descendants<ScrollContentPresenter>(host).Single();
        var viewportBounds = viewport.TransformToAncestor(input).TransformBounds(new Rect(viewport.RenderSize));
        var character = input.GetRectFromCharacterIndex(0);
        var inputFits = character.Top >= viewportBounds.Top - 0.5 && character.Bottom <= viewportBounds.Bottom + 0.5;
        capture(main, Path.Combine(output, "typed-input.png"));

        var samples = new Dictionary<string, PostBody>
        {
            ["brOnly"] = new() { Html = "第一行<br />第二行", FontSize = 16 },
            ["brWithSourceNewline"] = new() { Html = "第一行<br />\n第二行", FontSize = 16 },
            ["plainNewline"] = new() { Html = "第一行\n第二行", FontSize = 16 },
            ["intentionalBlank"] = new() { Html = "第一行<br /><br />第二行", FontSize = 16 }
        };
        var panel = new StackPanel { Margin = new Thickness(20) };
        foreach (var (name, body) in samples)
        {
            panel.Children.Add(new TextBlock { Text = name, Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(body);
        }
        var window = new Window { Title = "正文换行验证", Width = 640, Height = 650, Content = panel, Owner = main, ShowInTaskbar = false };
        try
        {
            window.Show();
            await LayoutAsync(window);
            capture(window, Path.Combine(output, "line-spacing.png"));
            var heights = samples.ToDictionary(pair => pair.Key, pair => pair.Value.ActualHeight);
            var lineBreaksMatch = Math.Abs(heights["brOnly"] - heights["brWithSourceNewline"]) < 1;
            var plainLinesMatch = Math.Abs(heights["brOnly"] - heights["plainNewline"]) < 1;
            var intentionalBlankPreserved = heights["intentionalBlank"] > heights["brOnly"] + 10;
            // 使用用户截图对应的公开回复作为固定夹具；不随在线内容、网络状态变化。
            var ascii = new PostBody { Html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ascii-post.html")), FontSize = 16 };
            panel.Children.Clear();
            panel.Children.Add(ascii);
            await LayoutAsync(window);
            var asciiLines = new TextRange(ascii.Document.ContentStart, ascii.Document.ContentEnd).Text
                .Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;
            var asciiHeight = ascii.ActualHeight;
            capture(window, Path.Combine(output, "ascii-light.png"));
            var fontHeights = new Dictionary<double, double>();
            foreach (var fontSize in new[] { 13d, 16d, 24d })
            {
                ascii.FontSize = fontSize;
                await LayoutAsync(window);
                fontHeights[fontSize] = ascii.ActualHeight;
            }
            ascii.FontSize = 16;
            App.ApplyTheme("dark");
            await LayoutAsync(window);
            capture(window, Path.Combine(output, "ascii-dark.png"));
            capture(main, Path.Combine(output, "typed-input-dark.png"));
            App.ApplyTheme("light");
            var report = new
            {
                InputFits = inputFits, InputHeight = input.ActualHeight,
                ContentHostPadding = host.Padding.ToString(), ContentHostMargin = host.Margin.ToString(),
                Viewport = viewportBounds.ToString(), Character = character.ToString(),
                LineBreaksMatch = lineBreaksMatch, PlainLinesMatch = plainLinesMatch,
                IntentionalBlankPreserved = intentionalBlankPreserved, Heights = heights,
                AsciiLines = asciiLines, AsciiHeight = asciiHeight, FontHeights = fontHeights
            };
            File.WriteAllText(Path.Combine(output, "text-layout-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(report));
            if (!inputFits || !lineBreaksMatch || !plainLinesMatch || !intentionalBlankPreserved || asciiLines != 10 ||
                fontHeights[13] >= fontHeights[16] || fontHeights[16] >= fontHeights[24])
                throw new InvalidOperationException("文字排版检查失败：输入裁切或换行高度不一致。");
        }
        finally { window.Close(); }
    }

    private static async Task LayoutAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
    }
}

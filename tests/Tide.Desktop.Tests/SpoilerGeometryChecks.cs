using System.IO;
using System.Windows;
using System.Windows.Documents;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class SpoilerGeometryChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        const string hidden = "中文 iii WWW 空格 <b>粗体</b>😀<br>次行 abc";
        var window = new TextWindow("遮挡排版检查", "前文[h]" + hidden + "[/h]尾标") { Owner = owner, Width = 570, Height = 420, ShowInTaskbar = false };
        window.Show();
        try
        {
            var body = UiRefinementChecks.Descendants<PostBody>(window).Single();
            body.HideSpoilers = true;
            foreach (var width in new[] { 570d, 480d })
            {
                window.Width = width;
                foreach (var size in new[] { 16d, 24d })
                {
                    body.FontSize = size;
                    body.Html = "前文[h]" + hidden + "[/h]尾标";
                    await Task.Delay(60); window.UpdateLayout();
                    var maskRect = Tail(body); var maskHeight = body.ActualHeight;
                    body.Html = "前文" + hidden + "尾标";
                    await Task.Delay(60); window.UpdateLayout();
                    var shownRect = Tail(body);
                    if (Math.Abs(maskRect.X - shownRect.X) > .2 || Math.Abs(maskRect.Y - shownRect.Y) > .2 || Math.Abs(body.ActualHeight - maskHeight) > .2)
                        throw new Exception($"遮挡改变排版 width={width}, size={size}: hidden={maskRect}, revealed={shownRect}");
                }
            }
            capture(window, Path.Combine(output, "spoiler-geometry.png"));
            Console.WriteLine("PASS spoiler geometry across widths and font sizes");
        }
        finally { window.Close(); }
    }
    private static Rect Tail(PostBody body) => body.Document.Blocks.OfType<Paragraph>().Single().Inlines.LastInline!.ContentEnd.GetPositionAtOffset(-2)!.GetCharacterRect(LogicalDirection.Forward);
}

using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using Paomian.Core;

namespace Paomian.Desktop;

// 只构造 WPF 文本元素；远程 HTML 不解释，图片不加载，链接只在主动点击时交给浏览器。
internal static class ReleaseMarkdown
{
    private static readonly Regex Tokens = new(@"!?\[(?<label>[^\]\n]*)\]\((?<url>[^\s()]+)\)|\*\*(?<strong>[^*]+)\*\*|__(?<strong>[^_]+)__|`(?<code>[^`]+)`|<(?<web>https?://[^\s<>]+)>|<[^>\n]+>|(?<bare>https?://[^\s<>()，。；！？、]+)", RegexOptions.NonBacktracking);

    internal static void Append(Panel parent, ReleaseNote entry, Action<string> open)
    {
        const int maxChars = 16_384, maxBlocks = 128;
        var shortened = entry.Body.Length > maxChars;
        var text = shortened ? entry.Body[..maxChars] : entry.Body;
        using var lines = new System.IO.StringReader(text);
        var paragraph = new StringBuilder();
        var count = 0;
        void Block(string value, bool heading = false, bool bullet = false)
        {
            if (++count > maxBlocks) { shortened = true; return; }
            var block = SettingsLayout.Text("");
            block.Margin = new(0, heading ? 6 : 2, 0, 0);
            if (heading) block.FontWeight = FontWeights.SemiBold;
            InlineText(block.Inlines, value, open);
            if (bullet)
            {
                var row = new DockPanel(); var dot = SettingsLayout.Text("•"); dot.Width = 14; dot.Margin = block.Margin;
                row.Children.Add(dot); row.Children.Add(block); parent.Children.Add(row);
            }
            else parent.Children.Add(block);
        }
        void Flush()
        {
            if (paragraph.Length == 0) return;
            Block(paragraph.ToString()); paragraph.Clear();
        }
        while (lines.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0) { Flush(); continue; }
            var hashes = 0;
            while (hashes < line.Length && line[hashes] == '#') hashes++;
            if (hashes is >= 1 and <= 6 && hashes < line.Length && char.IsWhiteSpace(line[hashes]))
            {
                Flush(); var title = line[hashes..].Trim();
                if (count > 0 || title != "更新内容") Block(title, heading: true);
            }
            else if (line.Length >= 2 && line[0] is '-' or '*' or '+' && char.IsWhiteSpace(line[1]))
            { Flush(); Block(line[2..].TrimStart(), bullet: true); }
            else { if (paragraph.Length > 0) paragraph.Append(' '); paragraph.Append(line); }
            if (count > maxBlocks) break;
        }
        Flush();
        if (count == 0 && !shortened) parent.Children.Add(SettingsLayout.Text("此版本未提供更新说明", true));
        if (shortened)
        {
            var more = SettingsLayout.Text(""); more.Inlines.Add(Link("在 GitHub 查看此版本完整说明", () => open(entry.Page.AbsoluteUri)));
            parent.Children.Add(more);
        }
    }

    private static void InlineText(InlineCollection result, string text, Action<string> open, int depth = 0)
    {
        var end = 0;
        foreach (Match match in Tokens.Matches(text))
        {
            if (match.Index > end) result.Add(text[end..match.Index]);
            end = match.Index + match.Length;
            if (match.Groups["strong"] is { Success: true } strong)
            {
                var bold = new Bold(); result.Add(bold);
                if (depth == 0) InlineText(bold.Inlines, strong.Value, open, 1); else bold.Inlines.Add(strong.Value);
            }
            else if (match.Groups["code"] is { Success: true } code) result.Add(code.Value);
            else if (match.Value.StartsWith('!')) result.Add(match.Groups["label"].Value);
            else if (match.Groups["url"].Success || match.Groups["web"].Success || match.Groups["bare"].Success)
            {
                var bare = match.Groups["bare"].Value;
                var url = match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["web"].Success ? match.Groups["web"].Value : bare.TrimEnd('.', ',', ';', '!', '?');
                var label = match.Groups["label"].Success ? match.Groups["label"].Value : url;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0 && uri.UserInfo.Length == 0)
                {
                    var link = Link(label, () => open(uri.AbsoluteUri)); link.ToolTip = uri.AbsoluteUri; result.Add(link);
                }
                else result.Add(label);
                if (bare.Length > url.Length) result.Add(bare[url.Length..]);
            }
            else result.Add(match.Value);
        }
        if (end < text.Length) result.Add(text[end..]);
    }

    internal static Hyperlink Link(string label, Action activate)
    {
        var link = new Hyperlink(new Run(label)); link.SetResourceReference(TextElement.ForegroundProperty, "Accent");
        AutomationProperties.SetName(link, label); link.Click += (_, e) => { e.Handled = true; activate(); };
        return link;
    }
}

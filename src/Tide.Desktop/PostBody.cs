using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>把帖子正文转换成可选择复制的 WPF 文档，只支持有限的格式和安全链接，不执行 HTML。</summary>
public sealed class PostBody : System.Windows.Controls.RichTextBox
{
    // 行高随字号变化；1.5 倍用于中文正文及多行颜文字，避免正文过松。
    private const double LineHeightRatio = UiMetrics.BodyLineHeightRatio;
    // 依赖属性使控件可直接在 XAML 中绑定正文；列表复用控件并换绑内容时会重新渲染。
    public static readonly DependencyProperty HtmlProperty = DependencyProperty.Register(nameof(Html), typeof(string), typeof(PostBody), new PropertyMetadata("", (d, _) => ((PostBody)d).Render()));
    public string Html { get => (string)GetValue(HtmlProperty); set => SetValue(HtmlProperty, value); }
    public static readonly DependencyProperty HideSpoilersProperty = DependencyProperty.Register(nameof(HideSpoilers), typeof(bool), typeof(PostBody), new PropertyMetadata(false, (d, _) => ((PostBody)d).Render()));
    public bool HideSpoilers { get => (bool)GetValue(HideSpoilersProperty); set => SetValue(HideSpoilersProperty, value); }
    public static event Action<long>? ReferenceClicked;
    public static event Action<Hyperlink, string>? UrlClicked;

    public PostBody()
    {
        IsReadOnly = true; IsDocumentEnabled = true; BorderThickness = new(0); Padding = new(0);
        Background = Brushes.Transparent; VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; AcceptsTab = false;
        SetResourceReference(ForegroundProperty, "BodyText");
        SetResourceReference(SelectionBrushProperty, "AccentSoft");
        FontFamily = AppFonts.Family;
        FontWeight = AppFonts.ReadingWeight;
        SetResourceReference(StyleProperty, "ReaderDocument");
        AppFonts.ApplyReadingRendering(this);
        SetResourceReference(HideSpoilersProperty, "HideSpoilers");
        DataObject.AddCopyingHandler(this, (_, e) =>
        {
            var text = EmojiText.Read(Selection.Start, Selection.End);
            // WPF's default RTF/XAML payload would otherwise contain the masked original text.
            e.CancelCommand();
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.ExternalException) { /* Keep the default rich payload cancelled when the clipboard is busy. */ }
        });
    }

    private void Render()
    {
        var paragraph = new Paragraph { Margin = new(0), LineStackingStrategy = LineStackingStrategy.MaxHeight };
        var doc = new FlowDocument(paragraph) { PagePadding = new(0), FontFamily = FontFamily, FontWeight = AppFonts.ReadingWeight, LineHeight = FontSize * LineHeightRatio };
        AppFonts.ApplyReadingRendering(doc);
        doc.SetResourceReference(ForegroundProperty, "BodyText");
        doc.SetBinding(FontSizeProperty, new System.Windows.Data.Binding(nameof(FontSize)) { Source = this });
        Document = doc;
        var content = ContentText.Normalize(Html);
        // 先隔离防剧透片段，再分析标签与链接，保证隐藏段落在点击前不进入可见正文。
        foreach (var piece in Regex.Split(content, @"(\[h\][\s\S]*?\[/h\])", RegexOptions.IgnoreCase))
        {
            if (HideSpoilers && piece.StartsWith("[h]", StringComparison.OrdinalIgnoreCase) && piece.EndsWith("[/h]", StringComparison.OrdinalIgnoreCase))
            {
                AddSpoiler(paragraph.Inlines, piece[3..^4]); continue;
            }
            // 关闭防剧透时不剥离 [h] 标签；仍使用同一个受限 HTML 渲染器。
            AppendHtml(paragraph.Inlines, piece);
        }
        RenderEmoji();
    }

    private void AddSpoiler(InlineCollection target, string hiddenHtml) => target.Add(new SpoilerSpan(hiddenHtml, true));

    // 列表预览复用同一份受限格式映射；调用方负责 Normalize 和防剧透处理。
    internal static void AppendHtml(InlineCollection target, string html)
    {
        var current = (Span?)null;
        // 栈保存嵌套标签的父 Span；遇到结束标签时恢复到对应层，容忍不完全规范的 HTML。
        var stack = new Stack<(string Tag, Span? Parent)>();
        var suppressLinkChildren = false;
        InlineCollection Destination() => current?.Inlines ?? target;
        foreach (var token in Regex.Split(html, "(<[^>]+>)"))
        {
            if (token.StartsWith('<'))
            {
                var m = Regex.Match(token, @"^<\s*(/?)\s*([a-z0-9]+)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var tag = m.Groups[2].Value.ToLowerInvariant(); var close = m.Groups[1].Value == "/";
                if (tag == "br" || close && tag is "p" or "div") { Destination().Add(new LineBreak()); continue; }
                if (close)
                {
                    if (stack.Any(x => x.Tag == tag))
                    {
                        while (stack.Count > 0) { var entry = stack.Pop(); current = entry.Parent; if (entry.Tag == tag) break; }
                    }
                    if (tag == "a") suppressLinkChildren = false;
                    continue;
                }
                if (tag is not ("b" or "strong" or "i" or "em" or "s" or "del" or "u" or "span" or "font" or "a")) continue;
                // 仅将已知标签和少量样式映射到 WPF 属性，不把服务端样式直接交给解析器。
                Span span = new();
                // 半粗体使用真实 Semibold 字形；Bold(700) 会再模拟加粗，使骰子数字和强调文字过黑。
                if (tag is "b" or "strong") span.FontWeight = FontWeights.SemiBold;
                if (tag is "i" or "em") span.FontStyle = FontStyles.Italic;
                if (tag is "s" or "del") span.TextDecorations = TextDecorations.Strikethrough;
                if (tag == "u") span.TextDecorations = TextDecorations.Underline;
                if (Regex.IsMatch(token, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)) span.FontWeight = FontWeights.SemiBold;
                if (Regex.IsMatch(token, @"(?:color\s*=\s*[""']?|color\s*:\s*)(?:red|#(?:cc0000|ff0000))", RegexOptions.IgnoreCase)) span.SetResourceReference(ForegroundProperty, "PostRed");
                else if (tag == "font" || Regex.IsMatch(token, @"color\s*:", RegexOptions.IgnoreCase)) span.SetResourceReference(ForegroundProperty, "PostGreen");
                if (tag == "a" && !suppressLinkChildren)
                {
                    var href = Regex.Match(token, @"(?<![\w:-])href\s*=\s*(?:""(?<url>[^""]*)""|'(?<url>[^']*)'|(?<url>[^\s>]+))", RegexOptions.IgnoreCase);
                    var url = WebUtility.HtmlDecode(href.Groups["url"].Value).Trim();
                    // 包括站内相对路径与 // 外站地址，统一补全后再做 HTTP/HTTPS 校验。
                    if (IslandLink.TryWebUri(url, out var uri))
                    {
                        var link = new Hyperlink { ToolTip = uri.AbsoluteUri };
                        link.SetResourceReference(ForegroundProperty, "Link");
                        link.Click += (_, _) => Navigate(link, uri.AbsoluteUri); span = link; suppressLinkChildren = true;
                    }
                }
                Destination().Add(span); stack.Push((tag, current)); current = span; continue;
            }
            var text = WebUtility.HtmlDecode(token);
            // 已位于 HTML 链接内部的文字不再识别成新链接，防止生成嵌套 Hyperlink。
            if (suppressLinkChildren) Destination().Add(new Run(text));
            else AddText(Destination(), text);
        }
    }

    private void RenderEmoji()
    {
        foreach (var paragraph in Document.Blocks.OfType<Paragraph>().ToArray()) ColorEmoji.Render(paragraph.Inlines);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == FontSizeProperty && Document is not null) Document.LineHeight = FontSize * LineHeightRatio;
    }

    private static void AddText(InlineCollection target, string text)
    {
        // 没有包在 HTML 链接中的引用号和网址也可以点击，其余文本保持普通 Run。
        var matches = Regex.Matches(text, @">>\s*(?:No\.)?(\d+)|https?://[^\s<>""'\u3000]+", RegexOptions.IgnoreCase);
        var start = 0;
        foreach (Match match in matches)
        {
            target.Add(new Run(text[start..match.Index]));
            var value = match.Value; var link = new Hyperlink(new Run(value));
            link.SetResourceReference(ForegroundProperty, "Link");
            link.ToolTip = match.Groups[1].Success ? "查看引用" : value;
            if (match.Groups[1].Success && long.TryParse(match.Groups[1].Value, out var id)) link.Click += (_, _) => ReferenceClicked?.Invoke(id);
            else link.Click += (_, _) => Navigate(link, value);
            target.Add(link); start = match.Index + match.Length;
        }
        target.Add(new Run(text[start..]));
    }
    private static void Navigate(Hyperlink source, string url)
    {
        // 网页包在 a 标签里的 >>No. 仍是引用；普通链接交给拥有该正文的主窗口统一导航。
        var text = new TextRange(source.ContentStart, source.ContentEnd).Text.Trim();
        if (text.StartsWith(">>") && ContentText.ParseThreadId(text) is { } id &&
            IslandLink.TryWebUri(url, out var uri) && ContentText.IsIslandHost(uri.Host)) ReferenceClicked?.Invoke(id);
        else UrlClicked?.Invoke(source, url);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class PostPreviewChecks
{
    internal static async Task RunAsync(MainWindow window, string output, Action<Window, string> capture)
    {
        var vm = window.Model;
        var full = new Post { Id = 101, Fid = 30, UserHash = "Admin", IsAdmin = true, IsSage = true,
            Time = "2026-10-27(二)15:48:38", Title = "标题独占一行", Name = "自定义名称", ReplyCount = 1234,
            Content = "首行正文应完整保留。<br><b>加粗</b> / <i>斜体</i> / <u>下划线</u><br>　保留全角缩进<br><br>空行仍在。😀<br><font color=\"green\">绿色强调</font><br><font color=\"red\">红色强调</font><br>第七行，在这里继续写一些文字使它需要截断。<br>第九行不应额外增加预览高度。" };
        var shortPost = full with { Id = 102, Title = "无标题", Name = "无名氏", IsAdmin = false, IsSage = false, UserHash = "7zWLImE", ReplyCount = 2, Content = "只有一行正文。" };
        var named = shortPost with { Id = 103, Title = "", Name = "只有名称", Content = "正文第一行<br>正文第二行" };
        vm.Posts.Clear();
        foreach (var post in new[] { full, shortPost, named }) vm.Posts.Add(new(post, "综合版1"));
        Require(vm.Posts[1].Title == "" && vm.Posts[1].Author == "" && !vm.Posts[1].HasTitle && !vm.Posts[1].HasAuthor, "默认标题名称仍占行，或正文被提升为标题");
        Require(vm.Posts[0].DateLabel == (DateTime.Now.Year == 2026 ? "10/27 15:48" : "2026/10/27 15:48") && vm.Posts[2].Excerpt == "正文第一行\n正文第二行", "日期或正文开头/换行丢失");
        var list = (ListBox)window.FindName("ThreadList");
        var columns = UiRefinementChecks.Descendants<Grid>(window).Single(g => g.ColumnDefinitions.Count == 5 && g.ColumnDefinitions[2].MinWidth == 240);
        window.Width = 1280; await Pause();
        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var firstBody = UiRefinementChecks.Descendants<PostPreview>(first).Single();
        Require(Read(firstBody).Contains("首行正文应完整保留。\n加粗 / 斜体 / 下划线\n　保留全角缩进\n\n空行仍在。😀"), "富文本预览丢失格式化换行、缩进、空行或 emoji");
        Require(Spans(firstBody.Inlines).Any(s => s.FontWeight == FontWeights.SemiBold) && Spans(firstBody.Inlines).Any(s => s.FontStyle == FontStyles.Italic), "预览没有保留加粗/斜体");
        var identity = UiRefinementChecks.Descendants<PostIdentity>(first).Single();
        Require(((Border)identity.FindName("SageBadge")).Visibility == Visibility.Collapsed, "元信息中仍重复显示 SAGE");
        // Refined Reader 移植后 SAGE 与串号同在卡片底部一行，不再位于标题上方。
        var order = new[] { "CardMetadata", "CardTitle", "CardAuthor", "CardBody", "CardSage" }.Select(n => Named(first, n)).ToArray();
        Require(order.All(e => e.IsVisible), "完整字段没有全部显示");
        for (var i = 1; i < order.Length; i++) Require(Bounds(order[i-1], window).Bottom <= Bounds(order[i], window).Top + 1, "卡片字段顺序或独立行不正确");
        capture(window, Path.Combine(output, "cards-initial.png"));
        Require(firstBody.FontSize == PreviewSize(vm.ReadingFontSize), "长正文没有跟随阅读字号（预览比正文小 2 号）；" + Describe(firstBody, vm.ReadingFontSize));
        Require(SevenLines(firstBody) && Math.Abs(firstBody.ActualHeight - firstBody.MaxHeight) <= 1, "长正文没有限制为七行；" + Describe(firstBody, vm.ReadingFontSize));
        list.ScrollIntoView(vm.Posts[1]); await Pause();
        var second = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
        Require(!Named(second, "CardTitle").IsVisible && !Named(second, "CardAuthor").IsVisible && !Named(second, "CardSage").IsVisible, "空可选字段没有折叠");
        Require(Named(second, "CardBody").ActualHeight <= ((PostPreview)Named(second, "CardBody")).LineHeight + 1, "短正文仍占七行高度");

        // 调整同一份阅读设置，验证已经显示的真实列表容器即时响应，而非只给控件手动赋值。
        var originalSize = vm.State.FontSize;
        try
        {
            foreach (var size in new[] { 13d, 18d, 24d })
            {
                vm.State.FontSize = size; vm.AppearanceChanged();
                list.ScrollIntoView(vm.Posts[0]); await Pause();
                first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                firstBody = (PostPreview)Named(first, "CardBody");
                var title = (EmojiLabel)Named(first, "CardTitle");
                var author = (EmojiLabel)Named(first, "CardAuthor");
                Require(firstBody.FontSize == PreviewSize(size) && title.FontSize == PreviewSize(size) && author.FontSize == vm.PreviewAuthorFontSize, $"列表正文、标题、作者没有同步阅读字号及层级；TitleFontSize={title.FontSize}, AuthorFontSize={author.FontSize}；" + Describe(firstBody, vm.ReadingFontSize));
                Require(title.FontWeight == FontWeights.SemiBold && Equals(author.Foreground, Application.Current.FindResource("Muted")), "字号同步后标题加粗或作者浅色丢失");
                Require(SevenLines(firstBody) && firstBody.ActualHeight <= firstBody.MaxHeight + 1, "同步字号后七行上限没有重新计算；" + Describe(firstBody, vm.ReadingFontSize));
                Require(((TextBlock)Named(first, "CardDate")).FontSize == 12 && ((TextBlock)Named(first, "CardReplies")).FontSize == 12, "正文调字号连带改变了元信息字号");
                capture(window, Path.Combine(output, $"cards-font-{size}.png"));
            }
        }
        finally { vm.State.FontSize = originalSize; vm.AppearanceChanged(); await Pause(); }

        // 单独验证八条可见文本行，以及窄宽度下的自然折行和字号改变。
        // 此独立样例固定 14 DIP 以核对八条实测行；真实列表的继承/绑定已在上面单独验证。
        var preview = new PostPreview { FontSize = 14, Html = string.Join("<br>", Enumerable.Range(1, 10).Select(i => $"第{i}行完整内容")) };
        var sample = new Window { Owner = window, ShowInTaskbar = false, Width = 480, Height = 300, Content = new StackPanel { Margin = new(20), Children = { preview } } };
        try
        {
            sample.Show(); await Pause();
            var lines = preview.Inlines.OfType<Run>().ToArray();
            var seventh = lines[6].ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(!seventh.IsEmpty && seventh.Bottom <= preview.ActualHeight + 1, "第七行被截掉，实际少于七行");
            Require(SevenLines(preview) && Math.Abs(preview.ActualHeight - preview.MaxHeight) < 1, "十行预览超出七行");
            capture(sample, Path.Combine(output, "seven-lines.png"));
            preview.FontSize = 18; await Pause(); Require(SevenLines(preview) && preview.ActualHeight <= preview.MaxHeight + 1, "字号变化后行数上限错误");
            preview.Html = "单行"; await Pause(); Require(preview.ActualHeight <= preview.LineHeight + 1, "控件复用后仍保留大高度");
            preview.Html = new string('长', 240); preview.Width = 200; await Pause();
            Require(Math.Abs(preview.ActualHeight - preview.MaxHeight) <= 1, "无换行长正文没有按可用宽度截为七行；" + Describe(preview, 18));
            preview.Html = "[h]秘密😀[/h]<br><a href=\"https://example.com\">链接</a>";
            App.ApplySpoilerProtection(true); await Pause(); Require(Read(preview).Contains("[隐藏内容]") && !Read(preview).Contains("秘密"), "预览防剧透泄露正文");
            App.ApplySpoilerProtection(false); await Pause(); Require(Read(preview).Contains("[h]秘密😀[/h]"), "关闭后标签或 emoji 丢失");
            Require(Spans(preview.Inlines).OfType<Hyperlink>().All(l => !l.Focusable) && !preview.IsHitTestVisible, "预览链接抢占点击或键盘焦点");
        }
        finally { sample.Close(); App.ApplySpoilerProtection(vm.State.HideSpoilers); }

        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme);
            foreach (var (fg, bg) in new[] { ("CookieForeground", "CookieBackground"), ("AdminCookieForeground", "AdminCookieBackground"), ("OnSage", "SageBackground") })
                Require(Contrast(Brush(fg), Brush(bg)) >= 4.5, "标签文字对比度不足：" + fg);
            foreach (var fg in new[] { "PostGreen", "PostRed" }) Require(Contrast(Brush(fg), Brush("Surface")) >= 4.5, "正文强调色对比度不足：" + fg);
            // 第二轮 Refined Reader 配色的固定值，防止调整饼干标签时连带改到选中背景与 PO 标签。
            Require(Brush("AccentSoft") == (Color)ColorConverter.ConvertFromString(theme == "light" ? "#E6ECE8" : "#2D3533"), "饼干调色连带修改了选中背景");
            Require(Brush("PoBackground") == (Color)ColorConverter.ConvertFromString(theme == "light" ? "#E7EEF5" : "#293541"), "饼干调色连带修改了 PO 标签");
            foreach (var width in new[] { 240d, 320d, 480d, 720d })
            {
                columns.ColumnDefinitions[2].Width = new(width); list.ScrollIntoView(vm.Posts[0]); await Pause();
                first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var replies = Bounds(Named(first, "CardReplies"), window);
                var header = Bounds(Named(first, "CardMetadata"), window);
                var date = (TextBlock)Named(first, "CardDate");
                Require(date.Text == vm.Posts[0].DateLabel && Equals(date.ToolTip, full.Time), "列表日期格式或原始时间提示丢失");
                Require(Math.Abs(replies.Right - header.Right) < 1 && replies.Left >= header.Left, "回复数未右对齐或超出栏宽");
                var cookie = UiRefinementChecks.Descendants<PostIdentity>(first).Single();
                Require(Bounds(cookie, window).Right <= replies.Left, "窄栏饼干与回复数重叠");
                Require(Named(first, "CardBody").ActualHeight <= ((PostPreview)Named(first, "CardBody")).MaxHeight + 1, "栏宽改变导致正文超过七行");
                capture(window, Path.Combine(output, $"cards-{theme}-{width}.png"));
            }
        }
        App.ApplyTheme("light"); columns.ColumnDefinitions[2].Width = new(320);
        list.SelectedItem = vm.Posts[0]; await Pause();
        Require(vm.Original?.Id == 101, "选择预览没有打开目标串");
        // 无图模式不发图片请求，也必须保留原图手动入口；图文预览不能超出窄栏。
        vm.State.ShowImages = false; vm.AppearanceChanged();
        var illustrated = new PostCard(named with { Id = 104, ImagePath = "preview-fixture", Extension = ".png", Content = "图片串正文\n第二行" }, "综合版1");
        vm.Posts.Add(illustrated); columns.ColumnDefinitions[2].Width = new(240); list.ScrollIntoView(illustrated); await Pause();
        var imageItem = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(illustrated);
        var image = UiRefinementChecks.Descendants<PostImage>(imageItem).Single();
        var imageButton = UiRefinementChecks.Descendants<Button>(image).Single();
        Require(image.IsVisible && Equals(imageButton.Content, "显示这张图片") && Bounds(image, window).Right <= Bounds(list, window).Right, "缩略图手动入口丢失或溢出");
        Require(Named(imageItem, "CardBody").ActualWidth > 50, "缩略图占满了预览正文区域");
        var empty = new PostCard(shortPost with { Id = 105, Content = "" }, "综合版1");
        vm.Posts.Add(empty); list.ScrollIntoView(empty); await Pause();
        Require(!Named((ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(empty), "CardBody").IsVisible, "空正文仍占一行");
        Console.WriteLine("PASS: optional fields, metadata order, SAGE row, reading-font sync at 13/18/24, exact seven lines, short/long reflow, formatting/emoji/spoilers, reuse, four pane widths through 720 DIP, labels-only palette and reader selection.");
    }

    private static FrameworkElement Named(DependencyObject root, string name) => UiRefinementChecks.Descendants<FrameworkElement>(root).First(e => e.Name == name);
    private static string Describe(PostPreview item, double readingSize) => $"FontSize={item.FontSize}, VM.FontSize={readingSize}, ActualHeight={item.ActualHeight}, MaxHeight={item.MaxHeight}, LineHeight={item.LineHeight}, DesiredHeight={item.DesiredSize.Height}, ActualWidth={item.ActualWidth}, Dpi={VisualTreeHelper.GetDpi(item).DpiScaleY}, LayoutRounding={item.UseLayoutRounding}";
    private static Rect Bounds(FrameworkElement item, Window window) => item.TransformToAncestor(window).TransformBounds(new Rect(item.RenderSize));
    private static string Read(PostPreview item) => EmojiText.Read(item.ContentStart, item.ContentEnd).Replace("\r\n", "\n");
    private static IEnumerable<Span> Spans(InlineCollection inlines)
    {
        foreach (var inline in inlines) if (inline is Span span) { yield return span; foreach (var child in Spans(span.Inlines)) yield return child; }
    }
    private static Color Brush(string name) => ((SolidColorBrush)Application.Current.FindResource(name)).Color;
    private static double Contrast(Color a, Color b)
    {
        static double C(byte c) { var v = c / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double L(Color c) => .2126 * C(c.R) + .7152 * C(c.G) + .0722 * C(c.B);
        return (Math.Max(L(a), L(b)) + .05) / (Math.Min(L(a), L(b)) + .05);
    }
    private static Task Pause() => Task.Delay(180);
    // 当前规则：串列表预览比阅读正文小 2 号（不低于 13），七行上限由行高 × 7 向上取整到设备像素。
    private static double PreviewSize(double reading) => Math.Max(13, reading - 2);
    private static bool SevenLines(PostPreview preview)
    {
        var scale = VisualTreeHelper.GetDpi(preview).DpiScaleY;
        return Math.Abs(preview.LineHeight - preview.FontSize * ReadingTypography.GetLineHeight(preview)) < 0.01
            && Math.Abs(preview.MaxHeight - Math.Ceiling(preview.LineHeight * UiMetrics.PreviewLines * scale) / scale) < 0.01;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

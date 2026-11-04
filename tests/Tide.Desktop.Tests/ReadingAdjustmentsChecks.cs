using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Automation;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ReadingAdjustmentsChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window,string> capture)
    {
        var model=owner.Model;
        var dialog=new TextWindow("阅读排版检查", "中文  空格 AABB (ﾟ∀ﾟ) 😀<br>第二行") { Owner=owner, ShowInTaskbar=false };
        dialog.Show(); await Pause();
        try
        {
            var body=All<PostBody>(dialog).Single();
            var plain=EmojiText.Read(body.Document.ContentStart,body.Document.ContentEnd);
            var before=AllInlines(body.Document).OfType<Run>().First(r=>r.Text.Contains("第二行")).ContentStart.GetCharacterRect(LogicalDirection.Forward).Y;
            model.State.ReadingLetterSpacing=2;model.State.ReadingLineHeight=2.2;model.AppearanceChanged();await Pause();
            Require(EmojiText.Read(body.Document.ContentStart,body.Document.ContentEnd)==plain,"tracking changed copied Unicode/whitespace");
            Require(AllInlines(body.Document).OfType<InlineUIContainer>().Any(),"tracking did not affect layout");
            var after=AllInlines(body.Document).OfType<Run>().First(r=>r.Text=="第").ContentStart.GetCharacterRect(LogicalDirection.Forward).Y;
            Require(after>before+3 && Math.Abs(body.Document.LineHeight-body.FontSize*2.2)<.1,"line spacing did not affect layout");
            model.State.HideSpoilers=true;model.AppearanceChanged();
            dialog.SetHtml("前 [h]保留  空格😀[/h] 尾");await Pause();
            var spoiler=AllInlines(body.Document).OfType<SpoilerSpan>().Single();
            var tail=AllInlines(body.Document).OfType<Run>().Last(r=>r.Text=="尾");
            var hidden=tail.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(EmojiText.Read(body.Document.ContentStart,body.Document.ContentEnd).Contains("[隐藏内容]"),"tracking leaked hidden text");
            capture(dialog,Path.Combine(output,"tracking-hidden.png"));
            spoiler.Reveal();await Pause();
            var revealed=tail.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            Require(Math.Abs(hidden.X-revealed.X)<1 && Math.Abs(hidden.Y-revealed.Y)<1,"spoiler reveal changed tracking geometry");
            Require(EmojiText.Read(body.Document.ContentStart,body.Document.ContentEnd).Contains("保留  空格😀"),"tracking changed revealed text");
            capture(dialog,Path.Combine(output,"tracking-revealed.png"));
            var saved=model.Store.Load();Require(saved.ReadingLetterSpacing==2 && saved.ReadingLineHeight==2.2,"spacing not persisted");
        }
        finally { dialog.Close();model.State.ReadingLetterSpacing=0;model.State.ReadingLineHeight=1.85;model.State.HideSpoilers=false;model.AppearanceChanged(); }

        var settings=new SettingsWindow(model){Owner=owner,ShowInTaskbar=false};settings.Show();await Pause();
        try
        {
            Require(!All<CheckBox>(settings).Any(c=>Equals(c.Content,"惯性滚动")) && model.InertialScrolling,"inertial option still present or disabled");
            var theme=All<ComboBox>(settings).Single(c=>AutomationProperties.GetName(c)=="外观主题");
            theme.SelectedIndex=1;await Pause();Require(model.State.Theme=="dark","theme dropdown not applied");
            var scroll=All<ScrollViewer>(settings).First(s=>s.Content is StackPanel);scroll.ScrollToTop();await Pause();
            capture(settings,Path.Combine(output,"reading-settings-dark.png"));theme.SelectedIndex=0;await Pause();capture(settings,Path.Combine(output,"reading-settings-light.png"));
            foreach(var slider in All<Slider>(settings).Where(s=>AutomationProperties.GetName(s)=="字间距"))slider.Value=1.25;
            Require(model.State.ReadingLetterSpacing==1.25,"letter-spacing slider did not save");
        }
        finally { settings.Close();model.State.ReadingLetterSpacing=0;model.AppearanceChanged(); }
        await CheckPreviewImage(owner, output, capture);
        Console.WriteLine("PASS: live spacing, Unicode/copy fidelity, spoiler geometry, persistence, theme dropdown and always-on inertial scrolling.");
    }

    private static async Task CheckPreviewImage(MainWindow owner, string output, Action<Window, string> capture)
    {
        var model = owner.Model;
        var post = new Post { Id = 87654321, Fid = 4, UserHash = "预览饼干", ImagePath = "qa/reader-feedback", Extension = ".jpg",
            Content = "图片放在正文下方。<br>这里保留连续  空格 (ﾟ∀ﾟ)<br>第三行<br>第四行<br>第五行<br>第六行<br>第七行<br>第八行不会增加预览行数。" };
        var url = post.ImageUrl(true)!;
        model.Store.Cache.Write("images/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))) + ".bin",
            File.ReadAllBytes("tests/Tide.Desktop.Tests/Fixtures/welcome.jpg"));
        model.Posts.Clear(); model.Posts.Add(new(post, "综合版1"));
        var list = (ListBox)owner.FindName("ThreadList");
        var grid = All<Grid>(owner).Single(g => g.ColumnDefinitions.Count == 5 && g.ColumnDefinitions[2].MinWidth == 240);
        var previousWidth = grid.ColumnDefinitions[2].Width;
        try
        {
            foreach (var width in new[] { 270d, 520d })
            {
                grid.ColumnDefinitions[2].Width = new GridLength(width); list.ScrollIntoView(model.Posts[0]);
                await Task.Delay(450);
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                var preview = All<PostPreview>(row).Single(); var thumbnail = All<PostImage>(row).Single();
                var picture = All<Image>(thumbnail).SingleOrDefault(i => i.Source is not null);
                Require(picture is not null && picture.ActualWidth > 80, "新缩略图没有加载或仍过小");
                var imageBounds = thumbnail.TransformToAncestor(row).TransformBounds(new Rect(thumbnail.RenderSize));
                var textBounds = preview.TransformToAncestor(row).TransformBounds(new Rect(preview.RenderSize));
                Require(imageBounds.Top >= textBounds.Bottom && imageBounds.Right <= row.ActualWidth + 1, "缩略图没有在正文下方或窄栏溢出");
                Require(preview.ActualHeight <= preview.LineHeight * 7 + 1, "有图预览超过七行");
                capture(owner, Path.Combine(output, $"preview-bottom-image-{width}.png"));
            }
            model.State.ShowImages = false; model.AppearanceChanged(); await Pause();
            Require(All<Button>(list).Any(b => Equals(b.Content, "显示这张图片")), "无图模式没有保留手动查看入口");
        }
        finally { grid.ColumnDefinitions[2].Width = previousWidth; model.State.ShowImages = true; model.AppearanceChanged(); }
        Console.WriteLine("PASS: enlarged bottom thumbnail at narrow/wide column widths, seven-line preview and manual image entry.");
    }
    private static IEnumerable<T> All<T>(DependencyObject value) where T:DependencyObject=>UiRefinementChecks.Descendants<T>(value);
    private static IEnumerable<Inline> AllInlines(FlowDocument doc)=>doc.Blocks.OfType<Paragraph>().SelectMany(p=>Nested(p.Inlines));
    private static IEnumerable<Inline> Nested(InlineCollection source){foreach(var i in source){yield return i;if(i is Span s)foreach(var n in Nested(s.Inlines))yield return n;}}
    private static Task Pause()=>Task.Delay(180);
    private static void Require(bool test,string message){if(!test)throw new InvalidOperationException(message);}
}

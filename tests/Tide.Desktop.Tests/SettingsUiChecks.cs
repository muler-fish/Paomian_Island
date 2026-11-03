using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class SettingsUiChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        await SpoilerGeometryChecks.RunAsync(owner, output, capture);
        var clipboard = Clipboard.GetDataObject();
        var body = new PostBody { Html = "[h]<font color='red'>秘密 iii WWW</font>😀<br>第二行 <b>加粗</b>[/h]", HideSpoilers = true, FontSize = 24 };
        var preview = new PostPreview { Html = "前文[h]这里是中文 WWW iii 空格😀<br>第二行[/h]尾文", HideSpoilers = true, FontSize = 24, Margin = new(0, 20, 0, 0) };
        var sample = new Window { Owner = owner, Width = 620, Height = 400, ShowInTaskbar = false, Content = new StackPanel { Margin = new(20), Children = { body, preview } } };
        sample.SetResourceReference(Window.BackgroundProperty, "Canvas"); sample.Show();
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); await Task.Delay(60); sample.UpdateLayout();
                Require(EmojiChecks.ColorPixels(body) == 0, "hidden colored content leaked");
                Require(!EmojiText.Read(body.Document).Contains("秘密") && preview.Inlines.OfType<SpoilerSpan>().Any(), "reader/preview mask missing");
                body.SelectAll(); body.Copy(); Require(!Clipboard.GetText().Contains("秘密") && !Clipboard.ContainsData(DataFormats.Rtf), "copy exposed hidden text");
                capture(sample, Path.Combine(output, "spoiler-selected-" + theme + ".png"));
            }
            var hidden = body.Document.Blocks.OfType<Paragraph>().Single().Inlines.OfType<SpoilerSpan>().Single();
            var inside = hidden.ContentStart.GetPositionAtOffset(4)!;
            Require(!EmojiText.Read(inside, hidden.ContentEnd).Contains("秘密"), "partial selection exposed hidden text");
            hidden.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(sample)!, 0, Key.Space) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Task.Delay(60);
            Require(!hidden.Hidden && EmojiText.Read(body.Document).Contains("秘密 iii WWW😀") && EmojiChecks.ColorPixels(body) > 0, "keyboard reveal failed");
            capture(sample, Path.Combine(output, "spoiler-revealed.png"));
        }
        finally { sample.Close(); if (clipboard is not null) Clipboard.SetDataObject(clipboard, true); }

        var settings = new SettingsWindow(owner.Model) { Owner = owner, ShowInTaskbar = false };
        settings.Show(); await Task.Delay(100);
        try
        {
            foreach (var title in new[] { "阅读", "外观", "网络", "缓存", "关于" })
                Require(UiRefinementChecks.Descendants<TextBlock>(settings).Any(t => t.Text == title), "settings category absent: " + title);
            var routes = UiRefinementChecks.Descendants<ComboBox>(settings).Where(c => c.Items.Contains("直连")).ToArray();
            Require(routes.Length == 2, "independent network controls absent");
            routes[0].SelectedIndex = 1; routes[1].SelectedIndex = 0;
            var saved = owner.Model.Store.Load();
            Require(!saved.IslandUseProxy && saved.UpdateUseProxy && !NetworkClients.IslandUseProxy && NetworkClients.UpdateUseProxy, "proxy choices not independent/persistent");
            var cacheKey = "images/" + new string('C', 64) + ".bin"; owner.Model.Store.Cache.Write(cacheKey, new byte[125000]);
            var cacheTitle = UiRefinementChecks.Descendants<TextBlock>(settings).Single(t => t.Text == "缓存");
            var scroll = UiRefinementChecks.Descendants<ScrollViewer>(settings).First(s => s.Content is StackPanel);
            scroll.ScrollToVerticalOffset(cacheTitle.TranslatePoint(new Point(), (UIElement)scroll.Content).Y); await Task.Delay(60);
            var limit = UiRefinementChecks.Descendants<TextBox>(settings).Single(t => t.Width == 90); limit.Text = "2";
            Click(settings, "应用上限"); await Wait(() => owner.Model.State.CacheLimitMb == 2);
            Click(settings, "刷新占用"); await Task.Delay(80);
            Require(UiRefinementChecks.Descendants<TextBlock>(settings).Any(t => t.Text.Contains("0.13 MB / 2 MB")), "cache usage missing");
            capture(settings, Path.Combine(output, "settings-cache-dark.png"));
            Click(settings, "清理缓存"); await Wait(() => owner.Model.Store.Cache.SizeBytes() == 0);
            Require(owner.Model.Store.Load().CacheLimitMb == 2 && Directory.Exists(owner.Model.Store.Root), "cache clear damaged settings");
            var about = UiRefinementChecks.Descendants<TextBlock>(settings).Single(t => t.Text == "关于"); scroll.ScrollToVerticalOffset(about.TranslatePoint(new Point(), (UIElement)scroll.Content).Y); await Task.Delay(80);
            capture(settings, Path.Combine(output, "settings-about-dark.png"));
            App.ApplyTheme("light"); await Task.Delay(60); capture(settings, Path.Combine(output, "settings-about-light.png"));
        }
        finally { settings.Close(); }

        var pixels = new byte[120 * 80 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i] = 50; pixels[i + 1] = 120; pixels[i + 2] = 200; pixels[i + 3] = 255; }
        var source = BitmapSource.Create(120, 80, 96, 96, PixelFormats.Bgra32, null, pixels, 480);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); using var data = new MemoryStream(); encoder.Save(data);
        var viewer = new ImageWindow([new Post { Id = 42, ImagePath = "fixture", Extension = ".png" }], 42, (_, _) => Task.FromResult(data.ToArray())) { Owner = owner, ShowInTaskbar = false, Width = 660, Height = 460 };
        viewer.Show(); await Wait(() => UiRefinementChecks.Descendants<Image>(viewer).Any(i => i.Source is not null));
        try
        {
            var image = UiRefinementChecks.Descendants<Image>(viewer).Single(i => i.Source is not null); var original = image.Source;
            foreach (var theme in new[] { "light", "dark", "light" })
            {
                App.ApplyTheme(theme); await Task.Delay(60);
                var canvas = UiRefinementChecks.Descendants<Canvas>(viewer).Single(c => c.Children.Contains(image));
                Require(((SolidColorBrush)canvas.Background).Color == ((SolidColorBrush)Application.Current.Resources["Canvas"]).Color && ReferenceEquals(image.Source, original), "image theme sync changed content or failed");
                capture(viewer, Path.Combine(output, "viewer-" + theme + ".png"));
            }
        }
        finally { viewer.Close(); }
        Console.WriteLine("PASS spoiler visual/copy/reveal; grouped settings and cache controls; proxy persistence; image live theme sync");
    }
    private static void Click(Window w, string text) => UiRefinementChecks.Descendants<Button>(w).Single(b => Equals(b.Content, text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task Wait(Func<bool> ready) { for (var i = 0; i < 100 && !ready(); i++) await Task.Delay(30); Require(ready(), "operation timed out"); }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class BrandingUiChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture, bool assetsOnly = false)
    {
        if (assetsOnly) { await BrandAssetsAsync(owner, output, capture); return; }
        var vm = owner.Model;
        await vm.OpenThreadAsync(101);
        var columns = (Grid)owner.FindName("ContentColumns"); var layout = (Grid)columns.Parent;
        Require(owner.Title == AppIdentity.WindowTitle && layout.RowDefinitions.Count == 2 && Grid.GetRow(columns) == 1, "brand or status row");
        Require(!UiRefinementChecks.Descendants<TextBlock>(owner).Any(t => t.Text.Contains("潮汐") || t.Text.Contains("非官方客户端")), "old display text/status footer remains");
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme);
            foreach (var (width, height) in new[] { (900d, 600d), (1280d, 800d) })
            {
                owner.Width = width; owner.Height = height; await Pause();
                Require(Math.Abs(columns.TranslatePoint(new Point(0, columns.ActualHeight), layout).Y - layout.ActualHeight) < 1, "unused footer space");
                foreach (var name in new[] { "RefreshListButton", "RefreshThreadButton" })
                {
                    var button = (Wpf.Ui.Controls.Button)owner.FindName(name);
                    Require(button.Appearance == Wpf.Ui.Controls.ControlAppearance.Secondary && ReferenceEquals(button.Style, owner.FindResource("RefreshIconButton")), "refresh priority/style");
                    Require(button.ActualWidth == 32 && button.ActualHeight == 32 && button.Template is not null, "refresh geometry/template");
                    button.IsEnabled = false; await Pause(); Require(!button.IsEnabled, "disabled refresh state"); button.ClearValue(UIElement.IsEnabledProperty);
                }
                capture(owner, Path.Combine(output, $"brand-{theme}-{width}.png"));
            }
        }
        vm.RefreshThreadCommand.Execute(null); await Pause(); Require(vm.Original?.Id == 101 && !vm.HasError, "refresh command changed");
        var settings = new SettingsWindow(vm) { Owner = owner, ShowInTaskbar = false }; settings.Show(); await Pause();
        Require(UiRefinementChecks.Descendants<TextBlock>(settings).Any(t => t.Text == "泡面岛") && !UiRefinementChecks.Descendants<TextBlock>(settings).Any(t => t.Text.Contains("潮汐")), "about name"); settings.Close();
        var protectedText = SecretProtection.Protect("fixture-only-not-a-cookie"); Require(SecretProtection.Unprotect(protectedText) == "fixture-only-not-a-cookie", "protection compatibility");
        await ImageCopyAsync(owner, output, capture);
        Console.WriteLine("PASS brand, reclaimed main content height, secondary refresh, about name, protected data, image copy success/failure");
    }
    private static async Task BrandAssetsAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var vm = owner.Model;
        Require(AppIdentity.EnglishName == "paomian island", "English name");
        var logo = (Viewbox)owner.FindName("BrandLogo");
        var appIcon = (ImageSource)owner.FindResource("AppLogo");
        Require(ReferenceEquals(owner.Icon, appIcon), "app icon changed");
        var mark = UiRefinementChecks.Descendants<System.Windows.Shapes.Path>(logo).Single();
        Require(mark.Fill is null && ((Canvas)logo.Child).Background is null, "header has a solid tile background");
        var settings = (Button)owner.FindName("SettingsButton"); var theme = (Button)owner.FindName("ThemeButton");
        Require(Grid.GetColumn(settings) == 0 && Grid.GetColumn(theme) == 1, "settings/theme positions");
        Require(UiRefinementChecks.Descendants<TextBlock>(settings).Any(t => t.Text == "设置"), "settings label");
        var columns = (Grid)owner.FindName("ContentColumns"); columns.ColumnDefinitions[0].Width = new GridLength(148);
        vm.State.Theme = "light"; App.ApplyTheme("light"); vm.AppearanceChanged();
        foreach (var expected in new[] { "dark", "light" })
        {
            theme.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Pause();
            Require(((SolidColorBrush)mark.Stroke).Color == ((SolidColorBrush)owner.FindResource("Accent")).Color, "header mark does not follow theme");
            Require(vm.State.Theme == expected && vm.Store.Load().Theme == expected, "theme toggle persistence");
            var moon = (Wpf.Ui.Controls.SymbolIcon)owner.FindName("ThemeMoon");
            Require(moon.Filled == (expected == "dark"), "moon state");
            if (expected == "dark") Require(((SolidColorBrush)moon.Foreground).Color == ((SolidColorBrush)owner.FindResource("FavoriteStar")).Color, "yellow moon");
            var left = settings.TranslatePoint(new Point(settings.ActualWidth, 0), owner);
            var right = theme.TranslatePoint(new Point(), owner); Require(left.X <= right.X && theme.ActualWidth >= 32, "narrow navigation overlap");
            capture(owner, Path.Combine(output, "logo-navigation-" + expected + ".png"));
        }
        Exception? failure = null;
        _ = owner.Dispatcher.BeginInvoke(new Action(async () =>
        {
            var dialog = Application.Current.Windows.OfType<SettingsWindow>().Single();
            try
            {
                Require(ReferenceEquals(dialog.Icon, appIcon), "settings window icon");
                var english = UiRefinementChecks.Descendants<TextBlock>(dialog).Single(t => t.Text == AppIdentity.EnglishName);
                Require(UiRefinementChecks.Descendants<Image>(dialog).Any(i => ReferenceEquals(i.Source, appIcon)), "about logo");
                english.BringIntoView(); await Pause(); capture(dialog, Path.Combine(output, "about-brand.png"));
            }
            catch (Exception ex) { failure = ex; }
            finally { dialog.Close(); }
        }));
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (failure is not null) throw failure;
        Console.WriteLine("PASS approved logo resources, English name, swapped controls, theme persistence, yellow moon, narrow navigation, settings entry and about");
    }

    private static async Task ImageCopyAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var oldClipboard = Clipboard.GetDataObject(); var fail = false;
        GetCursorPos(out var cursor);
        var bitmap = BitmapSource.Create(160, 100, 96, 96, PixelFormats.Bgra32, null, Enumerable.Range(0, 160 * 100).SelectMany(_ => new byte[] { 50, 120, 200, 255 }).ToArray(), 640);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var data = new MemoryStream(); encoder.Save(data);
        var viewer = new ImageWindow([new Post { Id = 101, ImagePath = "fixture", Extension = ".png" }], 101,
            (_, _) => Task.FromResult(data.ToArray()), copyImage: image => { if (fail) throw new ExternalException("clipboard locked fixture"); Clipboard.SetImage(image); }) { Owner = owner, ShowInTaskbar = false, Width = 700, Height = 520 };
        viewer.Show(); await Pause();
        try
        {
            var image = UiRefinementChecks.Descendants<Image>(viewer).Single(i => i.Source is not null);
            Require(image.IsHitTestVisible && image.ContextMenu is not null, "image context menu absent");
            var canvas = UiRefinementChecks.Descendants<Canvas>(viewer).Single(c => c.Children.Contains(image));
            Require(canvas.ContextMenu is null, "blank canvas gets image menu");
            var menu = image.ContextMenu!; var copy = menu.Items.OfType<MenuItem>().Single(); Require(Equals(copy.Header, "复制"), "unexpected menu actions");
            void Copy() { menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent)); copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent)); }
            viewer.ZoomAt(2, new Point(120, 100)); viewer.PanBy(-20, -10);
            // Native input needs the test window above the editor and its transformed image rendered first.
            viewer.Topmost = true; viewer.Activate(); await Pause();
            var point = image.PointToScreen(new Point(image.ActualWidth / 2, image.ActualHeight / 2));
            SetCursorPos((int)point.X, (int)point.Y); await Pause();
            Require(image.IsMouseOver, "native test cursor did not reach the image");
            mouse_event(0x0008, 0, 0, 0, UIntPtr.Zero); await Task.Delay(40); mouse_event(0x0010, 0, 0, 0, UIntPtr.Zero);
            await Pause(); Require(menu.IsOpen, "right click on displayed image did not open menu");
            copy.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
            Require(Clipboard.ContainsImage() && Clipboard.GetImage() is { PixelWidth: 160, PixelHeight: 100 }, "clipboard contains viewport instead of original pixels");
            Require(UiRefinementChecks.Descendants<TextBlock>(viewer).Any(t => t.Text == "图片已复制"), "success feedback missing");
            await Task.Delay(300); // Capture after the entrance animation, while the short success notice is visible.
            var notice = UiRefinementChecks.Descendants<TextBlock>(viewer).Single(t => t.Text == "图片已复制");
            var bubble = UiRefinementChecks.Descendants<Border>(viewer).Single(b => b.Child is ScrollViewer scroll && ReferenceEquals(scroll.Content, notice));
            Require(bubble.Opacity > .9 && bubble.IsVisible, "success feedback never became visible");
            capture(viewer, Path.Combine(output, "image-copy-success.png"));
            fail = true; Copy(); await Pause();
            Require(UiRefinementChecks.Descendants<TextBlock>(viewer).Any(t => t.Text.StartsWith("复制图片失败")), "failure feedback missing");
            capture(viewer, Path.Combine(output, "image-copy-failed.png"));
            image.Source = null; menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent)); Require(!copy.IsEnabled, "unloaded image copy enabled");
        }
        finally { viewer.Close(); SetCursorPos(cursor.X, cursor.Y); if (oldClipboard is not null) Clipboard.SetDataObject(oldClipboard, true); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    private static async Task Pause() { await Task.Delay(100); }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}

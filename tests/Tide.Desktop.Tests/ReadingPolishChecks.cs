using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ReadingPolishChecks
{
    internal const string Sample = "“多少钱？”<br><br>“两、三千，三千……先生。”<br><br>“市场价。”<br>南京，第71集团军合成第七十九旅，栖霞区<br>马群营区，合成第三营";

    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var vm = new MainViewModel(new XIslandApi(new HttpClient(new FixtureHandler())), new LocalStore(Path.Combine(output, "reading-profile")));
        vm.State.ShowImages = false; vm.State.FontSize = 22; vm.AppearanceChanged();
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        window.Show(); window.Activate();
        GetCursorPos(out var cursor);
        try
        {
            await vm.SelectForumAsync(new(4, "综合版1", "综合")); window.UpdateLayout(); await Task.Delay(100);
            var list = (ListBox)window.FindName("ThreadList");
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var preview = UiRefinementChecks.Descendants<PostPreview>(item).Single();
            await ClickAndLeave(window, item);
            for (var i = 0; vm.ThreadBusy && i < 40; i++) await Task.Delay(25);
            window.UpdateLayout();
            var listSelected = item.IsSelected;
            var selectedFill = Fill((Border)item.Template.FindName("Item", item));
            Require(listSelected && vm.Original?.Id == 101, "真实点击未选中并打开目标串");
            var reader = (ListBox)window.FindName("ReaderList");
            var bodies = UiRefinementChecks.Descendants<PostBody>(reader).ToArray();
            Require(bodies.Length >= 2, "未加载关联的串内正文");
            Require(EmojiText.Read(bodies[0].Document.ContentStart, bodies[0].Document.ContentEnd).Contains("三千……先生。"), "正文标点或原文被更改");
            var reply = bodies.Last(); reply.SelectAll();
            Require(EmojiText.Read(reply.Selection.Start, reply.Selection.End).Contains("😀"), "正文选区丢失彩色 emoji 原文");
            Require(reply.Document.Blocks.OfType<System.Windows.Documents.Paragraph>().SelectMany(p => p.Inlines).Any(i => i is Emoji.Wpf.EmojiInline), "emoji 未保留独立彩色绘制");
            foreach (var target in new DependencyObject[] { preview }.Concat(bodies).Concat(bodies.Select(b => b.Document)))
                Require(TextOptions.GetTextFormattingMode(target) == TextFormattingMode.Ideal && TextOptions.GetTextRenderingMode(target) == TextRenderingMode.Auto
                    && TextOptions.GetTextHintingMode(target) == TextHintingMode.Animated, "阅读渲染设置未覆盖预览/正文/文档");
            Require(preview.FontFamily.Equals(AppFonts.Family) && bodies.All(b => b.FontFamily.Equals(AppFonts.Family)), "未保留统一 MiSans 复合字体");
            Require(preview.ActualHeight <= preview.FontSize * 12 + 1 && preview.TextTrimming == TextTrimming.CharacterEllipsis, "正文渲染修改破坏八行上限或省略");
            reply.Selection.Select(reply.Document.ContentStart, reply.Document.ContentStart);
            var home = (Button)window.FindName("HomeTimelineButton");
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); await Hover(item);
                Require(Fill((Border)item.Template.FindName("Item", item)) == Fill((Brush)Application.Current.FindResource("Hover")), "阅读项悬停反馈丢失");
                await Hover(home);
                Require(Fill((Border)home.Template.FindName("QuietChrome", home)) == Fill((Brush)Application.Current.FindResource("Hover")), "按钮悬停反馈丢失");
                await Leave(window);
                Require(IsTransparent(Fill((Border)item.Template.FindName("Item", item))), "主题切换后阅读项残留选中底色");
                var readerItem = (ListBoxItem)reader.ItemContainerGenerator.ContainerFromIndex(0);
                readerItem.IsSelected = true;
                Require(IsTransparent(Fill(UiRefinementChecks.Descendants<Border>(readerItem).Single(b => b.Name == "PostRow"))), "串内选中项残留底色");
                capture(window, Path.Combine(output, "reading-" + theme + ".png"));
                CaptureScreen(window, Path.Combine(output, "reading-native-" + theme + ".png"));
            }
            await ClickAndLeave(window, home);
            var focusedFill = Fill((Border)home.Template.FindName("QuietChrome", home));
            var report = new { selectedFill, focusedFill, ListSelected = listSelected, HomeFocused = home.IsKeyboardFocused,
                Formatting = TextOptions.GetTextFormattingMode(preview).ToString(), Rendering = TextOptions.GetTextRenderingMode(preview).ToString(),
                Font = preview.FontFamily.Source, Dpi = VisualTreeHelper.GetDpi(window).PixelsPerInchX };
            File.WriteAllText(Path.Combine(output, "reading-polish-results.json"), JsonSerializer.Serialize(report));
            capture(window, Path.Combine(output, "after-click.png"));
            Require(IsTransparent(selectedFill), "串列表点击后仍残留选中色：" + selectedFill);
            Require(IsTransparent(focusedFill), "鼠标移开后按钮仍残留悬停色：" + focusedFill);
            Require(home.IsKeyboardFocused && home.FocusVisualStyle is not null, "清理鼠标反馈时丢失键盘焦点或焦点样式");
            var toggle = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("OnlyPoButton");
            toggle.IsChecked = true; window.UpdateLayout();
            Require(Fill(toggle.Background) == Fill((Brush)Application.Current.FindResource("PoBackground")), "真正的 PO 开关选中态被去掉");
            Console.WriteLine("PASS: real click opens thread; hover/leave and selected rows in both themes; keyboard focus and PO state retained; MiSans Auto/Animated reading, eight-line limit, text selection and color emoji preserved.");
        }
        finally { SetCursorPos(cursor.X, cursor.Y); window.Close(); App.ApplyTheme("light"); }
    }

    public static async Task CompareFontsAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        // 相同字库、字号和内容，仅改变排版/抗锯齿方式，保留为可查看的诊断对照。
        foreach (var theme in new[] { "dark", "light" })
        {
            App.ApplyTheme(theme); var grid = new Grid();
            grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
            grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new());
            var variants = new[] { (TextRenderingMode.Auto, TextHintingMode.Fixed), (TextRenderingMode.ClearType, TextHintingMode.Fixed),
                (TextRenderingMode.Grayscale, TextHintingMode.Fixed), (TextRenderingMode.Grayscale, TextHintingMode.Animated) };
            for (var i = 0; i < variants.Length; i++)
            {
                var (render, hint) = variants[i]; var panel = new StackPanel { Margin = new(16) };
                panel.Children.Add(new TextBlock { Text = $"{render} / {hint}", FontSize = 13, Margin = new(0, 0, 0, 10) });
                var sample = new PostPreview { Html = Sample, FontSize = 22 };
                TextOptions.SetTextFormattingMode(sample, TextFormattingMode.Ideal); TextOptions.SetTextRenderingMode(sample, render); TextOptions.SetTextHintingMode(sample, hint); panel.Children.Add(sample);
                var small = new PostPreview { Html = "小字号对照：三千……先生。 第七十九旅，栖霞区 Aa 123", FontSize = 16, Margin = new(0, 14, 0, 0) };
                TextOptions.SetTextFormattingMode(small, TextFormattingMode.Ideal); TextOptions.SetTextRenderingMode(small, render); TextOptions.SetTextHintingMode(small, hint); panel.Children.Add(small);
                var border = new Border { Child = panel, BorderThickness = new(1) }; border.SetResourceReference(Border.BackgroundProperty, "Surface"); border.SetResourceReference(Border.BorderBrushProperty, "Line");
                Grid.SetRow(border, i / 2); Grid.SetColumn(border, i % 2); grid.Children.Add(border);
            }
            var sampleWindow = new Window { Owner = owner, ShowInTaskbar = false, Width = Math.Min(1240, SystemParameters.WorkArea.Width - 40),
                Height = Math.Min(810, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = grid };
            try { sampleWindow.Show(); sampleWindow.Activate(); await Task.Delay(250); capture(sampleWindow, Path.Combine(output, "font-comparison-" + theme + ".png")); CaptureScreen(sampleWindow, Path.Combine(output, "font-native-" + theme + ".png")); }
            finally { sampleWindow.Close(); }
        }
        App.ApplyTheme("light");
    }

    private static string Fill(Border border) => Fill(border.Background);
    private static string Fill(Brush brush) => brush is SolidColorBrush solid ? solid.Color.ToString() : brush.ToString();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool IsTransparent(string fill) => fill.StartsWith("#00", StringComparison.Ordinal);
    private static async Task Hover(FrameworkElement target)
    {
        var point = target.PointToScreen(new Point(Math.Min(80, target.ActualWidth / 2), Math.Min(18, target.ActualHeight / 2)));
        SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(100);
    }
    private static async Task Leave(Window window)
    {
        var point = window.PointToScreen(new Point(420, 22)); SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(100);
    }
    private static async Task ClickAndLeave(Window window, FrameworkElement target)
    {
        await Hover(target);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); await Task.Delay(100);
        await Leave(window);
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    internal static void CaptureScreen(Window window, string path)
    {
        // 只取自有测试窗口范围；RenderTargetBitmap 会降级为灰度，不能用它判断实际 ClearType 色边。
        GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds);
        var dc = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top); var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, dc, bounds.Left, bounds.Top, 0x00CC0020)) throw new InvalidOperationException("测试窗口截图失败");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, dc); }
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("专项不允许网络写操作");
            var post = new { id = 101, fid = 4, user_hash = "sample", content = Sample, title = "", ReplyCount = 2, Replies = new[] { new { id = 102, content = "关联正文验证。😀", user_hash = "reader" } } };
            var route = request.RequestUri!.AbsolutePath.Split('/').Last();
            var json = route is "showf" or "timeline" ? JsonSerializer.Serialize(new[] { post }) : JsonSerializer.Serialize(post);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}

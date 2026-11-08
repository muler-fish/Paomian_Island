using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>颜文字选择器 0.3.8：四列等宽、行高按行自适应、缩小后的高度、右键复制／隐藏、整理模式、就近悬停预览。只用隔离资料，不联网。</summary>
internal static class EmoticonLayoutChecks
{
    private const string Art = "   /\\_/\\   \n  ( o.o )  \n   > ^ <\n\n  __|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__|__  ";
    private const string LongLine = "(((( ；ﾟДﾟ)))ｶﾞｸｶﾞｸﾌﾞﾙﾌﾞﾙ这是一条很长很长的单行颜文字ヽ(`Д´)ﾉ";
    private const string Medium = "( ´_ゝ`)旁白：这一条会折成两三行";

    internal static async Task RunAsync(MainWindow owner, string output)
    {
        var store = new LocalStore(Path.Combine(output, "layout-profile"));
        using var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        vm.State.Forums = [new(4, "综合版1", "综合")];
        vm.State.Cookies = [new("test", "测试饼干", SecretProtection.Protect("synthetic-cookie"))]; vm.State.ActiveCookieId = "test";
        vm.State.CustomEmoticons = []; vm.State.HiddenEmoticons = []; vm.State.RecentEmoticons = [];
        EmoticonCatalog.Save(vm.State, null, Art, "");
        EmoticonCatalog.Save(vm.State, null, Art.Replace("o.o", "^.^"), "开心猫");
        EmoticonCatalog.Save(vm.State, null, LongLine, "");
        EmoticonCatalog.Save(vm.State, null, Medium, "");
        EmoticonCatalog.Save(vm.State, null, "(・∀・)ｲｲ!!", "");
        for (var i = 0; i < 24; i++) EmoticonCatalog.Save(vm.State, null, $"填充{i}(ﾟ∀ﾟ)", i % 2 == 0 ? "" : $"备注{i}");
        vm.Save();
        var report = new Dictionary<string, object>();
        var composer = new ComposerWindow(vm, 0) { Owner = owner, ShowInTaskbar = false };
        composer.Show(); await Pause(300);
        var menu = composer.Emoticons;
        var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single(e => AutomationProperties.GetName(e) == "发言正文");
        try
        {
            // 1. 四列、等宽、同行等高，高度约为旧版（400）的 65%–70%
            await Open(composer); await Category(menu, "全部");
            var surface = (FrameworkElement)menu.Popup.Child;
            var list = UiRefinementChecks.Descendants<ItemsControl>(surface).Single(i => AutomationProperties.GetName(i) == "颜文字菜单");
            Require(menu.ColumnCount == 4, "正常编辑窗口不是四列：" + menu.ColumnCount);
            Require(list.ActualHeight <= 270.5 && list.ActualHeight >= 140, "列表高度不在约 65%–70% 的范围：" + list.ActualHeight);
            Require(!UiRefinementChecks.Descendants<Button>(surface).Any(b => (AutomationProperties.GetName(b) ?? "").StartsWith("更多操作")), "仍有悬停三点按钮");
            var rows = CheckRows(surface);
            report["Columns"] = menu.ColumnCount; report["ListHeight"] = list.ActualHeight; report["SurfaceWidth"] = surface.ActualWidth; report["OldListHeight"] = 400;
            Snapshot(surface, Path.Combine(output, "grid-all-light.png"));
            ScreenShot(composer, menu, Path.Combine(output, "composer-picker-light.png"));

            // 2. 滚动全部条目：逐段滚到底，检查每屏的行不重叠、同行等高
            var scroll = UiRefinementChecks.Descendants<ScrollViewer>(list).First();
            var checkedRows = 0;
            for (var offset = 0.0; offset <= scroll.ScrollableHeight + 1; offset += 120)
            {
                scroll.ScrollToVerticalOffset(offset); list.UpdateLayout(); await Pause(30);
                checkedRows += CheckRows(surface).Count;
            }
            scroll.ScrollToEnd(); list.UpdateLayout(); await Pause(60);
            var last = menu.Shown[^1];
            Require(menu.Realize(last) is { IsVisible: true }, "滚到底看不到最后一个条目");
            report["ScrolledRowsChecked"] = checkedRows;

            // 3. 自定义分类：动态行高、超长单行、多行 ASCII Art 显示简称或截断，不撑高整行
            await Category(menu, "自定义");
            var custom = CheckRows(surface);
            var heights = custom.Select(r => Math.Round(r.Height)).Distinct().ToList();
            Require(heights.Count > 1, "不同行没有不同高度：" + string.Join(",", heights));
            Require(custom.All(r => r.Height <= 20 * 3 + 10 + 4 + 1), "有条目超出三行限制撑高整行");
            var artButton = menu.Realize(menu.Shown.Single(e => e.Text == Art))!;
            Require(((EmojiLabel)artButton.Content).Text.Contains("行") && ((EmojiLabel)artButton.Content).Text.Length <= 24, "无简称的 ASCII Art 没有显示截断预览");
            Require(((EmojiLabel)menu.Realize(menu.Shown.Single(e => e.Name == "开心猫"))!.Content).Text == "开心猫", "有简称的条目没有显示简称");
            report["CustomRowHeights"] = heights;
            Snapshot(surface, Path.Combine(output, "grid-custom-light.png"));

            // 4. 右键菜单：内置「复制／隐藏」，自定义「复制／查看完整内容／编辑／删除」；复制完整原文
            await Category(menu, "全部"); scroll.ScrollToTop(); await Pause(60);
            var builtin = menu.Shown.First(e => !e.IsLarge);
            var builtinMenu = menu.Realize(builtin)!.ContextMenu!;
            Require(Headers(builtinMenu).SequenceEqual(new[] { "复制", "隐藏" }), "内置条目右键菜单不对：" + string.Join("/", Headers(builtinMenu)));
            await Category(menu, "自定义");
            var artMenu = menu.Realize(menu.Shown.Single(e => e.Text == Art))!.ContextMenu!;
            Require(Headers(artMenu).SequenceEqual(new[] { "复制", "查看完整内容", "编辑", "删除" }), "自定义条目右键菜单不对：" + string.Join("/", Headers(artMenu)));
            string? previous = null;
            try { previous = Clipboard.ContainsText() ? Clipboard.GetText() : null; } catch (Exception) { }
            var beforeCopy = editor.Text;
            Click(artMenu, "复制"); await Pause(60);
            Require(Clipboard.GetText() == Art.Replace("\n", "\r\n") && editor.Text == beforeCopy && menu.Popup.IsOpen, "复制的不是完整原文，或复制时插入了正文");
            try { if (previous is null) Clipboard.Clear(); else Clipboard.SetText(previous); } catch (Exception) { }
            // 菜单截图：真正打开右键菜单并截屏
            var builtinButton = menu.Realize(menu.Shown[0])!;
            await Category(menu, "全部"); builtinButton = menu.Realize(menu.Shown.First(e => !e.IsLarge))!;
            builtinButton.ContextMenu!.PlacementTarget = builtinButton; builtinButton.ContextMenu.IsOpen = true; await Pause(200);
            Require(editor.Text == beforeCopy, "打开右键菜单时插入了颜文字");
            ScreenShot(composer, menu, Path.Combine(output, "context-menu-builtin.png"), builtinButton.ContextMenu);
            builtinButton.ContextMenu.IsOpen = false; await Pause();
            await Category(menu, "自定义");
            var artItem = menu.Realize(menu.Shown.Single(e => e.Text == Art))!;
            artItem.ContextMenu!.PlacementTarget = artItem; artItem.ContextMenu.IsOpen = true; await Pause(200);
            ScreenShot(composer, menu, Path.Combine(output, "context-menu-custom.png"), artItem.ContextMenu);
            artItem.ContextMenu.IsOpen = false; await Pause();

            // 5. 整理模式：底部入口可见，开启后左键打开管理菜单而不插入
            var manage = UiRefinementChecks.Descendants<Button>(surface).Single(b => Equals(b.Content, "整理自定义颜文字"));
            Require(manage.IsVisible, "自定义分类没有可见的整理入口");
            Click(manage); await Pause();
            var managed = menu.Realize(menu.Shown.Single(e => e.Text == Medium))!;
            Click(managed); await Pause(200);
            Require(managed.ContextMenu!.IsOpen && editor.Text == beforeCopy && menu.Popup.IsOpen, "整理模式下左键没有打开管理菜单或插入了正文");
            Require(Headers(managed.ContextMenu).Contains("编辑") && Headers(managed.ContextMenu).Contains("删除"), "整理菜单缺少编辑或删除");
            managed.ContextMenu.IsOpen = false; await Pause();
            Click(UiRefinementChecks.Descendants<Button>(surface).Single(b => Equals(b.Content, "完成整理"))); await Pause();

            // 6. 悬停预览：短暂停留后在条目正上方出现；离开后关闭；快速划过不弹出
            var top = menu.Realize(menu.Shown.Single(e => e.Text == Art))!;
            Hover(top, true); await Pause(500);
            Require(menu.Preview.IsOpen, "悬停大型颜文字没有预览");
            var preview = (FrameworkElement)menu.Preview.Child; var previewRect = ScreenRect(preview); var anchorRect = ScreenRect(top);
            var work = WindowPlacement.WorkArea(composer);
            Require(previewRect.Left >= work.Left - 1 && previewRect.Right <= work.Right + 1 && previewRect.Top >= work.Top - 1 && previewRect.Bottom <= work.Bottom + 1, "预览超出工作区");
            var above = previewRect.Bottom <= anchorRect.Top + 1;
            Require((above || previewRect.Top >= anchorRect.Bottom - 1) && previewRect.Left < anchorRect.Right && previewRect.Right > anchorRect.Left, $"预览不在条目上方或下方：{previewRect} / {anchorRect}");
            var shown = UiRefinementChecks.Descendants<EmojiLabel>(preview).Single(l => AutomationProperties.GetName(l) == "颜文字完整内容");
            Require(shown.Text == Art && shown.TextWrapping == TextWrapping.NoWrap && shown.FontFamily.Source.StartsWith("Consolas"), "预览改动了图案、自动折行或没有用等宽字体");
            Require(preview.ActualWidth <= 520.5 && preview.ActualHeight <= 360.5, "预览没有限制最大尺寸");
            report["TopRowPreviewAbove"] = above;
            ScreenShot(composer, menu, Path.Combine(output, "preview-hover-top-light.png"));
            Hover(top, false); await Pause(400);
            Require(!menu.Preview.IsOpen, "离开条目后预览没有关闭");
            // 短颜文字完整显示时不预览
            var shortButton = menu.Realize(menu.Shown.Single(e => e.Text == "(・∀・)ｲｲ!!"))!;
            Hover(shortButton, true); await Pause(500); Require(!menu.Preview.IsOpen, "完整显示的短颜文字也弹出了预览"); Hover(shortButton, false);
            // 被折行截断的单行长颜文字需要预览
            var longButton = menu.Realize(menu.Shown.Single(e => e.Text == LongLine))!;
            Hover(longButton, true); await Pause(500); Require(menu.Preview.IsOpen, "超长单行颜文字没有预览"); Hover(longButton, false); await Pause(400);
            // 快速划过全部可见条目
            var count = menu.PreviewShownCount;
            foreach (var button in UiRefinementChecks.Descendants<Button>(surface).Where(b => b.Tag is EmoticonEntry && b.IsVisible).ToList())
            { Hover(button, true); await Pause(25); Hover(button, false); }
            await Pause(400);
            Require(menu.PreviewShownCount == count && !menu.Preview.IsOpen, "快速划过时弹出了预览");
            // 列表底部的条目：滚到底再悬停
            UiRefinementChecks.Descendants<ScrollViewer>(list).First().ScrollToEnd(); list.UpdateLayout(); await Pause(80);
            var bottomEntry = menu.Shown.Last(e => e.Display != e.Text);
            var bottom = menu.Realize(bottomEntry)!;
            Hover(bottom, true); await Pause(500);
            Require(menu.Preview.IsOpen, "底部条目没有预览");
            previewRect = ScreenRect((FrameworkElement)menu.Preview.Child); anchorRect = ScreenRect(bottom);
            Require((previewRect.Bottom <= anchorRect.Top + 1 || previewRect.Top >= anchorRect.Bottom - 1) && previewRect.Left < anchorRect.Right && previewRect.Right > anchorRect.Left, "底部条目预览位置错误");
            ScreenShot(composer, menu, Path.Combine(output, "preview-hover-bottom-light.png"));
            Hover(bottom, false); await Pause(400);

            // 7. 插入多行图案后继续输入
            editor.Text = "前后"; editor.Select(1, 0); menu.Toggle(); await Pause();
            await Open(composer); await Category(menu, "自定义");
            await ComposerInputChecks.Invoke(menu.Realize(menu.Shown.Single(e => e.Text == Art))!);
            var inserted = "前" + Art.Replace("\n", "\r\n");
            Require(editor.Text == inserted + "后" && editor.CaretIndex == inserted.Length, "插入多行图案后正文或光标错误");

            // 8. 深色主题
            App.ApplyTheme("dark"); await Open(composer); await Category(menu, "全部"); await Pause();
            CheckRows((FrameworkElement)menu.Popup.Child);
            Snapshot((FrameworkElement)menu.Popup.Child, Path.Combine(output, "grid-all-dark.png"));
            await Category(menu, "自定义");
            var darkTop = menu.Realize(menu.Shown.Single(e => e.Text == Art))!;
            Hover(darkTop, true); await Pause(500);
            ScreenShot(composer, menu, Path.Combine(output, "preview-hover-dark.png"));
            Hover(darkTop, false); await Pause(400); menu.Toggle(); App.ApplyTheme("light"); await Pause();

            // 9. 较窄的编辑窗口：列宽不足时减少列数，条目不侵占相邻列
            composer.Width = 420; await Pause(200);
            await Open(composer); await Category(menu, "全部");
            var narrow = CheckRows((FrameworkElement)menu.Popup.Child);
            report["NarrowColumns"] = menu.ColumnCount; report["NarrowSurfaceWidth"] = ((FrameworkElement)menu.Popup.Child).ActualWidth;
            Require(menu.ColumnCount is >= 2 and <= 4 && narrow.Count > 0, "窄窗口列数异常");
            Snapshot((FrameworkElement)menu.Popup.Child, Path.Combine(output, "grid-narrow-light.png"));
            menu.Toggle();
            report["Dpi"] = VisualTreeHelper.GetDpi(composer).PixelsPerDip;

            // 10. 模拟 100%／125%／150% DPI（向编辑窗口发送 WM_DPICHANGED）和较矮的窗口：四列、列表不超出工作区
            var dpiReport = new List<object>();
            foreach (var (scale, height) in new[] { (1.0, 820.0), (1.25, 820.0), (1.5, 640.0), (1.5, 520.0) })
            {
                SetDpi(composer, scale, 640, height); await Pause(250);
                await Open(composer); await Category(menu, "全部"); await Pause();
                var popupSurface = (FrameworkElement)menu.Popup.Child;
                var dpiList = UiRefinementChecks.Descendants<ItemsControl>(popupSurface).Single(i => AutomationProperties.GetName(i) == "颜文字菜单");
                CheckRows(popupSurface);
                var popupRect = ScreenRect(popupSurface); var workArea = WindowPlacement.WorkArea(composer);
                Require(menu.ColumnCount == 4, $"{scale:P0} 下不是四列");
                Require(popupRect.Top >= workArea.Top - 1 && popupRect.Bottom <= workArea.Bottom + 1, $"{scale:P0}/{height} 下选择器超出工作区：{popupRect} / {workArea}");
                dpiReport.Add(new { Scale = scale, WindowHeight = height, WindowDpi = VisualTreeHelper.GetDpi(composer).DpiScaleX, ListHeight = dpiList.ActualHeight, SurfaceHeight = popupSurface.ActualHeight, menu.ColumnCount });
                ScreenShot(composer, menu, Path.Combine(output, $"dpi-{(int)(scale * 100)}-{(int)height}.png"));
                menu.Toggle(); await Pause();
            }
            report["DpiChecks"] = dpiReport;
            File.WriteAllText(Path.Combine(output, "emoticon-layout.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: four equal columns, per-row heights, ~2/3 list height, scroll all rows, no hover ⋯ button, copy/hide/edit/delete menu, manage mode, hover preview above item with delay, fast pass, top/bottom, insert, dark, narrow.");
            Console.WriteLine(JsonSerializer.Serialize(report));
        }
        finally { composer.Close(); App.ApplyTheme("light"); }
    }

    private sealed record RowInfo(double Top, double Height);

    // 检查当前已生成的行：每行条目等宽（列宽一致）、同行等高且顶端对齐、行与行不重叠，文字不超出所在按钮。
    private static List<RowInfo> CheckRows(FrameworkElement surface)
    {
        surface.UpdateLayout();
        var rows = new List<RowInfo>();
        foreach (var grid in UiRefinementChecks.Descendants<Grid>(surface).Where(g => g.Children.OfType<Button>().Any(b => b.Tag is EmoticonEntry) && g.IsVisible))
        {
            var buttons = grid.Children.OfType<Button>().ToList();
            var top = grid.TranslatePoint(new Point(), surface).Y;
            var columnWidth = grid.ColumnDefinitions[0].ActualWidth;
            Require(grid.ColumnDefinitions.All(c => Math.Abs(c.ActualWidth - columnWidth) < 0.6), "列宽不一致：" + string.Join(",", grid.ColumnDefinitions.Select(c => c.ActualWidth.ToString("F2"))) + $" grid {grid.ActualWidth} parent {((FrameworkElement)VisualTreeHelper.GetParent(grid)).ActualWidth} vsp {UiRefinementChecks.Descendants<VirtualizingStackPanel>(surface).First().ActualWidth} surface {surface.ActualWidth}");
            Require(buttons.All(b => Math.Abs(b.ActualHeight - buttons[0].ActualHeight) < 0.6 && Math.Abs(b.TranslatePoint(new Point(), grid).Y - buttons[0].TranslatePoint(new Point(), grid).Y) < 0.6), "同一行条目高度或顶端不一致");
            Require(buttons.All(b => b.ActualWidth <= columnWidth + 0.6 && ((FrameworkElement)b.Content).ActualWidth <= b.ActualWidth), "条目侵占了相邻列");
            rows.Add(new RowInfo(top, grid.ActualHeight));
        }
        rows = rows.OrderBy(r => r.Top).ToList();
        for (var i = 1; i < rows.Count; i++) Require(rows[i].Top >= rows[i - 1].Top + rows[i - 1].Height - 0.6, "行与行重叠");
        return rows;
    }

    private static IEnumerable<string> Headers(ContextMenu menu) => menu.Items.OfType<MenuItem>().Select(i => i.Header.ToString()!);
    private static void Click(ContextMenu menu, string header) => menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, header)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Hover(UIElement element, bool enter) => element.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = enter ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent });
    private static async Task Open(ComposerWindow composer) { if (!composer.Emoticons.Popup.IsOpen) await ComposerInputChecks.Invoke(ComposerInputChecks.Tool(composer, "颜文字")); Require(composer.Emoticons.Popup.IsOpen, "颜文字菜单未打开"); }
    private static async Task Category(EmoticonMenu menu, string label)
    { UiRefinementChecks.Descendants<RadioButton>(menu.Popup.Child).Single(r => Equals(r.Content, label)).IsChecked = true; await Pause(); }
    private static Rect ScreenRect(FrameworkElement element)
    {
        var toDip = PresentationSource.FromVisual(element)!.CompositionTarget!.TransformFromDevice;
        return new Rect(toDip.Transform(element.PointToScreen(new Point())), toDip.Transform(element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight))));
    }
    private static void Snapshot(FrameworkElement surface, string path)
    {
        surface.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(surface);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(surface.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(surface); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    // 屏幕截图：编辑窗口、选择器、预览和右键菜单是各自的窗口，按它们的并集区域从屏幕取图。
    private static void ScreenShot(Window composer, EmoticonMenu menu, string path, ContextMenu? context = null)
    {
        var area = Physical(composer);
        foreach (var element in new[] { menu.Popup.IsOpen ? menu.Popup.Child : null, menu.Preview.IsOpen ? menu.Preview.Child : null, context?.IsOpen == true ? context : null })
            if (element is FrameworkElement { IsVisible: true } visible) area.Union(Physical(visible));
        var x = (int)area.Left; var y = (int)area.Top; var w = (int)Math.Ceiling(area.Width); var h = (int)Math.Ceiling(area.Height);
        var dc = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, w, h); var old = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, w, h, dc, x, y, 0x00CC0020)) throw new InvalidOperationException("屏幕截图失败");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, dc); }
        static Rect Physical(FrameworkElement element) => new(element.PointToScreen(new Point()), element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight)));
    }
    // 与 CookieManagerChecks 相同：发送 WM_DPICHANGED 让 WPF 按指定 DPI 重新布局。
    private static void SetDpi(Window window, double scale, double width, double height)
    {
        var rect = new NativeRect { Right = (int)(width * scale), Bottom = (int)(height * scale) };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rect, memory, false); var dpi = (int)(96 * scale); SendMessage(new WindowInteropHelper(window).Handle, 0x02E0, new((dpi << 16) | dpi), memory); window.Width = width; window.Height = height; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    private static Task Pause(int ms = 120) => Task.Delay(ms);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class GifClipboardChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var bytes = GifChecks.Fixture(loop: true);
        var root = Path.Combine(output, "clipboard-files"); Directory.CreateDirectory(root);
        var stale = Path.Combine(root, "No.1-" + new string('A', 64) + ".gif");
        File.WriteAllBytes(stale, bytes); File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-8));
        var unrelated = Path.Combine(root, "unrelated.gif"); File.WriteAllBytes(unrelated, bytes);
        File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-8));
        var original = Clipboard.GetDataObject(); uint sequence = 0;
        var post = new Post { Id = 901, ImagePath = "fixture/copy", Extension = ".gif" };
        var pngEncoder = new PngBitmapEncoder(); pngEncoder.Frames.Add(BitmapFrame.Create(ImageService.Decode(bytes)));
        using var pngStream = new MemoryStream(); pngEncoder.Save(pngStream); var png = pngStream.ToArray();
        var next = post with { Id = 902, Extension = ".png" };
        var fail = false; BitmapSource? staticCopy = null;
        var window = new ImageWindow([post, next], post.Id,
            (url, _) => Task.FromResult(url == next.ImageUrl() ? png : bytes),
            copyImage: image => staticCopy = image,
            copyGif: (image, id) =>
            {
                if (fail) throw new ExternalException("clipboard busy fixture");
                GifClipboard.Copy(image, id, root); sequence = GetClipboardSequenceNumber();
            }) { Owner = owner, ShowInTaskbar = false };
        window.Show();
        try
        {
            await Wait(() => ViewerImage(window).Source is WriteableBitmap);
            Click(window, "+"); Click(window, "旋转");
            Copy(ViewerImage(window)); await Task.Delay(80);
            var files = Clipboard.GetFileDropList();
            Require(files.Count == 1 && File.Exists(files[0]), "剪贴板没有可粘贴的 GIF 文件");
            var file = files[0]!;
            Require(file.StartsWith(root, StringComparison.OrdinalIgnoreCase) && file.EndsWith(".gif"), "剪贴板文件位置或格式错误");
            Require(File.ReadAllBytes(file).SequenceEqual(bytes), "文件粘贴格式丢失 GIF 帧或元数据");
            Require(staticCopy is null && !Clipboard.ContainsImage(), "GIF 被同时转为静态位图");
            foreach (var format in new[] { "image/gif", "GIF" })
                Require(ReadRaw(format, bytes.Length).SequenceEqual(bytes), "原生剪贴板格式没有保留完整字节：" + format);
            Require(ReadRaw("Preferred DropEffect", 4).SequenceEqual(BitConverter.GetBytes(1)), "文件粘贴不是复制语义");
            using (var stream = new MemoryStream(File.ReadAllBytes(file)))
                Require(new GifBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Count == 4, "复制后的 GIF 帧数不完整");
            Require(!File.Exists(stale) && File.Exists(unrelated), "临时文件清理范围不正确");
            Require(Children<TextBlock>(window).Any(t => t.Text == "GIF 已复制"), "GIF 成功气泡未显示");
            Copy(ViewerImage(window));
            Require(Clipboard.GetFileDropList()[0] == file && Directory.GetFiles(root, "No.*.gif").Length == 1, "重复复制没有复用文件");
            fail = true; Copy(ViewerImage(window)); await Task.Delay(80);
            Require(Children<TextBlock>(window).Any(t => t.Text.Contains("复制图片失败")), "GIF 失败气泡未显示");
            Require(ReadRaw("image/gif", bytes.Length).SequenceEqual(bytes), "复制失败破坏了已有剪贴板");
            capture(window, Path.Combine(output, "gif-copy-feedback.png"));
            Click(window, "›"); await Wait(() => ViewerImage(window).Source is BitmapImage);
            Copy(ViewerImage(window));
            Require(staticCopy is { IsFrozen: true, PixelWidth: 80, PixelHeight: 40 }, "普通图片复制回归");
            window.Close(); await Task.Delay(50);
            Require(File.Exists(file) && ReadRaw("GIF", bytes.Length).SequenceEqual(bytes), "关闭看图窗口后 GIF 无法粘贴");
            File.Copy(file, Path.Combine(output, "pasted.gif"), overwrite: true);
            Require(File.ReadAllBytes(Path.Combine(output, "pasted.gif")).SequenceEqual(bytes), "粘贴副本不同于原文件");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: raw image/gif and GIF bytes; file-drop GIF with four frames; copy effect; deduplication and scoped cleanup; window-close persistence; success/failure feedback; static image copying.\n");
            Console.WriteLine(File.ReadAllText(Path.Combine(output, "result.txt")));
        }
        finally
        {
            window.Close();
            // 只在剪贴板仍为测试所写时恢复；用户中途复制的新内容不覆盖。
            if (sequence != 0 && GetClipboardSequenceNumber() == sequence)
            { if (original is null) Clipboard.Clear(); else Clipboard.SetDataObject(original, copy: true); }
        }
    }
    private static byte[] ReadRaw(string format, int length)
    {
        var id = (uint)DataFormats.GetDataFormat(format).Id;
        Require(OpenClipboard(IntPtr.Zero), "无法读取测试剪贴板");
        try
        {
            var handle = GetClipboardData(id);
            Require(handle != IntPtr.Zero && GlobalSize(handle).ToUInt64() >= (ulong)length, "剪贴板原始数据不足：" + format);
            var pointer = GlobalLock(handle); Require(pointer != IntPtr.Zero, "无法锁定剪贴板数据");
            try { var result = new byte[length]; Marshal.Copy(pointer, result, 0, length); return result; }
            finally { GlobalUnlock(handle); }
        }
        finally { CloseClipboard(); }
    }
    private static void Copy(Image image)
    {
        var menu = image.ContextMenu;
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
        ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        menu.RaiseEvent(new RoutedEventArgs(ContextMenu.ClosedEvent));
    }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject => UiRefinementChecks.Descendants<T>(parent);
    private static Image ViewerImage(Window window) => Children<Image>(window).First(i => i.Parent is Canvas);
    private static void Click(Window window, string tag) => Children<Button>(window).Single(b => Equals(b.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Wait(Func<bool> condition) { for (var i = 0; i < 100 && !condition(); i++) await Task.Delay(30); Require(condition(), "等待 GIF 复制状态超时"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
}

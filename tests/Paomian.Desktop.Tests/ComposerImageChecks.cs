using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static partial class ComposerImageChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture, bool real, bool explorer)
    {
        var root = Directory.CreateDirectory(Path.Combine(output, "run-" + Guid.NewGuid().ToString("N"))).FullName;
        var path = Path.Combine(root, "测试附图.png"); var bitmap = Fixture(); var bytes = Encode(bitmap); File.WriteAllBytes(path, bytes);
        var store = new LocalStore(Path.Combine(root, "profile")); var handler = new Transport();
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        vm.State.Forums = [new(4, "测试版", "")]; vm.State.Cookies = [new("a", "测试", SecretProtection.Protect("fixture-1")), new("b", "备用", SecretProtection.Protect("fixture-2"))]; vm.State.ActiveCookieId = "a";
        var log = new List<string>();
        void Pass(string text) { log.Add(text); Console.WriteLine("PASS: " + text); }
        var original = await ClipboardAccess(Clipboard.GetDataObject);
        uint sequence = 0;
        async Task Copy(DataObject data)
        {
            sequence = await ClipboardAccess(() => { Clipboard.SetDataObject(data, true); return GetClipboardSequenceNumber(); });
            await Task.Delay(80); // 给系统剪贴板监听器让出一次读取机会。
        }
        var composer = Open(real && explorer ? 127 : 0);
        ComposerWindow Open(long thread) { var w = new ComposerWindow(vm, thread) { Owner = owner, ShowInTaskbar = false }; w.Show(); return w; }
        try
        {
            if (real && explorer)
            {
                All<EmojiEditor>(composer).Single().Text = "资源管理器附图";
                var selected = await ExplorerDropAsync(composer, root, bytes, () => sequence = GetClipboardSequenceNumber());
                Require(vm.State.Drafts.Single(d => d.ThreadId == 127).ImageFile == selected && handler.Requests == 0, "资源管理器拖入后草稿或请求数错误");
                handler.Fail = true;
                await PreviewAsync(composer, true, true);
                await Until(() => Text(composer).Contains("模拟失败"), "资源管理器附图未经过发送");
                Require(handler.LastImage.SequenceEqual(bytes), "资源管理器附图发送内容与原件不同");
                Pass("Windows 资源管理器复制粘贴、跨进程拖入正文、缩略图、草稿和模拟发送");
                File.WriteAllLines(Path.Combine(output, "results.txt"), log);
                return;
            }
            await CheckFilesAsync(store, root, bytes); Pass("本地 JPG/PNG/GIF、大小/目录/坏图、剪贴板透明像素、长边缩略图和文件解锁");
            var editor = All<EmojiEditor>(composer).Single(); editor.Text = "正文"; editor.CaretIndex = editor.Text.Length; editor.Focus();
            var zone = All<ImageDropSurface>(composer).Single(); var card = All<ComposerImageCard>(composer).Single();
            var fileData = Files(path); var textData = new DataObject(DataFormats.UnicodeText, "文本");
            var invalid = Drag(editor, Files(root), DragDrop.PreviewDragEnterEvent);
            Require(invalid.Effects == DragDropEffects.None && invalid.Handled && Text(zone).Contains("文件夹"), "文件夹应禁止");
            var plain = Drag(editor, textData, DragDrop.PreviewDragOverEvent); Require(!plain.Handled && !zone.IsVisible, "普通拖选文字被拦截");
            var bad = Path.Combine(root, "note.txt"); File.WriteAllText(bad, "test");
            var bogus = Drag(editor, Files(bad), DragDrop.PreviewDropEvent); Require(bogus.Effects == DragDropEffects.None && !card.IsVisible, "非图片被接受");
            Require(Drag(editor, Files(path, bad), DragDrop.PreviewDragOverEvent).Effects == DragDropEffects.None, "多选未拒绝");
            var over = Drag(editor, fileData, DragDrop.PreviewDragOverEvent);
            Require(over.Handled && over.Effects == DragDropEffects.Copy && zone.IsVisible && !zone.IsHitTestVisible && Text(zone).Contains("松开以添加图片"), "正文上方的覆盖层或 Copy 效果错误");
            capture(composer, Path.Combine(output, "drag-overlay.png"));
            Drag(editor, fileData, DragDrop.PreviewDragLeaveEvent); await Until(() => !zone.IsVisible, "拖出时覆盖层未关闭");
            Drag(editor, fileData, DragDrop.PreviewDropEvent); await Until(() => card.Photo.Source is not null, "拖入没有缩略图");
            Require(!zone.IsVisible && vm.State.Drafts.Single().ImageFile == path, "草稿路径与附图不同");
            Pass("发串窗口/正文拖入、取消、文本穿透、拒绝非图和多图");
            editor.Focus(); await Copy(Files(path)); ApplicationCommands.Paste.Execute(null, editor);
            Require(Text(composer).Contains("先移除") && vm.State.Drafts.Single().ImageFile == path, "重复添加没有提示");
            await ThemesAsync(composer, card, output, capture);
            var water = All<CheckBox>(composer).Single(c => Equals(c.Content, "为附图添加水印")); water.IsChecked = true;
            All<ComboBox>(composer).Single(c => AutomationProperties.GetName(c) == "发言使用的饼干").SelectedIndex = 1;
            Require(vm.State.Drafts.Single().ImageFile == path, "切饼干丢失附件");
            Require(handler.Requests == 0, "确认前不应预检或上传");
            await PreviewAsync(composer, confirm: false, image: true); Require(handler.Requests == 0, "取消预览上传了图片");
            handler.Fail = true; await PreviewAsync(composer, confirm: true, image: true);
            await Until(() => Text(composer).Contains("模拟失败"), "发送失败没有反馈");
            Require(handler.Posts == 1 && handler.LastImage.SequenceEqual(bytes) && handler.Water && handler.Target.EndsWith("doPostThread.html") && card.Photo.Source is not null, "发串、水印或失败保留错误");
            handler.Disconnect = true;
            await PreviewAsync(composer, confirm: true, image: true);
            await Until(() => Text(composer).Contains("未收到完整发送结果"), "断线没有提示核对发送结果");
            Require(handler.Posts == 2 && handler.LastImage.SequenceEqual(bytes) && editor.Text == "正文"
                && store.Load().Drafts.Single().ImageFile == path && card.Photo.Source is not null, "断线后重试或丢失附图草稿");
            handler.Disconnect = false;
            Pass("重复拒绝、换饼干、预览取消、模拟拒绝/断线保留草稿、水印字段和原件字节");
            card.Remove.Focus(); await Until(() => card.Remove.Opacity > .98, "键盘焦点没有显示移除按钮");
            Click(card.Remove); Require(!card.IsVisible && vm.State.Drafts.Single().ImageFile == "" && File.Exists(path), "移除影响外部原件或草稿");
            const string pasted = "链接 https://example.invalid/ 麵🍜";
            editor.Focus(); editor.Text = "首替换尾"; editor.Select(1, 2);
            var mix = new DataObject(); mix.SetImage(bitmap); mix.SetText(pasted); await Copy(mix);
            ApplicationCommands.Paste.Execute(null, editor);
            Require(editor.Text == "首" + pasted + "尾" && editor.SelectedText == "" && editor.CaretIndex == 1 + pasted.Length && !card.IsVisible,
                $"混合格式未准确替换已选文字（长度 {editor.Text.Length}、选区 {editor.SelectionStart}+{editor.SelectionLength}、附图 {card.IsVisible}）");
            var originalText = editor.Text; var screen = new DataObject(); screen.SetImage(bitmap); await Copy(screen);
            Require(ApplicationCommands.Paste.CanExecute(null, editor), "截图未启用粘贴命令"); ApplicationCommands.Paste.Execute(null, editor);
            await Until(() => card.Photo.Source is not null, "粘贴系统位图失败：" + Text(composer));
            var saved = store.Load().Drafts.Single(); var screenshot = store.ResolveDraftImage(saved.ImageFile);
            Require(LocalStore.IsClipboardImage(saved.ImageFile) && File.Exists(screenshot) && editor.Text == originalText, "截图未随草稿保存或污染正文");
            await ComposerInputChecks.Type(editor, "续"); Require(editor.Text == "首" + pasted + "续尾", "粘贴图片后未在原插入点继续输入");
            composer.Close(); composer = Open(0); card = All<ComposerImageCard>(composer).Single();
            await Until(() => card.Photo.Source is not null, "草稿重开未恢复图片"); Require(All<EmojiEditor>(composer).Single().Text == "首" + pasted + "续尾", "草稿正文未恢复");
            var expected = File.ReadAllBytes(screenshot); handler.Fail = false; await PreviewAsync(composer, confirm: true, image: true);
            await Until(() => composer.Delivered && !composer.IsVisible, "截图发送未完成"); Require(handler.LastImage.SequenceEqual(expected) && !File.Exists(screenshot), "成功后截图未回收或发送内容错误");
            Pass("剪贴板截图、混合文本/链接/Unicode、继续输入、草稿恢复、发送后回收");
            composer = Open(125); editor = All<EmojiEditor>(composer).Single(); editor.Text = "回应"; editor.Focus(); card = All<ComposerImageCard>(composer).Single();
            await Copy(Files(path)); Require(ApplicationCommands.Paste.CanExecute(null, editor), "资源管理器文件未启用粘贴命令"); ApplicationCommands.Paste.Execute(null, editor);
            await Until(() => card.Photo.Source is not null, "粘贴文件未生成缩略图");
            await PreviewAsync(composer, true, true); await Until(() => composer.Delivered, "回应发送失败");
            Require(handler.Target.EndsWith("doReplyThread.html") && !handler.Water && handler.LastImage.SequenceEqual(bytes), "回应表单错误"); Pass("回应正文粘贴文件、原图发送、水印关闭");
            composer = Open(126); editor = All<EmojiEditor>(composer).Single(); editor.Text = "待发送"; card = All<ComposerImageCard>(composer).Single();
            await InModal<ImageDropWindow>(composer, "添加图片", async picker =>
            {
                Require(picker.Owner == composer && Text(picker).Contains("将图片拖到这里"), "选图框归属/内容不正确");
                capture(picker, Path.Combine(output, "picker.png"));
                var child = All<ImageDropSurface>(picker).Single(); Drag(child, Files(path), DragDrop.PreviewDropEvent);
                await Until(() => !picker.IsVisible, "拖图到选图框后未返回");
            });
            await Until(() => card.Photo.Source is not null, "选图框拖入未生成缩略图"); var before = handler.Requests;
            File.WriteAllBytes(path, Encode(Fixture(20, 20))); await PreviewAsync(composer, true, true);
            await Until(() => Text(composer).Contains("原图已更改"), "预览之后换掉原图未拦截"); Require(handler.Requests == before, "原图改变仍发起网络请求");
            Click(card.Remove); File.WriteAllBytes(path, bytes); await PreviewAsync(composer, true, false); await Until(() => composer.Delivered, "无图回应发送失败"); Require(handler.LastImage.Length == 0, "移除后仍上传旧图");
            Pass("Dropzone 拖图、变更原文件拦截、移除后发送不带图片");
            if (real)
            {
                composer = Open(127); var testEditor = All<EmojiEditor>(composer).Single(); testEditor.Text = "实机附图";
                await RunRealAsync(composer, path, output, capture, Copy, bitmap);
                Pass("真实 OLE 文件拖放、鼠标悬停/移出/移除、Ctrl+V 位图和原生选择器确认/取消");
                handler.Fail = true; await PreviewAsync(composer, true, true);
                await Until(() => Text(composer).Contains("模拟失败"), "系统选图未经过发送"); Require(handler.LastImage.SequenceEqual(bytes), "原生入口没有发送原始文件");
            }
            File.WriteAllLines(Path.Combine(output, "results.txt"), log);
        }
        finally
        {
            if (composer.IsVisible) composer.Close(); App.ApplyTheme("light");
            await ClipboardAccess(() =>
            {
                if (sequence != 0 && sequence == GetClipboardSequenceNumber())
                {
                    if (original is null) Clipboard.Clear(); else Clipboard.SetDataObject(original, true);
                }
                return true;
            });
        }

        async Task PreviewAsync(ComposerWindow w, bool confirm, bool image)
        {
            await InModal<TextWindow>(w, "预览并发送", preview =>
            {
                Require(All<Image>(preview).Any(i => AutomationProperties.GetName(i) == "发送预览附图" && i.Source is BitmapSource) == image, "预览与编辑器的图片不同");
                if (confirm) Click(FindButton(preview, "确认发送")); else preview.Close(); return Task.CompletedTask;
            });
        }
    }

    private static async Task CheckFilesAsync(LocalStore store, string root, byte[] bytes)
    {
        var gif = Path.Combine(root, "a.gif"); File.WriteAllBytes(gif, GifChecks.Fixture(loop: true));
        var jpg = Path.Combine(root, "a.jpg"); File.WriteAllBytes(jpg, Encode(Fixture(), new JpegBitmapEncoder()));
        foreach (var path in new[] { gif, jpg })
        {
            var image = await new ComposerImageInput(FileName: path).PrepareAsync(store, default);
            Require(image.Thumbnail is { IsFrozen: true } && image.Hash == PostImageFile.Hash(File.ReadAllBytes(path)), "缩略图没有冻结或不是原文件");
            using var unlocked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        var data = new DataObject(); data.SetData("PNG", new MemoryStream(bytes), false); data.SetImage(Fixture());
        var saved = await ComposerImageInput.FromClipboard(data)!.PrepareAsync(store, default);
        Require(File.ReadAllBytes(store.ResolveDraftImage(saved.Reference)).SequenceEqual(bytes) && saved.Thumbnail!.PixelWidth == 256, "原始 PNG 或长边限幅错误"); store.DiscardClipboardImage(saved.Reference);
        var matchingFile = Files(jpg); matchingFile.SetText(jpg);
        Require(ComposerImageInput.FromClipboard(matchingFile)?.FileName == jpg, "剪贴板中的匹配文件路径未识别");
        var note = Path.Combine(root, "not-an-image.txt"); File.WriteAllText(note, "测试");
        var plainPath = Files(note); plainPath.SetText(note);
        Require(ComposerImageInput.FromClipboard(plainPath) is null, "非图片文件的路径文字被吞掉");
        var textWithFile = new DataObject(); textWithFile.SetFileDropList(Files(Path.Combine(root, "picture.png")).GetFileDropList()); textWithFile.SetText("保留说明与链接 https://example.invalid/ 麵🍜");
        Require(ComposerImageInput.FromClipboard(textWithFile) is null, "剪贴板里的普通文字被错误消费");
        var textWithBitmap = new DataObject(); textWithBitmap.SetImage(Fixture()); textWithBitmap.SetText("普通正文");
        Require(ComposerImageInput.FromClipboard(textWithBitmap) is null, "含图片格式的普通文字未保留");
        var tall = Fixture(13, 520); var result = await new ComposerImageInput(Bitmap: tall).PrepareAsync(store, default);
        var decoded = ImageService.Decode(File.ReadAllBytes(store.ResolveDraftImage(result.Reference)));
        Require(result.Thumbnail!.PixelHeight == 256 && decoded.PixelHeight == 520 && decoded.PixelWidth == 13, "长图被拉伸或提交分辨率改变");
        var rgba = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0); var pixels = new byte[13 * 520 * 4]; rgba.CopyPixels(pixels, 13 * 4, 0);
        Require(pixels[3] == 128, "剪贴板位图丢失透明度"); store.DiscardClipboardImage(result.Reference);
        // 大而可压缩的相机图片允许按目标尺寸解码，原件仍按字节数校验。
        var large = BitmapSource.Create(5000, 4000, 96, 96, PixelFormats.Gray8, null, new byte[5000 * 4000], 5000);
        var photo = Path.Combine(root, "camera.png"); File.WriteAllBytes(photo, Encode(large));
        var preview = await new ComposerImageInput(FileName: photo).PrepareAsync(store, default);
        Require(preview.Thumbnail is { PixelWidth: 256, PixelHeight: 205 }, "2000 万像素 PNG 没有按长边缩放");
        var strip = await new ComposerImageInput(Bitmap: Fixture(1, 4096)).PrepareAsync(store, default);
        Require(strip.Thumbnail is { PixelWidth: 1, PixelHeight: 256 }, "极细长图的短边被缩到零"); store.DiscardClipboardImage(strip.Reference);
        var largeGif = GifChecks.Fixture(loop: false); largeGif[6] = largeGif[7] = largeGif[8] = largeGif[9] = 0xff;
        foreach (var (name, content) in new[] { ("large-canvas.gif", largeGif), ("bad.png", bytes[..12]), ("large.png", new byte[PostImageFile.MaxBytes + 1]), ("fake.jpg", bytes) })
        {
            var path = Path.Combine(root, name); File.WriteAllBytes(path, content);
            try { await new ComposerImageInput(FileName: path).PrepareAsync(store, default); }
            catch (Exception ex) when (ex is InvalidDataException or FormatException or NotSupportedException or IOException) { continue; }
            throw new InvalidOperationException("接受了 " + name);
        }
    }

    private static async Task ThemesAsync(ComposerWindow w, ComposerImageCard card, string output, Action<Window, string> capture)
    {
        var w0 = w.Width; var h0 = w.Height;
        foreach (var theme in new[] { "light", "dark" })
            foreach (var scale in new[] { 1d, 1.25, 1.5 })
            {
                App.ApplyTheme(theme); UiCopyChecks.SetDpi(w, scale, 480, 690); await Task.Delay(80); card.BringIntoView(); await Task.Delay(60);
                Require(card.Tile.ActualWidth == 104 && card.Tile.ActualHeight == 76 && card.Photo.Stretch == Stretch.Uniform && card.Remove.ActualWidth >= 32, "DPI 布局变形");
                if (scale == 1.25) capture(w, Path.Combine(output, $"card-{theme}.png"));
            }
        App.ApplyTheme("light"); UiCopyChecks.SetDpi(w, 1, w0, h0); await Task.Delay(80);
    }

    private static async Task InModal<T>(Window owner, string button, Func<T, Task> inspect) where T : Window
    {
        var check = owner.Dispatcher.InvokeAsync(async () =>
        {
            var dialog = owner.OwnedWindows.OfType<T>().Single();
            try { await inspect(dialog); } finally { if (dialog.IsVisible) dialog.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        Click(FindButton(owner, button)); await await check.Task;
    }
    private static DragEventArgs Drag(UIElement source, DataObject data, RoutedEvent type)
    {
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs), BindingFlags.Instance | BindingFlags.NonPublic, null,
            [data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Copy, source, new Point(20, 20)], null)!;
        args.RoutedEvent = type; source.RaiseEvent(args); return args;
    }
    private static DataObject Files(params string[] paths) { var list = new StringCollection(); list.AddRange(paths); var data = new DataObject(); data.SetFileDropList(list); return data; }
    private static BitmapSource Fixture(int width = 360, int height = 180)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4; pixels[i] = (byte)(96 + x * 100 / width); pixels[i + 1] = (byte)(100 + y * 100 / height);
            pixels[i + 2] = (byte)(60 + (x / 36 % 2) * 110); pixels[i + 3] = (byte)(x < width / 4 ? 128 : 255);
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); result.Freeze(); return result;
    }
    private static byte[] Encode(BitmapSource src, BitmapEncoder? encoder = null)
    { encoder ??= new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(src)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray(); }
    private static IEnumerable<T> All<T>(DependencyObject d) where T : DependencyObject => UiRefinementChecks.Descendants<T>(d);
    private static string Text(DependencyObject d) => string.Join(" ", All<TextBlock>(d).Where(t => t.IsVisible).Select(t => t.Text));
    private static Button FindButton(Window w, string text) => All<Button>(w).Single(b => Equals(b.Content, text) || AutomationProperties.GetName(b) == text);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Require(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
    private static async Task Until(Func<bool> check, string error) { for (int i = 0; i < 120 && !check(); i++) await Task.Delay(35); Require(check(), error); }
    private static async Task<T> ClipboardAccess<T>(Func<T> run)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return run(); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x800401D0) && attempt < 4) { await Task.Delay(80); }
        }
    }
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    private sealed class Transport : HttpMessageHandler
    {
        internal int Requests, Posts; internal bool Fail, Water, Disconnect; internal byte[] LastImage = []; internal string Target = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            if (request.Method == HttpMethod.Get) return Reply("<html></html>");
            Posts++; Target = request.RequestUri!.AbsolutePath; var parts = (MultipartFormDataContent)request.Content!;
            var image = parts.SingleOrDefault(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "image"); LastImage = image is null ? [] : await image.ReadAsByteArrayAsync(ct);
            var water = parts.SingleOrDefault(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "water"); Water = water is not null && await water.ReadAsStringAsync(ct) == "true";
            if (Disconnect) throw new HttpRequestException("测试断线");
            return Reply(Fail ? "<p class='error'>模拟失败</p>" : "<p class='success'>成功</p><a href='/t/125'>串</a>");
        }
        private static HttpResponseMessage Reply(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    }
}

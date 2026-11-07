using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;
using ZXing;
using ZXing.QrCode;

namespace Tide.Desktop.Tests;

internal static class CookieImportChecks
{
    private const string SyntheticCookie = "synthetic%2Bcookie%25fixture";
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var store = new LocalStore(Path.Combine(output, "cookie-import-profile"));
        var handler = new AuthPages();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var main = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        CookieWindow? dialog = null;
        try
        {
            main.Show(); await vm.OpenThreadAsync(51288554, 99); await Task.Delay(150);
            var before = vm.ReadingPosts[0].Post.Id;
            await vm.OpenThreadAsync(51288554, 200);
            Require(vm.HasError && vm.Error.Contains("第 100 页") && vm.ReadingPosts[0].Post.Id == before, "截断页没有明确提示或替换了原正文。");
            Require(store.Load().History[0].Page == 99 && store.ReadPage(51288554, 200, "") is null, "错误高页污染进度或离线缓存。");

            dialog = new CookieWindow(vm) { Owner = main, ShowInTaskbar = false }; dialog.Show(); await Task.Delay(100);
            await Invoke(FindButton(dialog, "导入饼干"));
            Require(NameBlock(dialog).Text == "—" && !UiRefinementChecks.Descendants<TextBox>(dialog).Any(t => AutomationProperties.GetName(t) == "饼干名称"), "名称仍可编辑或空内容显示了旧名称。");
            var qr = Qr(JsonSerializer.Serialize(new { cookie = SyntheticCookie, name = "测试饼干" }));
            var path = Path.Combine(output, "synthetic-cookie-qr.png");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(qr)); using (var file = File.Create(path)) encoder.Save(file);
            await dialog.ReadQrAsync(() => CookieImport.ReadFileAsync(path));
            Require(CookieImport.Parse(ValueBox(dialog).Password).Cookie == SyntheticCookie && NameBlock(dialog).Text == "测试饼干", "二维码图片没有回填完整饼干和源名称。");
            capture(dialog, Path.Combine(output, "cookie-name-preview.png"));
            await dialog.ReadQrAsync(() => CookieImport.ReadImageAsync(Qr(JsonSerializer.Serialize(new { cookie = "another-cookie-fixture", name = "另一个饼干" }))));
            Require(NameBlock(dialog).Text == "另一个饼干", "更换二维码后沿用了旧名称。");
            ValueBox(dialog).Password = SyntheticCookie;
            Require(NameBlock(dialog).Text == "饼干 1", "改为无名称文本后仍沿用二维码名称。");
            ValueBox(dialog).Password = JsonSerializer.Serialize(new { userhash = SyntheticCookie, name = "文本饼干" });
            Require(NameBlock(dialog).Text == "文本饼干", "手动粘贴 JSON 后没有显示源名称。");
            await dialog.ReadQrAsync(() => CookieImport.ReadFileAsync(path));
            var import = FindButton(dialog, "导入并使用"); Require(import.IsVisible, "保存入口不可见。");
            await Invoke(import); await Task.Delay(100);
            Require(vm.State.Cookies.Count == 1 && vm.ActiveCookie() == SyntheticCookie && vm.State.ActiveCookieId.Length > 0 && vm.State.Cookies[0].Name == "测试饼干", "二维码未保存原名或没有立即使用。");
            Require(!File.ReadAllText(Path.Combine(store.Root, "state.json")).Contains(SyntheticCookie), "饼干以明文写入状态文件。");
            var remark = UiRefinementChecks.Descendants<TextBox>(dialog).Single(t => AutomationProperties.GetName(t) == "饼干备注");
            remark.Text = "我的常用饼干"; await Invoke(FindButton(dialog, "保存备注"));
            Require(store.Load().Cookies.Single().Remark == "我的常用饼干" && vm.State.Cookies[0].Name == "测试饼干" && vm.ActiveCookie() == SyntheticCookie, "备注未保存或修改了饼干原名/认证内容。");
            capture(dialog, Path.Combine(output, "cookie-import-light.png"));
            App.ApplyTheme("dark"); await Task.Delay(100); capture(dialog, Path.Combine(output, "cookie-import-dark.png")); App.ApplyTheme("light");
            dialog.Width = dialog.MinWidth; await Task.Delay(100); capture(dialog, Path.Combine(output, "cookie-import-narrow.png"));

            // 剪贴板图片走相同的冻结副本解码入口；不读取或覆盖用户的真实剪贴板。
            await Invoke(FindButton(dialog, "导入饼干"));
            var rotated = new TransformedBitmap(qr, new RotateTransform(90));
            await dialog.ReadQrAsync(() => CookieImport.ReadImageAsync(rotated)); await Invoke(import);
            Require(vm.State.Cookies.Count == 1, "重复导入同一饼干产生了重复身份。");
            var originalId = vm.State.ActiveCookieId;
            await Invoke(FindButton(dialog, "导入饼干"));
            ValueBox(dialog).Password = JsonSerializer.Serialize(new { cookie = SyntheticCookie, name = "源名称更新" }); await Invoke(import);
            Require(vm.State.Cookies.Count == 1 && vm.State.ActiveCookieId == originalId && vm.State.Cookies[0].Name == "源名称更新", "重复导入没有保留身份并使用源名称。");
            Require(vm.State.Cookies[0].Remark == "我的常用饼干", "重复导入覆盖了本地备注。");
            await Invoke(FindButton(dialog, "导入饼干"));
            ValueBox(dialog).Password = SyntheticCookie;
            Require(NameBlock(dialog).Text == "源名称更新", "重复导入无名称文本时预览丢失已保存名称。");
            await Invoke(import);
            Require(vm.State.Cookies[0].Name == "源名称更新", "无名称文本覆盖了已有名称。");
            await Invoke(FindButton(dialog, "导入饼干"));
            var white = BitmapSource.Create(200, 200, 96, 96, PixelFormats.Gray8, null, Enumerable.Repeat((byte)255, 40000).ToArray(), 200);
            await dialog.ReadQrAsync(() => CookieImport.ReadImageAsync(white));
            Require(Result(dialog).Contains("没有识别到二维码") && vm.State.Cookies.Count == 1, "空图片没有明确失败或改变身份。");
            ValueBox(dialog).Password = "{bad json";
            Require(!import.IsEnabled && NameBlock(dialog).Text.Contains("JSON") && vm.State.Cookies.Count == 1, "坏 JSON 未禁用导入或显示了旧名称。");
            ValueBox(dialog).Password = "1234567";
            Require(!import.IsEnabled && NameBlock(dialog).Text.Contains("7 位") && vm.State.Cookies.Count == 1, "公开短 ID 被当成完整饼干。");
            await dialog.ReadQrAsync(() => CookieImport.ReadImageAsync(Qr("https://example.invalid/qr")));
            Require(Result(dialog).Contains("网页链接"), "普通网页二维码被当成饼干。");
            dialog.Close(); dialog = null;

            await vm.OpenThreadAsync(51288554, 200); await Task.Delay(150);
            Require(!vm.HasError && vm.ReadingPosts[0].Post.Id == 200001 && handler.AuthenticatedPages.Contains(200), "导入后请求没有携带正确认证或高页被客户端阻止。");
            await vm.LoadPreviousThreadAsync(); await vm.LoadMoreThreadAsync();
            Require(vm.ReadingPosts[0].Post.Id == 199001 && vm.ReadingPosts.Last().Post.Id == 201019, "高页上下续载失败。");
            capture(main, Path.Combine(output, "large-thread-auth-fixture.png"));
            using (var reopened = new MainViewModel(new XIslandApi(new HttpClient(new AuthPages())), store))
                Require(reopened.ActiveCookie() == SyntheticCookie && reopened.State.Cookies[0].Name == "源名称更新", "重启后饼干身份或源名称没有保留。");

            dialog = new CookieWindow(vm) { Owner = main, ShowInTaskbar = false }; dialog.Show();
            UiRefinementChecks.Descendants<ComboBox>(dialog).Single(c => AutomationProperties.GetName(c) == "默认发言身份").SelectedValue = ""; await Task.Delay(150);
            Require(vm.State.ActiveCookieId == "" && store.Load().ActiveCookieId == "", "游客切换未立即生效。");
            await Invoke(FindButton(dialog, "导入饼干"));
            var gate = new TaskCompletionSource<CookieImport.Value>(); var pending = dialog.ReadQrAsync(() => gate.Task);
            dialog.Close(); dialog = null; gate.SetResult(new(SyntheticCookie, "迟到识别")); await pending;
            Require(vm.State.ActiveCookieId == "", "关闭窗口后迟到识别改变了身份。");
            File.WriteAllText(Path.Combine(output, "cookie-import-results.json"), JsonSerializer.Serialize(new {
                QrFile = true, RotatedImage = true, DuplicateReused = true, ImmediateAuth = true, RestartAuth = true,
                NameReadOnly = true, NameFollowsSource = true, NameChangesWithInput = true, UnnamedReimportKeepsName = true,
                EncryptionAtRest = true, InvalidInputsRejected = true, ClosedDuringDecode = true, HighPageRepetitionRejected = true,
                HighPagesBidirectional = true, RealCredentialUsed = false }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: read-only source names, QR/text replacement, duplicate names, QR file/image, immediate auth, encrypted persistence, invalid input, close cancellation, guest clamp vs authenticated high-page fixture.");
        }
        finally { dialog?.Close(); main.Close(); App.ApplyTheme("light"); }
    }
    internal static BitmapSource Qr(string text)
    {
        var writer = new BarcodeWriterPixelData { Format = BarcodeFormat.QR_CODE, Options = new QrCodeEncodingOptions { Width = 320, Height = 320, Margin = 4, CharacterSet = "UTF-8" } };
        var pixels = writer.Write(text);
        return BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Pixels, pixels.Width * 4);
    }
    private static PasswordBox ValueBox(CookieWindow window) => UiRefinementChecks.Descendants<PasswordBox>(window).Single();
    private static TextBlock NameBlock(CookieWindow window) => UiRefinementChecks.Descendants<TextBlock>(window).Single(t => AutomationProperties.GetName(t) == "饼干名称");
    private static string Result(CookieWindow window) => UiRefinementChecks.Descendants<TextBlock>(window).Single(t => AutomationProperties.GetName(t) == "饼干操作结果").Text;
    private static Button FindButton(Window window, string name) => UiRefinementChecks.Descendants<Button>(window).Single(b => b.Content?.ToString() == name);
    private static async Task Invoke(Button button)
    {
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke(); await Task.Delay(40);
    }
    private static void Require(bool test, string message) { if (!test) throw new InvalidOperationException(message); }
    private sealed class AuthPages : HttpMessageHandler
    {
        public List<int> AuthenticatedPages { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = request.RequestUri!.Query.TrimStart('?').Split('&').Select(s => s.Split('=')).ToDictionary(p => p[0], p => p[1]);
            var requested = int.Parse(fields["page"]);
            var authenticated = request.Headers.TryGetValues("Cookie", out var cookies) && cookies.Single() == "userhash=" + SyntheticCookie;
            if (authenticated) AuthenticatedPages.Add(requested);
            var page = authenticated ? requested : Math.Min(100, requested);
            var value = new { id = 51288554, ReplyCount = 8995, title = "高页与身份模拟", content = "主楼", user_hash = "Author",
                Replies = Enumerable.Range(1, 19).Select(i => new { id = page * 1000 + i, user_hash = "Reader", content = $"第 {page} 页第 {i} 条<br>模拟权限检查。<br>仅使用合成饼干。" }) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
        }
    }
}

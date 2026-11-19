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
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class SiteNoticeChecks
{
    public static async Task RunAsync(MainWindow host, string output, Action<Window, string> capture)
    {
        var store = new LocalStore(Path.Combine(output, "notice-profile"));
        store.Save(new UserState());
        var vm = new MainViewModel(UiRefinementChecks.CreateApi(), store);
        var owner = new MainWindow(vm) { Owner = host, ShowInTaskbar = false };
        var original = new SiteNotice("2026102100008", "这是一条界面测试通知。\n维护通知与读串请求分别加载。\n关闭后仍可继续浏览。");
        try
        {
            owner.Show(); owner.Activate();
            // 标题栏关闭，即使已经勾选也不视为确认。
            await Show(original, "light", true, false, "notice-light.png");
            Require(store.Load().SuppressedNoticeKeys.Count == 0, "关闭窗口错误地保存了免打扰。");
            await Show(original, "light", false, true, null);
            Require(store.Load().SuppressedNoticeKeys.Count == 0, "未勾选确认后不应停止后续启动提示。");
            await Show(original, "dark", true, true, "notice-dark.png");
            Require(store.Load().SuppressedNoticeKeys.SequenceEqual(new[] { original.Key }), "确认免打扰后没有持久保存。");

            // 重新读取保存的状态，模拟再次启动；同一公告应被忽略。
            vm.State.SuppressedNoticeKeys = store.Load().SuppressedNoticeKeys;
            using (var client = Client(original))
            using (var presenter = new StartupNoticePresenter(owner, vm, client))
            {
                await presenter.RunAsync();
                Require(!Notices(owner).Any(), "已忽略的公告仍然弹出。");
            }
            var longNotice = original with { Content = string.Join("\n", Enumerable.Range(1, 45).Select(i => $"第 {i} 行测试内容：长公告可以滚动，确认按钮应始终可见。")) };
            await Show(longNotice, "light", false, true, "notice-long-narrow.png", narrow: true);
            Require(!vm.State.SuppressedNoticeKeys.Contains(longNotice.Key), "更新公告被旧公告的免打扰误伤。");
            var newVersion = original with { Version = "2026102200001" };
            await Show(newVersion, "light", false, true, null);

            foreach (var response in new[] { "{\"enable\":false}", "{\"enable\":true,\"content\":\"\"}", "<html>error</html>" })
            {
                using var client = new SiteNoticeClient(new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) }))));
                using var presenter = new StartupNoticePresenter(owner, vm, client);
                var previousError = vm.Error;
                await presenter.RunAsync();
                Require(!Notices(owner).Any() && vm.Error == previousError, "空公告或错误响应影响了主界面。");
            }
            using (var client = new SiteNoticeClient(new HttpClient(new Handler((_, _) => throw new HttpRequestException("offline")))))
            using (var presenter = new StartupNoticePresenter(owner, vm, client))
            {
                await presenter.RunAsync();
                Require(presenter.Failure is not null && !Notices(owner).Any(), "断网时没有安全结束公告检查。");
            }

            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = false;
            using (var client = new SiteNoticeClient(new HttpClient(new Handler(async (_, ct) =>
            {
                started.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return new(HttpStatusCode.OK);
            }))))
            using (var presenter = new StartupNoticePresenter(owner, vm, client))
            {
                var pending = presenter.RunAsync(); await started.Task;
                owner.Close(); await pending;
                Require(cancelled && !Notices(owner).Any(), "退出主窗口后公告请求未取消。");
            }
            File.WriteAllText(Path.Combine(output, "site-notice-results.json"), JsonSerializer.Serialize(new
            {
                StartupPopup = true, RepeatWithoutSuppression = true, ConfirmOnlySuppression = true,
                SuppressionReload = true, ChangedContentOrVersionShows = true, OncePerStartup = true,
                Themes = new[] { "light", "dark" }, LongNoticeScrollAndFooter = true,
                ErrorsDoNotInterrupt = true, OwnerCloseCancels = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: startup notice popup, confirm-only suppression/reload, changed notice, light/dark, long scrolling, failures and shutdown cancellation.");
        }
        finally { if (owner.IsVisible) owner.Close(); App.ApplyTheme("light"); }

        async Task Show(SiteNotice notice, string theme, bool suppress, bool confirm, string? screenshot, bool narrow = false)
        {
            App.ApplyTheme(theme); owner.Activate();
            var calls = 0;
            using var client = Client(notice, () => calls++);
            using var presenter = new StartupNoticePresenter(owner, vm, client);
            var interaction = Interact();
            await presenter.RunAsync(); await interaction;
            await presenter.RunAsync();
            Require(calls == 1, "同次启动重复检查或弹出公告。");
            Require(owner.IsVisible && !Notices(owner).Any(), "公告关闭后主窗口状态异常。");

            async Task Interact()
            {
                SiteNoticeWindow? dialog = null;
                try
                {
                    for (var i = 0; i < 100 && dialog is null; i++) { await Task.Delay(50); dialog = Notices(owner).FirstOrDefault(); }
                    Require(dialog is not null, "没有显示启动通知。");
                    if (narrow) { dialog!.Width = 420; dialog.Height = 360; }
                    await Layout(dialog!);
                    var body = UiRefinementChecks.Descendants<RichTextBox>(dialog!).Single();
                    var text = new System.Windows.Documents.TextRange(body.Document.ContentStart, body.Document.ContentEnd).Text.TrimEnd();
                    Require(text.Replace("\r\n", "\n") == notice.Content, "公告正文或换行没有完整显示。");
                    var checkbox = UiRefinementChecks.Descendants<CheckBox>(dialog!).Single();
                    var button = UiRefinementChecks.Descendants<Button>(dialog!).Single(b => AutomationProperties.GetName(b) == "确定通知");
                    foreach (var control in new FrameworkElement[] { checkbox, button })
                    {
                        var bounds = control.TransformToAncestor(dialog!).TransformBounds(new Rect(control.RenderSize));
                        Require(bounds.Left >= 0 && bounds.Right <= dialog!.ActualWidth && bounds.Bottom <= dialog.ActualHeight, "公告底部操作超出窗口。");
                    }
                    if (narrow)
                    {
                        var scroll = UiRefinementChecks.Descendants<ScrollViewer>(dialog!).First(s => s.ScrollableHeight > 0);
                        scroll.ScrollToEnd(); await Layout(dialog!);
                        Require(scroll.VerticalOffset > 0, "长公告不能滚动。");
                    }
                    checkbox.IsChecked = suppress;
                    if (screenshot is not null) capture(dialog!, Path.Combine(output, screenshot));
                    if (confirm) ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
                    else dialog!.Close();
                }
                catch { if (dialog?.IsVisible == true) dialog.Close(); else owner.Close(); throw; }
            }
        }
    }

    private static SiteNoticeClient Client(SiteNotice notice, Action? called = null) => new(new HttpClient(new Handler((_, _) =>
    {
        called?.Invoke();
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { enable = true, date = notice.Version, content = WebUtility.HtmlEncode(notice.Content).Replace("\n", "<br>") })) });
    })));
    private static IEnumerable<SiteNoticeWindow> Notices(Window owner) => Application.Current.Windows.OfType<SiteNoticeWindow>().Where(w => w.Owner == owner && w.IsVisible);
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(100); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request, cancellationToken);
    }
}

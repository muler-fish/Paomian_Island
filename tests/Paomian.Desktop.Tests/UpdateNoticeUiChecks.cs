using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class UpdateNoticeUiChecks
{
    private enum CloseBy { Action, Caption, Escape, Owner, LegacyProfile }

    internal static async Task RunAsync(string output, Action<Window, string> capture)
    {
        var root = Path.Combine(output, Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var method in new[] { CloseBy.Action, CloseBy.Caption, CloseBy.Escape })
            {
                var profile = Path.Combine(root, method.ToString());
                Require(new LocalStore(profile).BeginUpdateNotice("1.0.7") is null, "全新资料意外生成通知");
                await LaunchAsync(profile, method, output, capture);
                await LaunchAsync(profile, null, output, capture);
            }
            var legacy = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "state.json"), "{}");
            await LaunchAsync(legacy, CloseBy.LegacyProfile, output, capture);
            await LaunchAsync(legacy, null, output, capture);
            var interrupted = Path.Combine(root, "interrupted");
            new LocalStore(interrupted).BeginUpdateNotice("1.0.7");
            await LaunchAsync(interrupted, CloseBy.Owner, output, capture);
            await LaunchAsync(interrupted, CloseBy.Escape, output, capture);
            await LaunchAsync(interrupted, null, output, capture);
            await LaunchAsync(Path.Combine(root, "new-profile"), null, output, capture);
            Console.WriteLine("PASS startup update dialog: three dismissals and restart, owner exit and recovery, pending network, keyboard focus, boss-key visibility, themes and fixed footer.");
        }
        finally { Personalization.Load(new UserState()); App.ApplyTheme("light"); }
    }

    private static async Task LaunchAsync(string profile, CloseBy? method, string output, Action<Window, string> capture)
    {
        var transport = new History();
        using var http = new HttpClient(transport);
        var vm = new MainViewModel(UiRefinementChecks.CreateApi(), new LocalStore(profile), new ReleaseUpdater(http), UpdateController.Version);
        var opened = new List<string>();
        var owner = new MainWindow(vm, opened.Add) { ShowInTaskbar = false };
        var presenter = new StartupUpdatePresenter(owner);
        owner.Show(); owner.Activate();
        try
        {
            await Until(() => owner.IsActive, "测试主窗口未激活");
            var jump = (TextBox)owner.FindName("JumpBox");
            jump.Focus();
            var run = presenter.RunAsync();
            await presenter.RunAsync();
            if (method is null)
            {
                await run.WaitAsync(TimeSpan.FromSeconds(5));
                Require(owner.OwnedWindows.Count == 0 && transport.Requests == 0, "无待确认版本仍显示窗口或请求 GitHub");
                return;
            }
            await Until(() => owner.OwnedWindows.OfType<UpdateNoticeWindow>().Any(w => w.IsVisible), "未出现更新内容");
            var dialog = owner.OwnedWindows.OfType<UpdateNoticeWindow>().Single();
            var ok = All<Button>(dialog).Single(b => Equals(b.Content, "知道了"));
            await Until(() => ok.IsKeyboardFocused, "焦点不在「知道了」");
            if (method != CloseBy.LegacyProfile)
                await Until(() => transport.Requests > 0, "历史日志请求未启动");
            Require(!IsWindowEnabled(Handle(owner)) && dialog.Owner == owner, "通知缺少模态 Owner");
            Require(All<TextBlock>(dialog).Any(b => b.Text == $"泡面岛 {UpdateController.Version}"), "缺少当前版本标题");
            if (method == CloseBy.Action)
            {
                transport.Release.TrySetResult();
                await Until(() => All<TextBlock>(dialog).Any(b => b.Text == "v1.0.12"), "跨版本说明未补齐");
                var versions = All<TextBlock>(dialog).Select(b => b.Text).Where(t => t.StartsWith('v')).ToArray();
                Require(versions.SequenceEqual(new[] { "v" + UpdateController.Version, "v1.0.12" }), "显示范围或版本顺序错误");
                await CheckLayoutAsync(dialog, ok, vm.State, output, capture);
                var history = All<TextBlock>(dialog).SelectMany(b => b.Inlines.OfType<Hyperlink>())
                    .Single(l => AutomationProperties.GetName(l) == "查看完整更新日志");
                Require(history.Focus(), "页脚链接无法取得键盘焦点");
                history.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
                Require(opened.SequenceEqual([ReleaseUpdater.Repository + "/releases"]) && dialog.IsVisible, "日志链接地址或打开方式错误");
                var windows = new BossKeyWindows(owner);
                windows.Hide();
                Require(!IsWindowVisible(Handle(dialog)) && !IsWindowVisible(Handle(owner)), "老板键未隐藏主窗口和更新通知");
                windows.Restore();
                Require(IsWindowVisible(Handle(dialog)) && !IsWindowEnabled(Handle(owner)), "老板键恢复时丢失模态或更新通知");
            }

            if (method == CloseBy.LegacyProfile)
            {
                var displayed = All<TextBlock>(dialog).Select(b => b.Text).ToList();
                Require(displayed.Contains("旧资料未记录升级前的版本，仅展示可确认的更新说明。") &&
                    displayed.Where(t => t.StartsWith('v')).SequenceEqual(["v" + UpdateController.Version]) &&
                    transport.Requests == 0, "旧资料的离线说明或范围错误");
                foreach (var theme in new[] { "light", "dark" })
                {
                    Personalization.Load(vm.State); App.ApplyTheme(theme);
                    await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout, DispatcherPriority.ApplicationIdle);
                    capture(dialog, Path.Combine(output, "legacy-" + theme + ".png"));
                }
            }
            switch (method)
            {
                case CloseBy.LegacyProfile:
                case CloseBy.Action: Click(ok); break;
                case CloseBy.Caption: Click(All<Button>(dialog).Single(b => AutomationProperties.GetName(b) == "关闭")); break;
                case CloseBy.Escape:
                    var key = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, 0, Key.Escape)
                    { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    dialog.RaiseEvent(key);
                    Require(key.Handled, "共用外壳没有处理 Esc"); break;
                case CloseBy.Owner: owner.Close(); break;
            }
            await run.WaitAsync(TimeSpan.FromSeconds(5));
            if (method != CloseBy.Owner)
                await Until(() => IsWindowEnabled(Handle(owner)) && jump.IsKeyboardFocusWithin, "关闭后主窗口焦点未恢复");
            if (method is CloseBy.Caption or CloseBy.Escape or CloseBy.Owner)
            {
                await Until(() => transport.Cancelled, "通知关闭时未取消历史请求");
                transport.Release.TrySetResult();
                await transport.Finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            Require(!dialog.IsVisible && Application.Current.Windows.OfType<UpdateNoticeWindow>().All(w => !w.IsVisible),
                "已关闭的通知再次出现");
        }
        finally { transport.Release.TrySetResult(); if (owner.IsVisible) owner.Close(); }
    }

    private static async Task CheckLayoutAsync(UpdateNoticeWindow dialog, Button ok, UserState state, string output, Action<Window, string> capture)
    {
        var scroll = All<ScrollViewer>(dialog).Single(s => s.ScrollableHeight > 0);
        dialog.UpdateLayout();
        var footer = All<TextBlock>(dialog).Single(b => b.Inlines.OfType<Hyperlink>()
            .Any(l => AutomationProperties.GetName(l) == "查看完整更新日志"));
        var buttonPosition = ok.TranslatePoint(new(), dialog);
        var linkPosition = footer.TranslatePoint(new(), dialog);
        Require(linkPosition.X < buttonPosition.X && linkPosition.Y >= buttonPosition.Y - 2 &&
            scroll.TranslatePoint(new Point(0, scroll.ActualHeight), dialog).Y <= buttonPosition.Y, "固定页脚位置错误");
        scroll.ScrollToEnd(); dialog.UpdateLayout();
        Require(scroll.VerticalOffset > 0 && ok.TranslatePoint(new(), dialog) == buttonPosition &&
            footer.TranslatePoint(new(), dialog) == linkPosition, "长日志滚动时页脚移动");
        scroll.ScrollToTop();
        foreach (var (theme, accent, file) in new[] { ("light", "", "light"), ("dark", "", "dark"), ("light", "#5B57A2", "custom-accent") })
        {
            state.AccentColor = accent; Personalization.Load(state); App.ApplyTheme(theme);
            await dialog.Dispatcher.InvokeAsync(dialog.UpdateLayout, DispatcherPriority.ApplicationIdle);
            capture(dialog, Path.Combine(output, file + ".png"));
            var buttonColor = ((SolidColorBrush)ok.Background).Color;
            var accentKey = ok.IsMouseOver ? "AccentHover" : "Accent";
            var expected = ((SolidColorBrush)dialog.FindResource(accentKey)).Color;
            var surface = ((SolidColorBrush)((Border)dialog.Content).Background).Color;
            var expectedSurface = ((SolidColorBrush)dialog.FindResource("Surface")).Color;
            Require(buttonColor == expected && surface == expectedSurface,
                $"{file} 主按钮 {buttonColor}/{expected} 或窗口底色 {surface}/{expectedSurface} 未跟随主题");
        }
    }

    private sealed class History : HttpMessageHandler
    {
        internal int Requests;
        internal bool Cancelled;
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.RequestUri?.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal) == true, "启动日志请求了意外的 URL");
            Requests++;
            using var registration = ct.Register(() => Cancelled = true);
            // 模拟关闭后才返回的网络结果，检验过时回调不会再打开原窗口。
            await Release.Task.ConfigureAwait(false);
            var longBody = "## 更新内容\n\n### 示例更新说明\n\n" +
                string.Join('\n', Enumerable.Range(1, 24).Select(i => $"- 离线验收条目 {i}，用于检查长日志的滚动范围。"));
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[]
            {
                new { tag_name = "v1.0.12", draft = false, prerelease = false, body = longBody },
                new { tag_name = "v99.0.0", draft = false, prerelease = false, body = "未来版本" }
            }) };
            Finished.TrySetResult();
            return response;
        }
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static async Task Until(Func<bool> ready, string error)
    {
        for (var i = 0; i < 180 && !ready(); i++) await Task.Delay(25);
        Require(ready(), error);
    }
    private static void Require(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
    private static nint Handle(Window window) => new WindowInteropHelper(window).Handle;
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
}

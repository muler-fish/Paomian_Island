using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class SubscriptionCacheChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var failures = new List<string>();
        void Check(bool value, string label) { Console.WriteLine($"{(value ? "PASS" : "FAIL")}: {label}"); if (!value) failures.Add(label); }
        var handler = new DelayedFeed();
        var store = new LocalStore(Path.Combine(output, "cache-profile"));
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        vm.SetSubscriptionId("测试订阅号");
        var loading = vm.ShowSubscriptionsAsync();
        await handler.SecondPageStarted.Task;
        Check(vm.Posts.Count == 10, "first page is visible while second page is blocked");
        vm.SubscriptionsCommand.Execute(null);
        Check(vm.SubscriptionsCommand.CanExecute(null), "navigation stays clickable during sync");
        handler.Release.TrySetResult(true); await loading;
        using var reopened = new MainViewModel(new XIslandApi(new HttpClient(new DelayedFeed())), store);
        Check(reopened.SubscriptionCount == "20", "reopen restores subscription count before network");
        var dialog = new SubscriptionWindow(vm) { Owner = owner, ShowInTaskbar = false };
        dialog.Show(); await Task.Delay(100);
        Check(UiRefinementChecks.Descendants<TextBox>(dialog).Any(t => t.Text == "测试订阅号"), "saved Unicode subscription ID is readable and editable");
        capture(dialog, Path.Combine(output, "subscription-editor.png")); dialog.Close();
        var legacyStore = new LocalStore(Path.Combine(output, "legacy-profile"));
        legacyStore.Save(new UserState { EncryptedSubscriptionId = SecretProtection.Protect("原来的订阅号") });
        using (var legacy = new MainViewModel(new XIslandApi(new HttpClient(new DelayedFeed())), legacyStore))
            Check(legacy.SubscriptionAccounts.Count == 1 && legacy.GetSubscriptionId() == "原来的订阅号", "preview9 saved number upgrades to the selected profile without re-entry");
        await LargeListAsync(owner, output, capture, Check);
        File.WriteAllText(Path.Combine(output, "cache-regression.json"), JsonSerializer.Serialize(new { Failures = failures }));
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
    }
    private static async Task LargeListAsync(MainWindow owner, string output, Action<Window, string> capture, Action<bool, string> check)
    {
        var handler = new DelayedFeed { Total = 950 };
        var store = new LocalStore(Path.Combine(output, "large-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        vm.SaveSubscriptionAccounts([new("a", "常用", " 中文 A & 1 "), new("b", "小说", "测试号乙")], "a");
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false }; window.Show();
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var loading = vm.ShowSubscriptionsAsync(); await handler.SecondPageStarted.Task;
            var firstPageMs = watch.ElapsedMilliseconds;
            check(vm.Posts.Count == 10 && !vm.SubscriptionKnown, "950 entries: first page usable without assuming complete membership");
            await Task.Delay(80); capture(window, Path.Combine(output, "progressive-loading.png"));
            vm.ShowLibrary("history"); vm.SubscriptionsCommand.Execute(null);
            check(vm.IsSubscriptions && vm.Posts.Count == 10 && vm.SubscriptionsCommand.CanExecute(null), "navigation returns immediately while background scan continues");
            check(handler.FeedCalls == 2, "navigation does not start a second scan");
            var thread = vm.OpenThreadAsync(950); await handler.ThreadStarted.Task;
            await Task.Delay(80);
            check(((FrameworkElement)window.FindName("ThreadLoadingIndicator")).IsVisible, "first thread load has a visible indicator");
            capture(window, Path.Combine(output, "thread-loading.png"));
            handler.ReleaseThread.TrySetResult(true); await thread;
            check(handler.FeedCalls == 2 && vm.Original?.Id == 950, "opening thread finishes independently from the 950-entry scan");
            handler.Release.TrySetResult(true); await loading;
            check(vm.SubscriptionCount == "950" && vm.IsSubscribed, "full list confirms membership on the final page");
            var calls = handler.FeedCalls;
            for (var i = 0; i < 4; i++) await vm.ShowSubscriptionsAsync();
            check(handler.FeedCalls == calls, "repeat navigation reuses fresh snapshot without requests");
            await vm.OpenThreadAsync(950);
            check(handler.ThreadCalls == 1, "reopening a recent thread uses the short memory cache");
            await vm.OpenThreadAsync(950, refresh: true);
            check(handler.ThreadCalls == 2, "explicit thread refresh bypasses memory cache");
            await vm.SelectForumAsync(new(4, "测试版块", "测试")); await vm.SelectForumAsync(new(4, "测试版块", "测试"));
            check(handler.ListCalls == 1, "reopening a recent forum uses the short memory cache");
            await vm.RefreshListAsync(); check(handler.ListCalls == 2, "explicit list refresh bypasses memory cache");
            await vm.ShowSubscriptionsAsync();
            handler.FailFeed = true; await vm.LoadSubscriptionsAsync(true);
            check(vm.SubscriptionCount == "950" && vm.Posts.Count == 10 && vm.SubscriptionSyncStatus.StartsWith("缓存"), "refresh failure preserves prior list, count and cache status");
            using (var reopened = new MainViewModel(new XIslandApi(new HttpClient(new DelayedFeed())), store))
                check(reopened.SubscriptionCount == "950" && reopened.GetSubscriptionId() == " 中文 A & 1 ", "restart restores exact Unicode ID and 950 count without network");
            store.SaveSubscriptions("测试号乙", "", new([new Post { Id = 5001, Content = "另一订阅号的内容" }], true, DateTimeOffset.UtcNow, []));
            vm.ActiveSubscriptionKey = "b";
            check(vm.SubscriptionCount == "1" && vm.Posts.Single().Post.Id == 5001, "account switch restores only its own snapshot");
            vm.ActiveSubscriptionKey = "a";
            check(vm.SubscriptionCount == "950" && vm.GetSubscriptionId() == " 中文 A & 1 ", "switch back restores the other snapshot immediately");
            var editor = new SubscriptionWindow(vm) { Owner = window, ShowInTaskbar = false }; editor.Show(); await Task.Delay(100);
            var textboxes = UiRefinementChecks.Descendants<TextBox>(editor).ToList();
            check(textboxes.Any(t => t.Text == " 中文 A & 1 " && System.Windows.Input.InputMethod.GetIsInputMethodEnabled(t)), "Unicode editor allows IME and displays saved value exactly");
            var note = textboxes.Single(t => t.Text == "常用"); note.Text = "主订阅";
            var save = UiRefinementChecks.Descendants<Button>(editor).Single(b => Equals(b.Content, "保存"));
            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(vm.SubscriptionAccounts.Single(a => a.Id == "a").Name == "主订阅" && !editor.IsVisible, "saving editor persists remarks and closes after success");
            await Task.Delay(100);
            check(((ComboBox)window.FindName("SubscriptionSelector")).SelectedItem is SubscriptionProfile { Id: "a", Name: "主订阅" }, "saving a remark preserves the visible current account selection");
            var selector = (ComboBox)window.FindName("SubscriptionSelector");
            selector.SelectedValue = "b"; await Task.Delay(60);
            check(vm.ActiveSubscriptionKey == "b" && vm.SubscriptionCount == "1", "dropdown selection switches the actual active profile");
            selector.SelectedValue = "a"; await Task.Delay(60);
            vm.Error = ""; await Task.Delay(100); capture(window, Path.Combine(output, "large-subscriptions-light.png"));
            GetCursorPos(out var oldPointer);
            var oldTopmost = window.Topmost;
            try
            {
                window.Topmost = true; window.Activate(); await Task.Delay(120);
                var outside = window.PointToScreen(new Point(300, 30));
                SetCursorPos((int)outside.X, (int)outside.Y); await Task.Delay(100);
                var hover = (Button)window.FindName("SubscriptionsButton");
                var point = hover.PointToScreen(new Point(hover.ActualWidth / 2, hover.ActualHeight / 2));
                SetCursorPos((int)point.X, (int)point.Y);
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(180);
                System.Windows.Input.Mouse.Synchronize();
                var chrome = (Border)hover.Template.FindName("QuietChrome", hover);
                GetCursorPos(out var actualPointer);
                File.WriteAllText(Path.Combine(output, "hover-diagnostics.json"), JsonSerializer.Serialize(new { hover.IsMouseOver, window.IsActive, selector.IsDropDownOpen, Captured = System.Windows.Input.Mouse.Captured?.GetType().Name, ExpectedX = point.X, ExpectedY = point.Y, ActualX = actualPointer.X, ActualY = actualPointer.Y, Color = chrome.Background.ToString() }));
                check(hover.IsMouseOver && ((System.Windows.Media.SolidColorBrush)chrome.Background).Color == ((System.Windows.Media.SolidColorBrush)Application.Current.FindResource("Hover")).Color, "light sidebar has a visible pointer hover background");
                capture(window, Path.Combine(output, "sidebar-hover-light.png"));
            }
            finally { window.Topmost = oldTopmost; SetCursorPos(oldPointer.X, oldPointer.Y); }
            App.ApplyTheme("dark"); await Task.Delay(100); capture(window, Path.Combine(output, "large-subscriptions-dark.png"));
            File.WriteAllText(Path.Combine(output, "large-list-results.json"), JsonSerializer.Serialize(new { Count = 950, FirstPageMs = firstPageMs, FeedRequests = calls, RepeatNavigationRequests = 0, CacheRestored = true }));
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct Pointer { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetCursorPos(out Pointer point);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private sealed class DelayedFeed : HttpMessageHandler
    {
        public int Total = 20, FeedCalls, ThreadCalls, ListCalls;
        public bool FailFeed;
        public TaskCompletionSource<bool> SecondPageStarted { get; } = new();
        public TaskCompletionSource<bool> Release { get; } = new();
        public TaskCompletionSource<bool> ThreadStarted { get; } = new();
        public TaskCompletionSource<bool> ReleaseThread { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("thread"))
            {
                ThreadCalls++; ThreadStarted.TrySetResult(true); await ReleaseThread.Task.WaitAsync(ct);
                return new(HttpStatusCode.OK) { Content = new StringContent("{\"id\":950,\"content\":\"正文先显示，不等待订阅扫描\"}") };
            }
            if (request.RequestUri.AbsolutePath.EndsWith("showf"))
            {
                ListCalls++; return new(HttpStatusCode.OK) { Content = new StringContent("[{\"id\":900,\"content\":\"列表缓存\"}]") };
            }
            FeedCalls++;
            if (FailFeed) throw new HttpRequestException("synthetic offline");
            var page = int.Parse(request.RequestUri!.Query.Split("page=")[1]);
            if (page == 2) { SecondPageStarted.TrySetResult(true); await Release.Task.WaitAsync(ct); }
            await Task.Delay(1, ct);
            var posts = Enumerable.Range(1, Total).Skip((page - 1) * 10).Take(10).Select(i => new Post { Id = i, Content = "缓存测试" }).ToArray();
            // 公网字段与 C# 模型字段不同，使用实际 API 形状。
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(posts.Select(p => new { id = p.Id, content = p.Content }))) };
        }
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class FloatingActionsChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new Fixture();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(Path.Combine(output, "actions-profile")));
        vm.State.Forums = [new(30, "综合版1", "综合"), new(1, "综合线", "时间线", IsTimeline: true)];
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false, Width = 1280, Height = 760 };
        try
        {
            window.Show(); await Pause();
            var publish = (Button)window.FindName("NewThreadButton");
            var reply = (Button)window.FindName("ReplyButton");
            var list = (ListBox)window.FindName("ThreadList");
            var reader = (ListBox)window.FindName("ReaderList");
            Require(!vm.State.HideSpoilers && !publish.IsVisible && !reply.IsVisible, "初始防剧透或无内容时的按钮状态错误");
            await vm.SelectForumAsync(new(30, "综合版1", "综合")); await Pause();
            Require(publish.IsVisible && !reply.IsVisible, "板块/未打开串的按钮状态错误");
            await vm.OpenThreadAsync(101, 1); await Pause();
            Require(reply.IsVisible && reply.IsEnabled, "打开串后没有可用的回应按钮");
            var tail = (Border)window.FindName("ListTailIndicator");
            Require(!tail.IsVisible, "没有续页提示时仍显示底部遮挡");
            CheckAlignment();
            var viewport = Bounds(list, window);
            handler.ListGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            handler.FailList = true;
            var loadingList = vm.LoadMoreListAsync(); await Pause();
            Require(vm.ListAppending && tail.IsVisible && Bounds(list, window)==viewport, "续页加载提示缺失或改变视口");
            handler.ListGate.SetResult(); await loadingList; await Pause();
            Require(vm.ListAppendFailed && tail.IsVisible && Bounds(list, window)==viewport, "续页错误提示缺失或改变视口");
            capture(window,Path.Combine(output,"floating-list-retry.png"));
            handler.FailList=false;
            var retry=UiRefinementChecks.Descendants<Button>(tail).Single();
            Require(retry.IsVisible && retry.IsEnabled,"重试入口不可用");
            ((IInvokeProvider)new ButtonAutomationPeer(retry).GetPattern(PatternInterface.Invoke)).Invoke(); await Pause();
            Require(!vm.ListAppendFailed && vm.ListTailStatus=="已到底" && Bounds(list,window)==viewport,"重试未到末页或改变视口");
            foreach (var (items, button) in new[] { (list, publish), (reader, reply) })
            {
                var before = Bounds(button, window);
                var scroll = UiRefinementChecks.Descendants<ScrollViewer>(items).First();
                scroll.ScrollToVerticalOffset(300); await Pause();
                Require(Bounds(button, window) == before, "悬浮按钮跟随正文滚动");
                scroll.ScrollToEnd(); await Pause();
                items.ScrollIntoView(items.Items[items.Items.Count - 1]); await Pause();
                scroll.ScrollToEnd(); await Pause();
                var last = (ListBoxItem)items.ItemContainerGenerator.ContainerFromIndex(items.Items.Count - 1);
                Require(last is not null && Bounds(last, window).Bottom <= Bounds(button, window).Top, "最后一条内容无法滚动到按钮上方");
                Require(UiRefinementChecks.Descendants<ListBoxItem>(items).Count(c => c.Margin.Bottom > 0) == 1, "悬浮按钮留白被重复加到其他条目");
                Require(VirtualizingPanel.GetIsVirtualizing(items), "悬浮按钮关闭了列表虚拟化");
            }
            // 追加内容后，原来的末项应撤去留白，留白移到新的末项。
            vm.Posts.Add(new(new Post { Id = 909, Content = "新末项", UserHash = "test" }, "综合版1")); await Pause();
            list.ScrollIntoView(vm.Posts[^1]); await Pause();
            var oldTail = list.ItemContainerGenerator.ContainerFromIndex(vm.Posts.Count - 2) as ListBoxItem;
            var newTail = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(vm.Posts.Count - 1);
            Require(oldTail?.Margin.Bottom == 0 && newTail.Margin.Bottom == publish.Margin.Bottom + 64, "续页后尾部留白未转移");
            Console.WriteLine("PASS: pane-fixed positions, last item clearance, append clearance transfer, virtualization.");

            await CheckComposer(publish, window, false);
            await CheckComposer(reply, window, true);
            vm.ShowLibrary("history"); await Pause();
            Require(!publish.IsVisible && reply.IsVisible, "历史列表仍显示发串，或误隐藏了已打开串的回应");
            await vm.ShowSubscriptionsAsync(); await Pause();
            Require(!publish.IsVisible && reply.IsVisible, "订阅列表仍显示发串，或误隐藏了已打开串的回应");
            await vm.SelectForumAsync(new(1, "综合线", "时间线", IsTimeline: true)); await Pause();
            Require(publish.IsVisible, "时间线没有恢复发串入口");
            handler.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            var loading = vm.OpenThreadAsync(102, 1, refresh: true); await Pause();
            Require(!reply.IsEnabled, "正在切换串时仍可回应旧串");
            handler.Gate.SetResult(); await loading; await Pause();
            Require(reply.IsEnabled, "新串加载完成后回应未恢复");

            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme);
                foreach (var width in new[] { 900d, 1280d })
                {
                    window.Width = width; await Pause();
                    CheckAlignment();
                    foreach (var (items, button) in new[] { (list, publish), (reader, reply) })
                    {
                        var frame = Bounds(items, window); var action = Bounds(button, window);
                        Require(action.Left >= frame.Left && action.Right <= frame.Right && action.Bottom <= frame.Bottom && action.Top >= frame.Top, "悬浮按钮超出对应内容区");
                        Require(action.Width == 48 && action.Height == 48 && button.Focusable, "按钮大小或键盘焦点错误");
                        Require(button.InputHitTest(new Point(24, 24)) is not null, "按钮无法命中");
                    }
                    capture(window, Path.Combine(output, $"floating-{theme}-{width}.png"));
                }
            }
            window.WindowState=WindowState.Maximized; await Pause(); CheckAlignment();
            capture(window,Path.Combine(output,"floating-dark-maximized.png"));
            App.ApplyTheme("light");
            Console.WriteLine("PASS: full-height list, aligned floating actions, loading/failure/retry/end without viewport shift, visible contexts, compose/reply targets, loading guard, both themes, compact and maximized windows.");

            void CheckAlignment()
            {
                Require(Math.Abs(Bounds(publish,window).Top-Bounds(reply,window).Top)<1,"发串与回应按钮未对齐");
                var pane=(Border)window.FindName("ListPane");
                Require(Math.Abs(Bounds(list,window).Bottom-Bounds(pane,window).Bottom)<1,"串列表底部仍有固定占位条");
            }
        }
        finally { App.ApplyTheme("light"); window.Close(); }
    }

    private static async Task CheckComposer(Button button, MainWindow owner, bool isReply)
    {
        var result = new TaskCompletionSource();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        var attempts = 0;
        timer.Tick += (_, _) =>
        {
            var dialog = owner.OwnedWindows.OfType<ComposerWindow>().FirstOrDefault();
            if (dialog is null && ++attempts < 30) return;
            timer.Stop();
            try
            {
                Require(dialog is not null, "点击悬浮按钮没有打开编辑器");
                Require(dialog!.Title.StartsWith(isReply ? "参与回应" : "发串"), "打开了错误的编辑器");
                if (isReply) Require(UiRefinementChecks.Descendants<TextBlock>(dialog).Any(t => t.Text == "No.101"), "回应目标串号错误");
                else Require(UiRefinementChecks.Descendants<ComboBox>(dialog).Any(c => AutomationProperties.GetName(c) == "发串版块" && c.SelectedItem is Forum { Id: 30 }), "发串没有选中当前板块");
                result.SetResult();
            }
            catch (Exception ex) { result.SetException(ex); }
            finally { dialog?.Close(); }
        };
        timer.Start();
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
        await result.Task;
    }

    private static Rect Bounds(FrameworkElement item, Window window) => item.TransformToAncestor(window).TransformBounds(new Rect(item.RenderSize));
    private static async Task Pause() => await Task.Delay(180);
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    // 只提供合成 GET 数据；测试不会读取用户身份或提交真实发言。
    private sealed class Fixture : HttpMessageHandler
    {
        internal TaskCompletionSource? Gate;
        internal TaskCompletionSource? ListGate;
        internal bool FailList;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.Method == HttpMethod.Get, "验证禁止发出写请求");
            object payload;
            if (request.RequestUri!.AbsolutePath.Contains("thread"))
            {
                if (Gate is not null) await Gate.Task.WaitAsync(cancellationToken);
                payload = new { id = 101, fid = 30, title = "悬浮回应验收", user_hash = "test", content = "串首正文", ReplyCount = 12,
                    Replies = Enumerable.Range(1, 12).Select(i => new { id = 101 + i, fid = 30, user_hash = "user", content = $"第 {i} 条回应<br>多行正文用于验证滚动位置。<br>按钮应固定在右下角。", now = "2026-10-27" }) };
            }
            else
            {
                var page=System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["page"];
                if(page=="2")
                {
                    if(ListGate is not null) await ListGate.Task.WaitAsync(cancellationToken);
                    if(FailList) return new(HttpStatusCode.ServiceUnavailable) {Content=new StringContent("temporary failure")};
                }
                payload = Enumerable.Range(1, page=="2"?0:20).Select(i => new { id = 200 + i, fid = 30, title = $"第 {i} 个串", user_hash = "test", content = "内容列表滚动检查，末项应该可以滚动到按钮上方。", ReplyCount = 12 });
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(payload)) };
        }
    }
}

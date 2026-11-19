using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ThreadNavigationChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new PageHandler();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(Path.Combine(output, "navigation-profile")));
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        try
        {
            window.Show(); await vm.OpenThreadAsync(1000, 1); await Task.Delay(200);
            var reader = (ListBox)window.FindName("ReaderList");
            var input = UiRefinementChecks.Descendants<TextBox>(window).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "回复页码");
            input.Text = "474"; ((Button)window.FindName("JumpPageButton")).Focus();
            input.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            await Task.Delay(40);
            Require(input.IsKeyboardFocused && input.SelectedText == "474", "首次点击页码框没有全选现有页码。");
            // 已获得焦点后，拖选/定位的起始按下不能再被全选逻辑吞掉。
            input.Select(1, 1);
            var nextClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent };
            input.RaiseEvent(nextClick);
            Require(!nextClick.Handled && input.SelectedText == "7", "再次按下鼠标覆盖选区，无法拖选页码。");
            ((Button)window.FindName("JumpPageButton")).Focus(); input.Focus();
            Require(input.SelectedText == "474", "键盘进入页码框没有全选。");
            input.SelectedText = "5";
            Require(input.Text == "5", "直接输入不能替换已选页码。");
            capture(window, Path.Combine(output, "page-input-selection.png"));
            input.Text = "5";
            input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            await Until(() => handler.Pages.Contains(5) && !vm.ThreadBusy, "回车未请求指定的第 5 页"); await Task.Delay(200);
            capture(window, Path.Combine(output, "jump-page5.png"));
            File.WriteAllText(Path.Combine(output, "jump-observation.json"), JsonSerializer.Serialize(new { handler.Pages, vm.PageText, vm.ThreadPageLabel, FirstPost = vm.ReadingPosts[0].Post.Id, Originals = vm.ReadingPosts.Count(p => p.IsOriginal) }));
            Require(vm.ReadingPosts.All(p => !p.IsOriginal) && vm.ReadingPosts[0].Post.Id == 5001, "跳到第 5 页后仍首先显示串首，没有从目标页回复开始阅读。");
            Require(handler.Pages.SequenceEqual(new[] { 1, 5 }), "跳页时自动加载了目标页以外的内容。");
            Console.WriteLine("PASS: Enter jumps directly to requested page without showing original post or prefetching other pages.");

            var viewer = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            var gate = handler.Block(4);
            Wheel(reader, 120);
            await Until(() => handler.Pages.Contains(4), "在目标页顶部向上滚动没有加载第 4 页");
            Require(vm.ThreadPrepending && vm.ReadingPosts.Count == 19 && vm.CanInteractThread, "前页等待期间清空了正文或禁止阅读操作。");
            for (var i = 0; i < 4; i++) Wheel(reader, 120);
            // 请求开始后仍允许阅读移动，锚点应以响应落地时的视口为准。
            viewer.ScrollToVerticalOffset(250); await Task.Delay(180);
            var anchor = Anchor(reader);
            gate.TrySetResult(); await Until(() => !vm.ThreadBusy, "前页请求未结束"); await Task.Delay(240);
            Require(Stayed(reader, anchor), "插入前页后眼前回复的位置变化。");
            Require(vm.ReadingPosts.Count == 38 && vm.ReadingPosts[0].Post.Id == 4001 && handler.Pages.Count(p => p == 4) == 1, "前页未按顺序插入或重复请求。");
            Require(!handler.Pages.Contains(3) && vm.ReadingPosts.All(p => !p.IsOriginal), "加载前页后自动读回首页或重复显示串首。");
            capture(window, Path.Combine(output, "previous-page-anchor.png"));

            gate = handler.Block(6); Bottom(reader, viewer);
            await Until(() => handler.Pages.Contains(6), "向下滚动未请求第 6 页");
            input.Text = "9"; gate.TrySetResult();
            await Until(() => !vm.ThreadBusy, "后页请求未结束"); await Task.Delay(220);
            Require(vm.ReadingPosts.Count == 57 && input.Text == "9", "续载覆盖了正在输入的页码或没有追加回复。");
            Require(vm.ThreadPageLabel.Contains("4–6"), "前后页边界混淆。");

            // 自动续载阻塞时可跳页；连续两次显式跳页也必须以最后一次为准。
            gate = handler.Block(7); Bottom(reader, viewer);
            await Until(() => handler.Pages.Contains(7), "未进入慢续载");
            Jump(input, 8); await Until(() => handler.Pages.Contains(8) && !vm.ThreadBusy, "续载期间的跳页被忽略");
            gate.TrySetResult(); await Task.Delay(200);
            Require(vm.ReadingPosts[0].Post.Id == 8001 && vm.ReadingPosts.Count == 19, "迟到续页混入了新的跳转结果。");
            gate = handler.Block(9); Jump(input, 9);
            await Until(() => handler.Pages.Contains(9), "未开始第 9 页跳转");
            Jump(input, 3); await Until(() => handler.Pages.Contains(3) && !vm.ThreadBusy, "第二次显式跳转被第一条命令忽略");
            gate.TrySetResult(); await Task.Delay(200);
            Require(vm.ReadingPosts[0].Post.Id == 3001 && input.Text == "3", "迟到跳页覆盖最后一次跳转。");
            Console.WriteLine("PASS: bidirectional loading, response-time anchor, single requests, draft page text, latest jump wins.");

            vm.OnlyPo = true; await Task.Delay(150);
            Require(vm.ReadingPosts.Count == 0 && !vm.ReadingPosts.Any(p => p.IsOriginal), "PO 过滤在非首页重新插入串首。");
            Wheel(reader, 120); await Until(() => vm.ReadingPosts.Count == 1 && !vm.ThreadBusy, "过滤为空时无法向前寻找 PO 回复");
            Require(vm.ReadingPosts[0].Post.Id == 2001 && vm.ReadingPosts[0].IsPo, "向前加载的 PO 回复不正确。");
            Wheel(reader, 120); await Until(() => !vm.HasPreviousThread && !vm.ThreadBusy, "未加载至第 1 页");
            Require(vm.ReadingPosts.Count(p => p.IsOriginal) == 1 && vm.ReadingPosts[0].IsOriginal, "到第 1 页时主楼缺失或重复。");
            vm.OnlyPo = false;

            input.Text = "10";
            ((IInvokeProvider)new ButtonAutomationPeer((Button)window.FindName("JumpPageButton")).GetPattern(PatternInterface.Invoke)).Invoke();
            await Until(() => handler.Pages.Contains(10) && !vm.ThreadBusy, "跳转按钮没有请求末页"); await Task.Delay(180);
            Require(vm.ReadingPosts.Count == 19 && !vm.HasMoreThread, "末页继续向下请求或包含串首。");
            handler.FailPage = 9;
            reader.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Home) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            await Until(() => vm.ThreadPrependFailed, "前页失败没有重试状态");
            var failedCalls = handler.Pages.Count;
            Wheel(reader, 120); await Task.Delay(200);
            Require(handler.Pages.Count == failedCalls && vm.ReadingPosts.Count == 19, "前页失败后自动重试或丢失已读内容。");
            capture(window, Path.Combine(output, "previous-page-retry.png"));
            handler.FailPage = 0; vm.RetryPreviousThreadCommand.Execute(null);
            await Until(() => !vm.ThreadBusy && !vm.ThreadPrependFailed, "前页手动重试未完成");
            Require(vm.ReadingPosts.Count == 38 && vm.ReadingPosts[0].Post.Id == 9001 && !vm.HasMoreThread, "重试加载了错误页或改坏后端边界。");

            var calls = handler.Pages.Count;
            foreach (var invalid in new[] { "0", "11", "abc" })
            {
                input.Text = invalid; Enter(input); await Task.Delay(40);
                Require(vm.HasError && handler.Pages.Count == calls && vm.ReadingPosts.Count == 38, "无效页码发出请求或清空了正文。");
            }
            vm.Error = ""; input.Text = "10";
            App.ApplyTheme("dark"); await Task.Delay(180); capture(window, Path.Combine(output, "navigation-dark.png"));
            window.Width = 900; await Task.Delay(180); capture(window, Path.Combine(output, "navigation-compact.png"));
            var jumpButton = (Button)window.FindName("JumpPageButton");
            Require(jumpButton.IsVisible && jumpButton.TransformToAncestor(window).Transform(new Point()).X + jumpButton.ActualWidth < window.ActualWidth, "窄窗口跳转按钮不可见。");

            await vm.OpenThreadAsync(2000, 5); await vm.LoadPreviousThreadAsync();
            Require(vm.ReadingPosts.Count == 37 && vm.ReadingPosts.Select(p => p.Post.Id).Distinct().Count() == 37, "前插页的跨页重复没有去重。");
            await vm.LoadPreviousThreadAsync();
            Require(!vm.HasPreviousThread && vm.ReadingPosts.Count == 37, "重复的前页没有停止继续请求。");
            await vm.OpenThreadAsync(3000, 5); await vm.LoadPreviousThreadAsync();
            Require(!vm.HasPreviousThread && vm.ReadingPosts.Count == 19, "空前页没有停止或清空了已读内容。");
            File.WriteAllText(Path.Combine(output, "navigation-results.json"), JsonSerializer.Serialize(new
            {
                TargetPageOnly = true, PreviousAndNext = true, StableAnchor = true, NoRepeatedOriginal = true,
                EmptyPoPrevious = true, LatestJumpWins = true, DraftInputPreserved = true, PreviousRetry = true,
                EndStops = true, PreviousDedupAndEmpty = true, InvalidInput = true, KeyboardAndButton = true, RequestPages = handler.Pages
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: PO empty page, original at page one only, previous retry/dedup/empty, end page, invalid input, keyboard/button, compact and dark UI.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }

    private static void Jump(TextBox input, int page) { input.Text = page.ToString(); Enter(input); }
    private static void Enter(TextBox input) => input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
    private static void Wheel(UIElement source, int delta) => source.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
    private static void Bottom(ListBox reader, ScrollViewer viewer)
    {
        reader.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(reader), Environment.TickCount, Key.End) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        viewer.ScrollToEnd();
    }
    private static (object Item, double Y) Anchor(ListBox list)
    {
        list.UpdateLayout();
        var item = UiRefinementChecks.Descendants<ListBoxItem>(list).Where(c => c.ActualHeight > 0 && c.TransformToAncestor(list).Transform(new Point()).Y >= 0).OrderBy(c => c.TransformToAncestor(list).Transform(new Point()).Y).First();
        return (item.DataContext, item.TransformToAncestor(list).Transform(new Point()).Y);
    }
    private static bool Stayed(ListBox list, (object Item, double Y) anchor)
    {
        list.UpdateLayout();
        return list.ItemContainerGenerator.ContainerFromItem(anchor.Item) is ListBoxItem item && Math.Abs(item.TransformToAncestor(list).Transform(new Point()).Y - anchor.Y) < 3;
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(20); }
        throw new InvalidOperationException(message);
    }
    private sealed class PageHandler : HttpMessageHandler
    {
        public List<int> Pages { get; } = [];
        public int FailPage;
        private readonly Dictionary<int, TaskCompletionSource> _gates = [];
        public TaskCompletionSource Block(int page) => _gates[page] = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(s => s.Split('=')).ToDictionary(s => s[0], s => s[1]);
            var page = int.Parse(query.GetValueOrDefault("page", "1")); Pages.Add(page);
            if (_gates.TryGetValue(page, out var gate)) await gate.Task; // 故意忽略取消，验证迟到响应隔离。
            if (FailPage == page) throw new HttpRequestException("Synthetic previous-page failure");
            var id = int.Parse(query["id"]);
            var dataPage = id == 2000 && page == 3 ? 4 : page;
            var value = new { id, title = "双向阅读测试", content = "主楼只应在第 1 页出现", user_hash = "Author", ReplyCount = 190,
                Replies = Enumerable.Range(1, id == 3000 && page == 4 ? 0 : 19).Select(i => new { id = id == 2000 && dataPage == 4 && i == 19 ? 5001 : dataPage * 1000 + i, user_hash = page == 2 && i == 1 ? "Author" : "Reader", content = $"第 {page} 页，第 {i} 条回复<br>" + string.Join("<br>", Enumerable.Repeat("测试不同高度的回复正文。", 2 + i % 5)) }) };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
        }
    }
}

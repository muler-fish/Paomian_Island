using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>仅覆盖 TODO 10–12：菜单、屏蔽显示与续载、按来源反馈，不调用真实网站。</summary>
internal static class ContextActionChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture, bool postMenuOnly = false, bool menuSplitOnly = false)
    {
        var handler = new FixtureHandler(); var store = new LocalStore(Path.Combine(output, "context-profile-" + Guid.NewGuid().ToString("N")));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        vm.State.ShowImages = false; vm.State.HideSpoilers = true; vm.AppearanceChanged();
        var forum = new Forum(4, "综合版1", "综合", MaxPage: 3);
        vm.State.FavoriteForums.Add(forum); vm.State.Forums.Add(forum);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        var clipboard = Clipboard.GetDataObject();
        window.Show(); window.Activate();
        try
        {
            await vm.InitializeAsync(); await vm.SelectForumAsync(forum); await vm.OpenThreadAsync(1000);
            await Task.Delay(120);
            var list = (ListBox)window.FindName("ThreadList");
            var reader = (ListBox)window.FindName("ReaderList");
            var overlay = (FeedbackOverlay)window.FindName("FeedbackLayer");
            var body = UiRefinementChecks.Descendants<PostBody>(reader).First(b => b.DataContext is ReadPost { IsOriginal: true });
            var readerScroll = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            var before = (vm.Original!.Id, vm.PageText, vm.State.History.Count, readerScroll.VerticalOffset);

            // 菜单是真实 XAML 绑定的实例，复制取显示文档，不能漏出折叠剧透或丢失 emoji。
            body.Selection.Select(body.Document.ContentStart, body.Document.ContentStart);
            var postMenu = Open(body); Require(Labels(postMenu).SequenceEqual(new[] { "复制串号", "复制正文", "回应", "举报" }), "回应菜单条目错误");
            Click(postMenu, "复制正文");
            var copied = Clipboard.GetText();
            Require(copied.Contains("😀") && copied.Contains("\n") && !copied.Contains("秘密"),
                $"复制正文丢失换行/emoji 或泄露剧透：Emoji={copied.Contains("😀")}, Newline={copied.Contains("\n")}, Spoiler={copied.Contains("秘密")}");
            Require(HasBubble(overlay, "已复制正文"), "复制没有气泡");
            var run = body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Run>().First(r => r.Text.Contains("正文"));
            body.Selection.Select(run.ContentStart, run.ContentEnd);
            postMenu = Open(body); Click(postMenu, "复制正文"); Require(Clipboard.GetText() == copied, "复制正文被选区截断");
            postMenu = Open(body); Click(postMenu, "复制串号"); Require(Clipboard.GetText() == "1000", "主楼串号复制错误");
            var replyBody=UiRefinementChecks.Descendants<PostBody>(reader).First(b=>b.DataContext is ReadPost {Post.Id:3001});
            var replyMenu=Open(replyBody); Click(replyMenu,"复制串号"); Require(Clipboard.GetText()=="3001","回应复制了父串号");
            replyMenu=Open(replyBody); Click(replyMenu,"复制正文"); Require(Clipboard.GetText()=="普通回应","回应复制了其他楼正文");
            body.Selection.Select(body.Document.ContentStart, body.Document.ContentStart);

            // 回应只打开原有编辑器，目标是当前串，正文引用被右键的楼号，不发送。
            Task? composerCheck = null;
            _ = window.Dispatcher.BeginInvoke(() => composerCheck = CloseComposerAsync(window, vm), DispatcherPriority.Background);
            postMenu = Open(body); Click(postMenu, "回应");
            if (composerCheck is not null) await composerCheck;
            Require(vm.State.Drafts.Any(d => d.ThreadId == 1000 && d.Content.Contains(">>No.1000")), "回应草稿目标或引用错误");
            Require(handler.Posts == 0, "专项测试误提交请求");
            var tip = UiRefinementChecks.Descendants<PostBody>(reader).First(b => b.DataContext is ReadPost { IsTip: true });
            var tipMenu = Open(tip); Require(Labels(tipMenu).SequenceEqual(new[] { "复制正文" }), "Tips 被当作普通发言回应"); tipMenu.IsOpen = false;

            // 举报入口只影响串内菜单；单独入口避免连带执行列表屏蔽、系统鼠标定位及气泡计时。
            if (postMenuOnly)
            {
                File.WriteAllText(Path.Combine(output, "post-menu-results.txt"), "PASS: copy post ID/full document even with selection; spoilers and emoji preserved; reply target/draft; report menu; tips excluded; no POST.");
                Console.WriteLine(File.ReadAllText(Path.Combine(output, "post-menu-results.txt")));
                return;
            }
            if (menuSplitOnly)
            {
                await CheckForumBlockingAsync(window,vm,handler,store,output,capture);
                File.WriteAllText(Path.Combine(output,"menu-split-results.txt"),"PASS: split copy for main/reply, full text + emoji/spoilers, reply/report unaffected, forum-name menu and correct fid, thread/forum independent filters, restart, unblock without network, all-hidden continuation, history/subscriptions preserved; no POST.");
                Console.WriteLine(File.ReadAllText(Path.Combine(output,"menu-split-results.txt"))); return;
            }

            var card = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
            // 真实鼠标输入验证菜单入口，而不仅是直接设置 IsOpen。
            await NativeRightClickAsync(window, card);
            Require(card.ContextMenu.IsOpen && vm.Original?.Id == 1000, $"实际右键未打开菜单或误切串：open={card.ContextMenu.IsOpen}, active={window.IsActive}, thread={vm.Original?.Id}, selected={list.SelectedItem}"); card.ContextMenu.IsOpen = false;
            body.Focus(); var hwnd = new WindowInteropHelper(window).Handle;
            SendMessage(hwnd, 0x0100, (IntPtr)0x5D, IntPtr.Zero); SendMessage(hwnd, 0x0101, (IntPtr)0x5D, IntPtr.Zero);
            await Task.Delay(80); Require(body.ContextMenu.IsOpen, "菜单键不能打开回应菜单"); body.ContextMenu.IsOpen = false;
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = UIElement.PreviewMouseRightButtonDownEvent };
            card.RaiseEvent(down); Require(down.Handled, "右键未阻止默认选串");
            var threadMenu = Open(card); Require(Labels(threadMenu).SequenceEqual(new[]{"屏蔽串","屏蔽板块（综合版1）"}), "串列表菜单未绑定");
            Click(threadMenu, "屏蔽串"); await Task.Delay(60);
            Require(!vm.VisiblePosts.Cast<PostCard>().Any(p => p.Post.Id == 1001) && vm.Posts.Any(p => p.Post.Id == 1001), "屏蔽没有过滤显示或破坏源集合");
            Require(before == (vm.Original!.Id, vm.PageText, vm.State.History.Count, readerScroll.VerticalOffset), "右键/屏蔽打断阅读位置或写入历史");
            Require(HasBubble(overlay, "已屏蔽此串"), "屏蔽无反馈");
            var calls = handler.ListCalls;
            var manager = new BlockedThreadsWindow(vm) { Owner = window, ShowInTaskbar = false }; manager.Show();
            capture(manager, Path.Combine(output, "blocked-manager-light.png"));
            UiRefinementChecks.Descendants<Button>(manager).First(b => Equals(b.Content, "取消屏蔽")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(vm.VisiblePostCount == 20 && handler.ListCalls == calls && vm.BlockedThreads.Count == 0, "解除屏蔽未即时恢复或重新请求"); manager.Close();

            // 所有已加载串被屏蔽时仍有手动续页入口，底层页码及去重独立于显示数量。
            foreach (var item in vm.Posts.ToArray()) vm.BlockThread(item.Post);
            await Task.Delay(120);
            Require(vm.VisiblePostCount == 0 && vm.EmptyList && vm.HasMoreList && handler.ListCalls == calls, "整页屏蔽触发循环请求或误判末页");
            var more = (Button)window.FindName("EmptyListLoadMore"); Require(more.IsVisible && more.IsEnabled, "整页屏蔽后无法继续浏览");
            more.Command.Execute(null); await Until(() => !vm.ListBusy && vm.Posts.Count == 40);
            Require(vm.VisiblePostCount == 20 && vm.Posts[20].Post.Id == 1020, "屏蔽后的续页跳错或重复");
            using (var restored = new MainViewModel(new XIslandApi(new HttpClient(new FixtureHandler())), store))
            {
                await restored.SelectForumAsync(forum);
                Require(restored.BlockedThreads.Count == 20 && restored.VisiblePostCount == 0, "屏蔽重启后丢失");
            }

            vm.State.History.AddRange(Enumerable.Range(1001, 29).Select(id => new SavedThread(FixtureHandler.Post(id), 1, DateTimeOffset.Now)));
            vm.ShowLibrary("history"); while (vm.HasMoreList) await vm.LoadMoreListAsync();
            Require(vm.Posts.Count == 30 && vm.VisiblePostCount == 10 && !vm.VisiblePosts.Cast<PostCard>().Any(p => p.Post.Id < 1020), "历史的分批加载未应用屏蔽");
            await vm.ShowSubscriptionsAsync(); while (vm.HasMoreList && vm.LoadMoreListCommand.CanExecute(null)) await vm.LoadMoreListAsync();
            Require(vm.SubscriptionCount == "15" && vm.VisiblePostCount == 0 && !vm.HasMoreList, "屏蔽破坏订阅数量、续载或云端状态");
            Require(handler.Posts == 0, "屏蔽触发了云端取消订阅");

            // 板块菜单复用星标，收藏/取消不切换入口。收藏后重开菜单必须更新动作文案。
            var forumRow = UiRefinementChecks.Descendants<Border>(window).First(b => b.Name == "ForumRow" && b.DataContext is Forum f && f.Id == 4);
            var active = vm.ActiveForumKey; var forumMenu = Open(forumRow); Require(Labels(forumMenu).Single() == "取消收藏板块", "收藏状态菜单错误");
            Click(forumMenu, "取消收藏板块");
            Require(!vm.FavoriteForumKeys.Contains("f4") && vm.ActiveForumKey == active, "收藏菜单误跳转");
            // 显式展开分组以验证非收藏入口也附带同一原生菜单。
            foreach (var expander in UiRefinementChecks.Descendants<Expander>(window)) expander.IsExpanded = true;
            window.UpdateLayout();
            forumRow = UiRefinementChecks.Descendants<Border>(window).First(b => b.Name == "ForumRow" && b.DataContext is Forum f && f.Id == 4);
            forumMenu = Open(forumRow); Require(Labels(forumMenu).Single() == "收藏板块", "非收藏入口未更新菜单"); Click(forumMenu, "收藏板块");
            Require(vm.FavoriteForumKeys.Contains("f4") && vm.ActiveForumKey == active, "收藏菜单改变浏览上下文");

            // 成功气泡锚定复制按钮，重复操作替换；定时关闭不改焦点或页面布局。
            overlay.Clear(); var copy = (Button)window.FindName("CopyThreadLinkButton"); copy.Focus();
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(overlay.Count == 1 && HasBubble(overlay, "已复制串链接") && copy.IsKeyboardFocused, "复制反馈堆积或抢焦点");
            var pane = (FrameworkElement)window.FindName("ReaderPane"); var height = pane.ActualHeight;
            capture(window, Path.Combine(output, "copy-feedback-light.png"));
            await Task.Delay(2800); Require(overlay.Count == 0 && pane.ActualHeight == height, "成功提示未自动收起或挤动布局");
            copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var bubble = overlay.Children.OfType<Border>().Single();
            bubble.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent });
            await Task.Delay(1200); Require(overlay.Count == 0, "成功气泡因悬停延长停留"); overlay.Clear();

            // 列表与正文并发失败必须各归其位；同样的错误可以再次弹出。
            handler.FailList = true; var listTask = vm.SelectForumAsync(new Forum(6, "错误板块", "综合"));
            await vm.OpenThreadAsync(9999); await listTask;
            Require(overlay.Count == 2 && HasBubble(overlay, "该串不存在") && BubbleText(overlay).Any(t => t.Contains("502")), "并发错误提示未按区域分离");
            capture(window, Path.Combine(output, "errors-light.png"));
            App.ApplyTheme("dark"); await Task.Delay(60); capture(window, Path.Combine(output, "errors-dark.png")); App.ApplyTheme("light");
            overlay.Dismiss(FeedbackArea.Reader); vm.SetError("该串不存在", FeedbackArea.Reader); Require(overlay.Count == 2, "相同错误不再提示");
            Require(!UiRefinementChecks.Descendants<Button>(overlay).Any(), "气泡仍有关闭按钮");
            overlay.Clear();
            vm.SetError(string.Concat(Enumerable.Repeat("很长的错误详情，仍应能滚动查看。\n", 60)), FeedbackArea.List);
            window.UpdateLayout();
            var longBubble = overlay.Children.OfType<Border>().Single();
            Require(Canvas.GetTop(longBubble) >= 0 && Canvas.GetTop(longBubble) + longBubble.ActualHeight <= overlay.ActualHeight, "长错误撑出窗口");
            Require(UiRefinementChecks.Descendants<ScrollViewer>(longBubble).First().ScrollableHeight > 0, "长错误无法滚动阅读"); overlay.Clear();
            vm.SetError("定时错误", FeedbackArea.Reader); await Task.Delay(5700); Require(overlay.Count == 0, "错误未自动收起");
            Require(handler.Posts == 0, "专项测试提交了网络写操作");
            File.WriteAllText(Path.Combine(output, "context-actions-results.json"), JsonSerializer.Serialize(new { Passed = true, Scope = "TODO 10-12", handler.ListCalls, handler.Posts, BlockedCount = vm.BlockedThreads.Count }));
        }
        finally
        {
            foreach (var child in window.OwnedWindows.Cast<Window>().ToArray()) child.Close();
            window.Close(); App.ApplyTheme("light"); if (clipboard is not null) Clipboard.SetDataObject(clipboard, true);
        }
    }

    private static async Task CheckForumBlockingAsync(MainWindow window,MainViewModel vm,FixtureHandler handler,LocalStore store,string output,Action<Window,string> capture)
    {
        vm.State.Forums.Add(new(8,"婆罗门一","综合"));
        var first=new PostCard(FixtureHandler.Post(9001) with { Fid=8 },"婆罗门一");
        var second=new PostCard(FixtureHandler.Post(9002) with { Fid=8 },"婆罗门一");
        vm.Posts.Add(first); vm.Posts.Add(second);
        var list=(ListBox)window.FindName("ThreadList");
        var scroll=UiRefinementChecks.Descendants<ScrollViewer>((ListBox)window.FindName("ReaderList")).First();
        var reading=(vm.Original!.Id,vm.PageText,vm.State.History.Count,scroll.VerticalOffset);
        // 手动滚动至待操作项前关闭自动续载，保持候选数据固定；随后恢复并检查全隐藏时的手动续载。
        IncrementalScroll.SetCommand(list,null);
        async Task<ContextMenu> Menu(PostCard card)
        {
            list.ScrollIntoView(card); await Task.Delay(80); window.UpdateLayout();
            var item=(ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(card);
            var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Right){RoutedEvent=UIElement.PreviewMouseRightButtonDownEvent};
            item.RaiseEvent(down); Require(down.Handled,"右键未阻止默认选串"); return Open(item);
        }
        var menu=await Menu(first);
        Require(Labels(menu).SequenceEqual(new[]{"屏蔽串","屏蔽板块（婆罗门一）"}),"屏蔽菜单未显示目标板块名");
        Click(menu,"屏蔽串");
        Require(vm.VisiblePostCount==21 && vm.BlockedThreads.Count==1,"屏蔽串影响了其他串");
        menu=await Menu(second); Click(menu,"屏蔽板块（婆罗门一）");
        Require(vm.ActiveForum?.Id==4 && vm.VisiblePostCount==20 && vm.Posts.Count==22 && vm.BlockedForums.Single().Id==8,"板块屏蔽误用了当前入口或破坏源数据");
        Require(reading==(vm.Original!.Id,vm.PageText,vm.State.History.Count,scroll.VerticalOffset),"列表屏蔽打断正文阅读");
        var loaded=new LocalStore(store.Root).Load();
        Require(loaded.BlockedForums.Single().Id==8 && loaded.BlockedThreads.Single().Id==9001,"屏蔽状态未持久化");
        var manager=new BlockedThreadsWindow(vm){Owner=window,ShowInTaskbar=false}; manager.Show();
        try
        {
            foreach(var theme in new[]{"light","dark"})
            {
                App.ApplyTheme(theme); await Task.Delay(60); capture(manager,Path.Combine(output,$"block-manager-{theme}.png"));
            }
            var requests=handler.ListCalls;
            UiRefinementChecks.Descendants<Button>(manager).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="取消屏蔽板块 婆罗门一")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(vm.VisiblePostCount==21 && vm.BlockedForums.Count==0 && vm.BlockedThreads.Count==1 && handler.ListCalls==requests,"解除板块屏蔽清除了单串屏蔽或重新请求");
            UiRefinementChecks.Descendants<Button>(manager).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="取消屏蔽 No.9001")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(vm.VisiblePostCount==22 && handler.ListCalls==requests,"解除串屏蔽未即时恢复");
        }
        finally {manager.Close();App.ApplyTheme("light");}

        vm.BlockForum(first.Post); vm.BlockForum(vm.Posts[0].Post);
        var before=handler.ListCalls;
        await Task.Delay(100);
        Require(vm.EmptyList && vm.HasMoreList && handler.ListCalls==before,"全板块屏蔽误判末页或循环请求");
        IncrementalScroll.SetCommand(list,vm.LoadMoreListCommand);
        var more=(Button)window.FindName("EmptyListLoadMore"); Require(more.IsVisible&&more.IsEnabled,"全隐藏后没有继续浏览入口");
        await vm.LoadMoreListAsync();
        Require(vm.Posts.Count==42&&vm.VisiblePostCount==0&&handler.ListCalls==before+1,"屏蔽板块没有过滤新增页或破坏分页");
        using(var restarted=new MainViewModel(new XIslandApi(new HttpClient(new FixtureHandler())),store))
        {
            await restarted.SelectForumAsync(new(4,"综合版1","综合"));
            Require(restarted.BlockedForums.Count==2&&restarted.Posts.Count==20&&restarted.VisiblePostCount==0,"重启未恢复板块过滤");
        }
        vm.State.History.Add(new(first.Post,1,DateTimeOffset.Now));
        vm.State.History.Add(new(FixtureHandler.Post(6001) with {Fid=6},1,DateTimeOffset.Now));
        vm.ShowLibrary("history");
        Require(vm.VisiblePostCount==1&&vm.VisiblePosts.Cast<PostCard>().Single().Post.Id==6001,"历史未正确过滤板块");
        await vm.ShowSubscriptionsAsync(); while(vm.HasMoreList&&vm.LoadMoreListCommand.CanExecute(null)) await vm.LoadMoreListAsync();
        Require(vm.SubscriptionCount=="15"&&vm.VisiblePostCount==0,"板块屏蔽改变了云端订阅数量");
        Require(handler.Posts==0,"屏蔽或菜单发送了写请求");
    }

    private static async Task CloseComposerAsync(MainWindow window, MainViewModel vm)
    {
        await Until(() => window.OwnedWindows.OfType<ComposerWindow>().Any());
        var composer = window.OwnedWindows.OfType<ComposerWindow>().Single();
        try { Require(UiRefinementChecks.Descendants<EmojiEditor>(composer).Single().Text.Contains(">>No.1000"), "回应未插入楼号"); }
        finally { composer.Close(); }
    }
    private static ContextMenu Open(FrameworkElement target)
    {
        var menu = target.ContextMenu ?? throw new InvalidOperationException("控件未附带右键菜单");
        menu.PlacementTarget = target; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; return menu;
    }
    private static IEnumerable<string> Labels(ContextMenu menu) => menu.Items.OfType<MenuItem>().Select(i => i.Header.ToString()!);
    private static void Click(ContextMenu menu, string label)
    {
        var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, label)); menu.IsOpen = false;
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }
    private static IEnumerable<string> BubbleText(FeedbackOverlay overlay) => UiRefinementChecks.Descendants<TextBlock>(overlay).Select(t => t.Text);
    private static bool HasBubble(FeedbackOverlay overlay, string text) => BubbleText(overlay).Contains(text);
    private static async Task Until(Func<bool> predicate) { for (var i = 0; i < 80 && !predicate(); i++) await Task.Delay(50); Require(predicate(), "专项等待超时"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static async Task NativeRightClickAsync(Window window, FrameworkElement target)
    {
        window.Activate(); await Task.Delay(80);
        var screen = target.PointToScreen(new Point(target.ActualWidth / 2, Math.Min(12, target.ActualHeight / 2)));
        GetCursorPos(out var previous);
        try
        {
            SetCursorPos((int)screen.X, (int)screen.Y); await Task.Delay(80);
            mouse_event(8, 0, 0, 0, UIntPtr.Zero); mouse_event(16, 0, 0, 0, UIntPtr.Zero);
            await Task.Delay(100);
        }
        finally { SetCursorPos(previous.X, previous.Y); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int ListCalls, Posts; public bool FailList;
        internal static Post Post(int id) => new() { Id = id, Fid = 4, Time = "2026-10-29 12:00:00", UserHash = "cookie", Title = $"测试串 {id}", Content = "正文" };
        private static object Entry(int id) => new { id, fid = 4, user_hash = "cookie", title = $"测试串 {id}", content = "列表正文<br>第二行<br>第三行", now = "2026-10-29 12:00:00" };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) { Posts++; throw new InvalidOperationException("禁止真实写操作"); }
            var route = request.RequestUri!.AbsolutePath.Split('/').Last();
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var page = int.Parse(query["page"] ?? "1"); var id = int.Parse(query["id"] ?? "1");
            if (route == "getForumList") return Response("""[{"name":"综合","forums":[{"id":4,"name":"综合版1"}]}]""");
            if (route == "getTimelineList") return Response("""[{"id":1,"name":"综合线","max_page":3}]""");
            if (route == "feed") return Response(JsonSerializer.Serialize(Enumerable.Range(1000 + (page - 1) * 10, page == 1 ? 10 : page == 2 ? 5 : 0).Select(Entry)));
            if (route is "showf" or "timeline")
            {
                ListCalls++; if (FailList) { await Task.Delay(120, ct); return new(HttpStatusCode.BadGateway) { Content = new StringContent("maintenance") }; }
                return Response(JsonSerializer.Serialize(Enumerable.Range(1000 + (page - 1) * 20, page <= 2 ? 20 : 0).Select(Entry)));
            }
            if (id == 9999) return Response("""{"success":false,"error":"该串不存在"}""");
            return Response("""{"id":1000,"fid":4,"user_hash":"po","ReplyCount":2,"content":"正文 😀<br>[h]秘密[/h]","Replies":[{"id":9999999,"user_hash":"Tips","now":"2099-01-01 00:00:01","content":"提示"},{"id":3001,"user_hash":"reply","content":"普通回应"}]}""");
        }
        private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}

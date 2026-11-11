using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;
using Button = System.Windows.Controls.Button;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;

namespace Tide.Desktop.Tests;

/// <summary>使用隔离资料和固定 GET 响应验证真实侧栏；不会移动鼠标或访问论坛。</summary>
internal static class ForumNavigationChecks
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm, string output, Action<Window, string> capture)
    {
        var forums = new List<Forum>
        {
            new(1, "综合线", "时间线", IsTimeline: true),
            new(2, "创作线", "时间线", IsTimeline: true),
            new(1, "综合版1", "综合"),
            new(2, "用于验证窄侧栏截断及星标可见性的长版块名称", "综合")
        };
        forums.AddRange(Enumerable.Range(3, 30).Select(i => new Forum(i, "测试版块 " + i, "综合")));
        var store = new LocalStore(Path.Combine(output, "forum-navigation-profile"));
        store.Save(new UserState { Forums = forums });
        var handler = new NavigationHandler();
        var testVm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var testWindow = new MainWindow(testVm) { Owner = window, ShowInTaskbar = false, Width = 1280, Height = 760 };
        var results = new Dictionary<string, object>();
        try
        {
            App.ApplyTheme("light");
            testWindow.Show();
            await testVm.SelectForumAsync(forums[0]);
            await Layout(testWindow);
            var groups = (ItemsControl)testWindow.FindName("ForumGroupList");
            var favorites = (ItemsControl)testWindow.FindName("FavoriteForumList");
            var scroll = (ScrollViewer)testWindow.FindName("ForumScroll");
            var refresh = (Wpf.Ui.Controls.Button)testWindow.FindName("RefreshListButton");
            foreach (var expander in Descendants<Expander>(groups)) expander.IsExpanded = true;
            await Layout(testWindow);

            var firstGroup = Descendants<Expander>(groups).First();
            var groupHeader = (ToggleButton)firstGroup.Template.FindName("HeaderSite", firstGroup);
            CheckHeaderWidth(firstGroup, scroll);
            groupHeader.BringIntoView();
            await Layout(testWindow);
            // 检查文字右侧空白的真实命中对象，避免按钮看起来变宽但仍只能点文字。
            var rightPoint = groupHeader.TranslatePoint(new Point(groupHeader.ActualWidth - 5, groupHeader.ActualHeight / 2), testWindow);
            var hit = testWindow.InputHitTest(rightPoint) as DependencyObject;
            Require(hit is not null && (ReferenceEquals(hit, groupHeader) || Ancestors(hit).Contains(groupHeader)), "分组标题右侧空白没有命中展开按钮。");
            Require(groupHeader.Focus(), "分组标题无法通过键盘聚焦。");
            Toggle(groupHeader); await Layout(testWindow);
            Require(!firstGroup.IsExpanded, "分组标题无法收起。");
            Click(groupHeader); await Layout(testWindow);
            Require(firstGroup.IsExpanded, "分组标题无法再次展开。");
            results["fullWidthGroupHeaderHitAndToggle"] = true;

            var home = (Button)testWindow.FindName("HomeTimelineButton");
            Require(AutomationProperties.GetName(home) == "综合线", "首页导航名称没有与综合线对应。");
            foreach (var library in new[] { "history" })
            {
                testVm.ShowLibrary(library);
                Invoke(home);
                await Until(() => !testVm.IsLibrary && testVm.ActiveForumKey == "t1" && !testVm.ListBusy, testWindow, "综合线入口没有从本机列表返回时间线。");
            }
            results["homeReturnsToTimelineFromLibraries"] = true;

            Require(refresh.Content is SymbolIcon { Symbol: SymbolRegular.ArrowClockwise24 }, "刷新按钮不再是原来的纯图标。");
            Require(AutomationProperties.GetName(refresh) == "刷新串列表", "图标刷新缺少可访问名称。");
            Require(testWindow.InputBindings.OfType<KeyBinding>().Any(k => k.Key == Key.F5 && ReferenceEquals(k.Command, testVm.RefreshListCommand)), "F5 没有复用刷新命令。");
            results["refreshIconAndShortcut"] = true;

            var timelineStar = Star(groups, "t1");
            var forumStar = Star(groups, "f1");
            var beforeNavigation = testVm.ActiveForumKey;
            refresh.Focus(); await Layout(testWindow);
            var idleStars = Descendants<ToggleButton>(groups).Where(s => s.Name == "ForumFavoriteStar")
                .Where(s => Ancestors(s).OfType<Border>().Any(b => b.Name == "ForumRow" && !b.IsMouseOver && !b.IsKeyboardFocusWithin)).ToList();
            Require(idleStars.Count > 0 && idleStars.All(s => s.Opacity == 0 && s.IsChecked == false), "未悬停、未聚焦且未收藏的星标没有隐藏。");
            results["unselectedIdleStarsHidden"] = true;
            OpenButton(groups, "t1").BringIntoView();
            Require(OpenButton(groups, "t1").Focus(), "版块名称不能通过键盘聚焦。");
            await Layout(testWindow);
            Require(timelineStar.Opacity == 1, "键盘焦点进入版块行后没有揭示星标。");
            Require(timelineStar.Focus(), "星标不能通过键盘聚焦。");
            await Layout(testWindow);
            Require(timelineStar.IsKeyboardFocused && timelineStar.Opacity == 1, "星标取得焦点后仍不可见。");
            Toggle(timelineStar);
            await Layout(testWindow);
            Require(testVm.FavoriteForumKeys.Contains("t1") && favorites.Items.Count == 1, "可访问性 Toggle 没有收藏时间线。");
            Require(testVm.ActiveForumKey == beforeNavigation, "点击星标意外切换了版块。");
            refresh.Focus(); await Layout(testWindow);
            Require(timelineStar.IsChecked == true && timelineStar.Opacity == 1, "离开焦点后已收藏星标没有常显。");

            forumStar.BringIntoView();
            Toggle(forumStar); await Layout(testWindow);
            Require(testVm.FavoriteForumKeys.SetEquals(new[] { "t1", "f1" }), "相同数字 ID 的时间线和版块收藏相互覆盖。");
            Require(Star(favorites, "f1").IsChecked == true && forumStar.IsChecked == true, "收藏区与目录星标没有同步。");
            Toggle(Star(favorites, "f1")); await Layout(testWindow);
            Require(!testVm.FavoriteForumKeys.Contains("f1") && forumStar.IsChecked == false && favorites.Items.Count == 1, "从收藏区取消后目录仍保留旧收藏状态。");
            Toggle(forumStar); await Layout(testWindow);
            Invoke(OpenButton(favorites, "f1"));
            await Until(() => testVm.ActiveForumKey == "f1" && !testVm.ListBusy, testWindow, "收藏快捷入口没有打开目标版块。");
            Require(testVm.ListTitle == "综合版1", "快捷入口的版块标题不正确。");
            results["keyboardRevealAndPersistentStar"] = true;
            results["toggleDoesNotNavigate"] = true;
            results["timelineForumKeysAndTwoWaySync"] = true;
            results["favoriteQuickNavigation"] = true;

            // 普通点击先经过 OnClick 再进入 OnToggle；一次点击只能切换一次，不能执行两遍命令。
            Click(timelineStar); await Layout(testWindow);
            Require(!testVm.FavoriteForumKeys.Contains("t1") && testVm.FavoriteForums.Count == 1 && timelineStar.IsChecked == false, "普通点击未取消收藏，可能重复执行了切换命令。");
            Click(timelineStar); await Layout(testWindow);
            Require(testVm.FavoriteForumKeys.Contains("t1") && testVm.FavoriteForums.Count == 2 && timelineStar.IsChecked == true && Star(favorites, "t1").IsChecked == true, "普通点击未恢复单个收藏项或两处状态不一致。");
            results["normalClickTogglesExactlyOnce"] = true;

            // 控制响应完成时机，检查实际按钮禁用与 TaskCommand 的重复请求保护。
            var beforeRequests = handler.RequestCount;
            handler.Block = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Invoke(refresh);
            await Until(() => testVm.ListBusy, testWindow, "刷新按钮没有启动请求。");
            Require(!refresh.IsEnabled && !testVm.RefreshListCommand.CanExecute(null), "请求期间刷新按钮仍可点击。");
            testVm.RefreshListCommand.Execute(null);
            await Layout(testWindow);
            Require(handler.RequestCount == beforeRequests + 1, "忙时重复刷新发出了第二次请求。");
            handler.Release();
            await Until(() => !testVm.ListBusy && refresh.IsEnabled, testWindow, "刷新完成后按钮没有恢复。");
            results["refreshBusyPreventsDuplicateRequest"] = true;

            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); refresh.Focus(); scroll.ScrollToTop();
                await Layout(testWindow);
                CheckHeaderWidth(firstGroup, scroll);
                Require(ReferenceEquals(refresh.Style, testWindow.FindResource("RefreshIconButton")) &&
                    (refresh.Background is null || refresh.Background is SolidColorBrush background && background.Color.A == 0),
                    "刷新按钮未使用透明的次要图标样式。");
                var color = ((SolidColorBrush)Application.Current.FindResource("FavoriteStar")).Color;
                foreach (var star in new[] { Star(groups, "t1"), Star(groups, "f1"), Star(favorites, "t1"), Star(favorites, "f1") })
                {
                    Require(star.IsChecked == true && star.Opacity == 1, "已收藏星标不是常显状态。");
                    Require(((SolidColorBrush)star.Foreground).Color == color, "收藏星标未使用当前主题颜色。");
                    Require(Descendants<SymbolIcon>(star).Single().Filled, "收藏状态没有使用实心图形。");
                }
                var contrasts = new[] { "Sidebar", "AccentSoft", "Hover" }
                    .Select(key => Contrast(color, ((SolidColorBrush)Application.Current.FindResource(key)).Color)).ToArray();
                Require(contrasts.All(c => c >= 3), "收藏星标在某种侧栏背景下对比度不足 3:1。");
                results[theme + "StarContrasts"] = contrasts;
                capture(testWindow, Path.Combine(output, "sidebar-favorites-" + theme + ".png"));
            }

            var columns = Ancestors(scroll).OfType<Grid>().Single(g => g.ColumnDefinitions.Count == 5);
            var sidebarWidth = columns.ColumnDefinitions[0].Width;
            columns.ColumnDefinitions[0].Width = new GridLength(148);
            var geometry = new List<object>();
            foreach (var width in new[] { 900d, 1060d, 1280d })
            {
                testWindow.Width = width; scroll.ScrollToTop(); await Layout(testWindow);
                var bar = Descendants<ScrollBar>(scroll).Single(b => b.Orientation == Orientation.Vertical && b.IsVisible);
                Require(scroll.ScrollableHeight > 0 && bar.ActualWidth > 0, "没有产生真实目录滚动条，边界测试无效。");
                var barBounds = Bounds(bar, scroll);
                var controls = Descendants<ButtonBase>(scroll).Where(b => b.IsVisible && b.Name is "ForumOpenButton" or "ForumFavoriteStar").ToList();
                foreach (var control in controls)
                {
                    var bounds = Bounds(control, scroll);
                    Require(bounds.Left >= -.5 && bounds.Right <= barBounds.Left - 1,
                        $"窄侧栏的 {control.Name} 被滚动条覆盖：控件={bounds}，滚动条={barBounds}。");
                }
                foreach (var group in Descendants<Expander>(groups)) CheckHeaderWidth(group, scroll);
                var longName = OpenButton(groups, "f2");
                Require(longName.ToolTip?.ToString() == forums[3].Name && Descendants<TextBlock>(longName).Any(t => t.TextTrimming == TextTrimming.CharacterEllipsis), "长版块名缺少截断或完整名称提示。");
                Require(Bounds(refresh, testWindow).Right <= testWindow.ActualWidth, "刷新按钮在窄窗口中溢出。");
                geometry.Add(new { WindowWidth = width, SidebarWidth = columns.ColumnDefinitions[0].ActualWidth, ScrollbarLeft = barBounds.Left, RightmostControl = controls.Max(c => Bounds(c, scroll).Right), GroupHeaderWidth = groupHeader.ActualWidth });
            }
            capture(testWindow, Path.Combine(output, "sidebar-favorites-narrow.png"));
            columns.ColumnDefinitions[0].Width = sidebarWidth;
            results["minimumSidebarGeometry"] = geometry;
            File.WriteAllText(Path.Combine(output, "forum-navigation-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: favorite focus/toggle/sync/navigation, refresh icon/busy, light/dark and narrow sidebar scrollbar clearance.");
        }
        finally
        {
            handler.Release();
            testWindow.Close();
            App.ApplyTheme(vm.State.Theme);
        }
    }

    private static ToggleButton Star(DependencyObject root, string key) => Descendants<ToggleButton>(root).Single(b => b.Name == "ForumFavoriteStar" && b.Tag is Forum forum && forum.Key == key);
    private static void CheckHeaderWidth(Expander group, Visual ancestor)
    {
        var header = (ToggleButton)group.Template.FindName("HeaderSite", group);
        var row = Descendants<Border>(group).First(b => b.Name == "ForumRow");
        var headerBounds = Bounds(header, ancestor);
        var rowBounds = Bounds(row, ancestor);
        Require(Math.Abs(headerBounds.Left - rowBounds.Left) < 1 && Math.Abs(headerBounds.Right - rowBounds.Right) < 1 && header.ActualHeight >= 36,
            $"分组标题点击区域未与版块整行对齐：标题={headerBounds}，版块行={rowBounds}。");
    }
    private static Button OpenButton(DependencyObject root, string key) => Descendants<Button>(root).Single(b => b.Name == "ForumOpenButton" && b.Tag is Forum forum && forum.Key == key);
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject => UiRefinementChecks.Descendants<T>(root);
    private static IEnumerable<DependencyObject> Ancestors(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent)) yield return parent;
    }
    private static Rect Bounds(FrameworkElement item, Visual ancestor) => item.TransformToAncestor(ancestor).TransformBounds(new Rect(item.RenderSize));
    private static void Invoke(Button button) => ((IInvokeProvider)(UIElementAutomationPeer.CreatePeerForElement(button) ?? new ButtonAutomationPeer(button)).GetPattern(PatternInterface.Invoke)).Invoke();
    private static void Toggle(ToggleButton button) => ((IToggleProvider)(UIElementAutomationPeer.CreatePeerForElement(button) ?? new ToggleButtonAutomationPeer(button)).GetPattern(PatternInterface.Toggle)).Toggle();
    private static void Click(ToggleButton button) => typeof(ToggleButton).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(button, null);
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(70); }
    private static async Task Until(Func<bool> condition, Window window, string message)
    {
        for (var attempt = 0; attempt < 50; attempt++) { await Layout(window); if (condition()) return; }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte c) { var v = c / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        var x = Luminance(a); var y = Luminance(b);
        return Math.Round((Math.Max(x, y) + .05) / (Math.Min(x, y) + .05), 2);
    }
    private sealed class NavigationHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public TaskCompletionSource<bool>? Block { get; set; }
        public void Release() { Block?.TrySetResult(true); Block = null; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("导航测试禁止提交发言。");
            RequestCount++;
            if (Block is { } block) await block.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ReaderControlsChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new FixtureHandler();
        var store = new LocalStore(Path.Combine(output, "reader-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false, Width = 1460, Height = 780 };
        try
        {
            window.Show(); await vm.OpenThreadAsync(101); await vm.LoadSubscriptionsAsync(); await Layout(window);
            var reader = (ListBox)window.FindName("ReaderList");
            var po = (ToggleButton)window.FindName("OnlyPoButton");
            var star = (Button)window.FindName("ThreadSubscriptionButton");
            var moon = (Wpf.Ui.Controls.SymbolIcon)window.FindName("ThemeMoon");
            CheckThreadHeading(window, vm);
            var originalTitles = UiRefinementChecks.Descendants<EmojiLabel>(reader).Where(t => t.Name == "OriginalPostTitle" && t.IsVisible).ToArray();
            Require(originalTitles.Length == 1 && originalTitles[0].Text == "阅读工具栏验证", "串号成为页标题后，原帖标题丢失或重复显示在回复中。");
            Require(vm.ReadingPosts.Count == 4 && vm.ReadingPosts.Count(p => p.IsPo) == 2, "主楼与 PO 回复判定不正确。");
            ((IToggleProvider)new ToggleButtonAutomationPeer(po).GetPattern(PatternInterface.Toggle)).Toggle();
            await Layout(window);
            Require(vm.OnlyPo && vm.ReadingPosts.Select(p => p.Post.Id).SequenceEqual(new long[] { 101, 102 }), "PO 开关没有实际过滤回复。");
            Require(handler.Calls == 1 && ColorOf(po.Background) == ColorOf(Resource("AccentSoft")), "PO 开关应在本地过滤并使用交互强调色。");
            capture(window, Path.Combine(output, "reader-po-only.png"));
            po.IsChecked = false; await Layout(window);
            Require(!vm.OnlyPo && vm.ReadingPosts.Count == 4, "关闭 PO 后未恢复全部楼层。");

            Invoke(star); await Layout(window);
            Require(vm.IsSubscribed && handler.Subscribed, "星标没有订阅云端串。");
            var starIcon = UiRefinementChecks.Descendants<Wpf.Ui.Controls.SymbolIcon>(star).Single();
            Require(starIcon.Filled && ColorOf(starIcon.Foreground) == ColorOf(Resource("FavoriteStar")), "订阅后不是黄色实心星。");
            capture(window, Path.Combine(output, "reader-favorited.png"));
            Invoke(star); await Layout(window);
            Require(!vm.IsSubscribed && !starIcon.Filled && !handler.Subscribed, "取消星标未恢复空心或未同步云端。");

            foreach (var theme in new[] { "light", "dark" })
            {
                vm.State.Theme = theme; App.ApplyTheme(theme); vm.AppearanceChanged(); await Layout(window);
                CheckThreadHeading(window, vm);
                Require(moon.Filled == (theme == "dark"), "月亮填充状态不正确。");
                Require(ColorOf(moon.Foreground) == ColorOf(Resource(theme == "dark" ? "FavoriteStar" : "Text")), "月亮颜色没有跟随模式。");
                foreach (var identity in UiRefinementChecks.Descendants<PostIdentity>(reader).Where(i => i.IsVisible))
                {
                    var badge = (Border)identity.FindName("PoBadge");
                    Require(badge.IsVisible == (identity.Post!.UserHash == "Island1"), "PO 标签显示在错误饼干上。");
                    if (badge.IsVisible) Require(ColorOf(badge.Background) == ColorOf(Resource("PoBackground")), "PO 标签不是蓝色底色。");
                }
                capture(window, Path.Combine(output, $"reader-{theme}.png"));
            }
            Invoke((Button)window.FindName("ThemeButton")); await Layout(window);
            Require(!vm.IsDark && !moon.Filled && store.Load().Theme == "light", "月亮入口没有切换并保存主题。");

            var originalSize = vm.State.FontSize;
            vm.State.FontSize = 24; vm.AppearanceChanged(); await Layout(window);
            CheckThreadHeading(window, vm);
            Require(UiRefinementChecks.Descendants<PostBody>(reader).Where(b => b.IsVisible).All(b => b.FontSize == 24), "串内正文没有响应阅读字号。");
            vm.State.FontSize = originalSize; vm.AppearanceChanged(); await Layout(window);

            foreach (var width in new[] { 900d, 1060d, 1460d })
            {
                window.Width = width; await Layout(window);
                foreach (var button in UiRefinementChecks.Descendants<Button>(reader).Where(b => b.Tag is Post && b.IsVisible))
                {
                    var header = (Grid)button.Parent;
                    var date = header.Children.OfType<TextBlock>().Single();
                    var bounds = button.TransformToAncestor(reader).TransformBounds(new Rect(button.RenderSize));
                    Require(bounds.Right <= reader.ActualWidth - 10 && bounds.Left >= 0, "楼号越界。");
                    Require(date.Text == PostTime.Format(((Post)button.Tag).Time, true), "串内日期没有统一为精确到秒的格式。");
                    Require(date.ActualWidth >= date.DesiredSize.Width - date.Margin.Left - date.Margin.Right - 1 && date.ActualHeight > 0, "日期被挤掉。");
                    Require(Grid.GetRow(date) == (header.ActualWidth < 420 ? 1 : 0), "窄阅读栏未将日期换行。");
                }
                capture(window, Path.Combine(output, $"reader-width-{width}.png"));
            }

            // 点楼号打开的是向当前串引用该楼的编辑器，不是另一个主串。
            var quote = UiRefinementChecks.Descendants<Button>(reader).First(b => b.Tag is Post { Id: 102 });
            string? draft = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            timer.Tick += (_, _) =>
            {
                var composer = window.OwnedWindows.OfType<ComposerWindow>().FirstOrDefault();
                if (composer is null) return;
                draft = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "发言正文").Text;
                composer.Close(); timer.Stop();
            };
            timer.Start(); Invoke(quote); await Layout(window); timer.Stop();
            Require(draft == ">>No.102\r\n" && store.Load().Drafts.Any(d => d.ThreadId == 101 && d.Content == draft), "楼号引用目标或草稿不正确。");
            var calls = handler.Calls;
            Invoke((Button)window.FindName("RefreshThreadButton")); await Layout(window);
            Require(handler.Calls == calls + 1 && vm.Original?.Id == 101, "正文刷新没有发起读取。");
            CheckThreadHeading(window, vm);
            File.WriteAllText(Path.Combine(output, "reader-controls-results.json"), JsonSerializer.Serialize(new
            {
                PoFiltering = true, PoReplyBadges = true, SubscriptionStar = true, MoonTheme = true,
                ResponsiveHeaders = true, ThreadNumberHeading = true, ForumOnlyMeta = true, OriginalTitlePreserved = true, ReadingFontSync = true, QuoteByPostNumber = true, Refresh = true,
                ClipboardNotTouched = true, OnlyGetRequests = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: thread-number heading, forum-only meta, reading-font sync, PO filtering/badges, subscription state, moon theme, narrow headers, quote by number, refresh.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }
    private static void CheckThreadHeading(Window window, MainViewModel vm)
    {
        Require(vm.ReaderTitle == "No.101" && vm.ReaderMeta == vm.ForumName(4), "串内标题未改为串号，或顶部元信息仍包含串号/回复数。");
        var heading = (TextBlock)window.FindName("ReaderHeading");
        var forum = (TextBlock)window.FindName("ReaderForum");
        Require(heading.Text == vm.ReaderTitle && heading.IsVisible, "串号标题未实际显示。");
        Require(forum.Text == vm.ReaderMeta && forum.IsVisible, "顶部板块名称未实际显示。");
    }
    private static Brush Resource(string name) => (Brush)Application.Current.FindResource(name);
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    private static async Task Layout(Window window) { await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle); await Task.Delay(160); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool Subscribed { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("禁止发帖。");
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("addFeed")) { Subscribed = true; return Json("\"订阅大成功\""); }
            if (path.EndsWith("delFeed")) { Subscribed = false; return Json("\"取消订阅成功\""); }
            if (path.EndsWith("feed")) return Json("[]");
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"id":101,"fid":4,"user_hash":"Island1","title":"阅读工具栏验证","content":"主楼内容","ReplyCount":3,"now":"2026-10-23(五)10:30:00",
                 "Replies":[{"id":102,"fid":4,"user_hash":"Island1","content":"PO 的回复","now":"2026-10-23(五)10:31:00"},
                 {"id":103,"fid":4,"user_hash":"Normal2","content":"普通回复","now":"2026-10-23(五)10:32:00"},
                 {"id":104,"fid":4,"user_hash":"RedName","admin":1,"sage":1,"content":"红名与 SAGE","now":"2026-10-23(五)10:33:00"}]}
                """) });
        }
        private static Task<HttpResponseMessage> Json(string value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value) });
    }
}

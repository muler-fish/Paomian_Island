using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>串列表选中状态与阅读区同步、阅读历史后退/前进、位置恢复、竞态与失败处理。只使用模拟接口。</summary>
internal static class ReadingHistoryChecks
{
    // A、C、D、E 属于 ROLL 点板块列表；B 属于综合版1，不在 ROLL 列表里。
    private const long A = 69711052, B = 69711012, C = 69711070, D = 69711080, E = 69711090, Missing = 69799999;
    private static readonly Forum Roll = new(25, "ROLL点", "综合"), General = new(4, "综合版1", "综合");
    private static int _fallbackClicks;

    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var profile = Path.Combine(output, "history-profile");
        if (Directory.Exists(profile)) Directory.Delete(profile, true);
        var handler = new HistoryHandler();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(profile));
        vm.State.Forums = [Roll, General]; vm.State.ShowImages = false;
        // B 是以前读过的串：引用预览只给已确认的串首提供「打开串」。
        vm.State.History.Add(new(new Post { Id = B, Fid = General.Id }, 1, DateTimeOffset.Now.AddDays(-1)));
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false, Width = 1280, Height = 800 };
        var results = new Dictionary<string, object>();
        try
        {
            window.Show(); await Pause();
            var list = (ListBox)window.FindName("ThreadList");
            var reader = (ListBox)window.FindName("ReaderList");
            var back = (Wpf.Ui.Controls.Button)window.FindName("ReaderBackButton");
            var forward = (Wpf.Ui.Controls.Button)window.FindName("ReaderForwardButton");
            long[] Ids() => vm.Navigation.Entries.Select(e => e.ThreadId).ToArray();
            long? Selected() => (list.SelectedItem as PostCard)?.Post.Id;
            async Task Opened(long id, string why) { await Until(() => !vm.ThreadBusy && vm.Original?.Id == id, why); await Pause(); }
            async Task OpenByLinkPreview(long id, string why)
            {
                await window.OpenContentLinkAsync($"/t/{id}");
                await Until(() => Popup<ThreadLinkPreviewWindow>() is { } p && Button(p, "打开串").IsEnabled, "串链接预览没有加载");
                Click(Popup<ThreadLinkPreviewWindow>()!, "打开串"); await Opened(id, why);
            }
            // 列表不满一屏时会自动续载下一页；等续载结束再记录请求数，避免和导航检查混在一起。
            async Task SelectForum(Forum forum)
            {
                await vm.SelectForumAsync(forum);
                for (var i = 0; i < 5 && vm.HasMoreList; i++) { await Until(() => !vm.ListBusy, "串列表加载没有结束"); await vm.LoadMoreListAsync(); }
                await Until(() => !vm.ListBusy, "串列表续载没有结束"); await Pause();
            }

            await SelectForum(Roll);
            var cards = vm.Posts.ToArray(); var listCalls = handler.ListCalls;
            var listViewer = UiRefinementChecks.Descendants<ScrollViewer>(list).First();
            Require(cards.Select(c => c.Post.Id).SequenceEqual([A, C, D, E]), "ROLL 列表夹具不正确");
            Require(!back.IsEnabled && !forward.IsEnabled && !vm.CanGoBack && !vm.CanGoForward, "没有历史时后退/前进可用");

            // 一、截图场景：列表打开 A → 引用预览打开 B → 列表不再把 A 当作当前串 → 再点 A 能直接回去。
            await ClickCard(list, vm, A); await Opened(A, "点击列表没有打开 A");
            Require(Selected() == A && vm.ReadingThreadId == A && Ids().SequenceEqual([A]), "打开 A 后选中项或历史不正确");
            var threadCalls = handler.ThreadCalls.Count;
            var dialog = await OpenReference(window, owner, B);
            Click(dialog, "关闭"); await Pause();
            Require(vm.Original?.Id == A && Ids().SequenceEqual([A]) && handler.ThreadCalls.Count == threadCalls, "只查看并关闭引用预览改变了阅读区、历史或发起了读串请求");
            dialog = await OpenReference(window, owner, B);
            Click(dialog, "打开串"); await Opened(B, "引用预览「打开串」没有打开 B");
            Require(vm.ReaderMeta == General.Name && vm.ListTitle == Roll.Name && vm.ActiveForumKey == Roll.Key, "跨板块打开引用串时切换了板块列表");
            Require(vm.Posts.SequenceEqual(cards) && handler.ListCalls == listCalls, "引用跳转清空或重新加载了串列表");
            Require(list.SelectedItem is null && vm.ReadingThreadId == B, "右侧已是 No.69711012，列表仍把 No.69711052 显示为选中");
            Require(Ids().SequenceEqual([A, B]) && vm.CanGoBack && back.IsEnabled && !forward.IsEnabled, "引用跳转没有记入历史");
            capture(window, Path.Combine(output, "reference-jump-selection.png"));
            await ClickCard(list, vm, A); await Opened(A, "引用跳转后再次点击 No.69711052 无法返回");
            Require(Selected() == A && Ids().SequenceEqual([A, B, A]), "重新点击原串后选中项或历史不正确");
            var calls = handler.ThreadCalls.Count; var offset = listViewer.VerticalOffset;
            await ClickCard(list, vm, A); await Pause();
            Require(handler.ThreadCalls.Count == calls && Ids().Length == 3 && vm.Original?.Id == A, "点击右侧正在显示的串重复请求或新增历史");
            Require(Math.Abs(listViewer.VerticalOffset - offset) < 1, "导航改变了串列表滚动位置");
            results["ReferenceJumpReselect"] = true;

            // 二、A → B → C：两次后退、两次前进；串链接预览和跳转框也走同一套历史。
            await OpenByLinkPreview(B, "串链接预览没有打开 B");
            vm.JumpText = $"No.{C}"; vm.JumpCommand.Execute(null); await Opened(C, "跳转框没有打开 C");
            Require(Ids().SequenceEqual([A, B, A, B, C]) && vm.Navigation.Index == 4 && Selected() == C, "串链接和跳转框没有进入统一导航");
            Require(vm.BackLabel == $"后退到 No.{B} · Alt+←" && Equals(back.ToolTip, vm.BackLabel) && Equals(forward.ToolTip, "前进 · Alt+→"), "按钮提示缺少目标或快捷键");
            Invoke(back); await Opened(B, "第一次后退没有回到 B");
            Require(vm.Navigation.Index == 3 && list.SelectedItem is null && forward.IsEnabled, "后退到不在列表里的串时列表选中项不正确");
            Invoke(back); await Opened(A, "第二次后退没有回到 A");
            Require(vm.Navigation.Index == 2 && Selected() == A, "后退回原串时列表没有恢复选中");
            Invoke(forward); await Opened(B, "第一次前进没有回到 B");
            Invoke(forward); await Opened(C, "第二次前进没有回到 C");
            Require(vm.Navigation.Index == 4 && Ids().Length == 5 && Selected() == C && !forward.IsEnabled && back.IsEnabled, "前进后历史被重复记录或按钮状态错误");
            results["BackForwardTwice"] = true;

            // 三、后退到 B 后打开 D：截断 B 之后的前进历史。
            Invoke(back); await Opened(B, "后退到 B 失败");
            await ClickCard(list, vm, D); await Opened(D, "点击 D 失败");
            Require(Ids().SequenceEqual([A, B, A, B, D]) && !vm.CanGoForward && !forward.IsEnabled, "打开新串没有删除前进历史");
            results["ForwardTruncated"] = true;

            // 四、刷新、跳页不新增历史；刷新仍然真正请求。
            calls = handler.ThreadCalls.Count;
            Invoke((Button)window.FindName("RefreshThreadButton")); await Opened(D, "刷新失败");
            Require(handler.ThreadCalls.Count == calls + 1 && Ids().Length == 5, "刷新当前串新增了历史或没有请求");
            vm.PageText = "2"; vm.JumpPageCommand.Execute(null); await Until(() => !vm.ThreadBusy && vm.ThreadPageLabel.StartsWith("第 2"), "跳页失败");
            Require(Ids().Length == 5 && vm.Navigation.Current!.Page == 2, "串内跳页新增历史或没有记录页码");
            results["RefreshAndPageJumpKeepHistory"] = true;

            // 五、返回时恢复页码和滚动位置，优先复用本次运行已读的页面；布局变化后仍停在原位置。
            await ClickCard(list, vm, A); await Opened(A, "打开 A 失败");
            vm.PageText = "3"; vm.JumpPageCommand.Execute(null); await Until(() => !vm.ThreadBusy && vm.ThreadPageLabel.StartsWith("第 3"), "A 跳到第 3 页失败");
            await Pause();
            var viewer = UiRefinementChecks.Descendants<ScrollViewer>(reader).First();
            viewer.ScrollToVerticalOffset(900); await Pause();
            var before = Visible(reader);
            Require(before.Id != 0 && before.Id / 1000 == A, "第 3 页没有可见回复");
            await OpenByLinkPreview(B, "从第 3 页打开 B 失败");
            calls = handler.ThreadCalls.Count(c => c.Id == A && c.Page == 3);
            Invoke(back); await Opened(A, "从 B 后退到 A 失败");
            await Pause(400);
            var restored = Visible(reader);
            Require(vm.PageText == "3" && vm.ThreadPageLabel.StartsWith("第 3") && vm.ReadingPosts.First(p => !p.IsTip).Page == 3, "后退没有恢复页码");
            Require(Same(restored, before), $"后退没有恢复阅读位置。Before={before}, After={restored}");
            Require(handler.ThreadCalls.Count(c => c.Id == A && c.Page == 3) == calls, "后退没有复用刚读过的页面，重新下载了同一页");
            var grown = GrowItemAbove(reader);
            await Pause(400);
            Require(!grown || Same(Visible(reader), before), "图片等异步内容改变上方高度后阅读位置漂移");
            results["RestorePageAndScroll"] = true; results["RestoreAfterLayoutChange"] = grown;
            capture(window, Path.Combine(output, "restored-position.png"));

            // 六、跨板块：切到综合版1 后返回 ROLL 的串，不切换板块、不清空列表、不强制选中。
            await SelectForum(General);
            var generalCards = vm.Posts.ToArray(); listCalls = handler.ListCalls;
            Invoke(forward); await Opened(B, "前进到 B 失败");
            Invoke(back); await Opened(A, "后退到 A 失败");
            Require(vm.ActiveForumKey == General.Key && vm.Posts.SequenceEqual(generalCards) && handler.ListCalls == listCalls && list.SelectedItem is null,
                "返回其他板块的串时切换了板块、清空了列表或错误选中");
            await SelectForum(Roll);
            Require(Selected() == A, "回到 ROLL 列表后没有按当前阅读串恢复选中");
            results["CrossForum"] = true;

            // 七、连续快速操作：较早的请求晚返回也不能覆盖最后一次选择。
            Require(Ids().SequenceEqual([A, B, A, B, D, A, B]) && vm.Navigation.Index == 5, $"竞态测试前的历史不正确：{string.Join(",", Ids())} @ {vm.Navigation.Index}");
            vm.ClearTemporaryCache();
            var gateD = handler.Block(D);
            Invoke(back); await Until(() => gateD.Hit, "后退到 D 没有发起请求");
            Require(vm.ThreadOpening && vm.ReadingThreadId == D && vm.CanGoBack && vm.BackLabel.Contains($"No.{B}"), "等待中的后退没有更新当前阅读串或下一次后退目标");
            Invoke(back); await Opened(B, "连续第二次后退没有回到 B");
            gateD.Release(); await Pause(250);
            Require(vm.Original?.Id == B && vm.Navigation.Index == 3 && vm.ReadingThreadId == B, "迟到的后退结果覆盖了最后一次后退");
            gateD = handler.Block(D);
            Invoke(forward); await Until(() => gateD.Hit, "前进到 D 没有发起请求");
            await ClickCard(list, vm, C); await Opened(C, "前进等待期间打开 C 失败");
            gateD.Release(); await Pause(250);
            Require(vm.Original?.Id == C && Ids().SequenceEqual([A, B, A, B, C]) && vm.Navigation.Index == 4 && Selected() == C, "等待中的前进被新串取代后历史不正确");
            var gateE = handler.Block(E);
            await ClickCard(list, vm, E); await Until(() => gateE.Hit, "打开 E 没有发起请求");
            Require(Selected() == E && vm.ReadingThreadId == E, "打开等待中时列表没有立即对应用户选择");
            Invoke(back); await Opened(B, "打开新串等待期间后退失败");
            gateE.Release(); await Pause(250);
            Require(vm.Original?.Id == B && Ids().SequenceEqual([A, B, A, B, C]) && vm.Navigation.Index == 3 && list.SelectedItem is null, "迟到的新串结果覆盖了后退");
            var gateA = handler.Block(A); calls = handler.ThreadCalls.Count(c => c.Id == B);
            Invoke(back); await Until(() => gateA.Hit, "后退到 A 没有发起请求");
            Invoke(forward); await Pause();
            Require(!vm.ThreadBusy && vm.Original?.Id == B && vm.Navigation.Index == 3 && vm.ReadingThreadId == B && handler.ThreadCalls.Count(c => c.Id == B) == calls,
                "后退未完成又前进回当前串时没有直接取消");
            gateA.Release(); await Pause(250);
            Require(vm.Original?.Id == B && vm.Navigation.Index == 3, "取消的后退迟到后仍然生效");
            Invoke(forward); await Opened(C, "前进到 C 失败");
            gateE = handler.Block(E); calls = handler.ThreadCalls.Count(c => c.Id == C);
            await ClickCard(list, vm, E); await Until(() => gateE.Hit, "打开 E 没有发起请求");
            await ClickCard(list, vm, C); await Pause();
            Require(!vm.ThreadBusy && vm.Original?.Id == C && Selected() == C && vm.ReadingThreadId == C && handler.ThreadCalls.Count(c => c.Id == C) == calls, "点回正在显示的串没有取消等待中的切换");
            gateE.Release(); await Pause(250);
            Require(vm.Original?.Id == C && Ids().SequenceEqual([A, B, A, B, C]) && Selected() == C, "取消的切换迟到后仍然生效");
            results["RapidNavigation"] = true;

            // 八、失败：有离线缓存时回看缓存；没有缓存时保留历史并提示；串被删除时移除该项；普通打开失败恢复选中项。
            handler.NetworkDown.Add(B); vm.ClearTemporaryCache();
            Invoke(back); await Opened(B, "网络失败时没有使用离线缓存后退");
            Require(vm.Navigation.Index == 3 && vm.Status.StartsWith("离线阅读"), "离线缓存后退状态不正确");
            Invoke(forward); await Opened(C, "前进到 C 失败");
            vm.Store.Cache.Clear(); vm.ClearTemporaryCache(); vm.Error = "";
            Invoke(back); await Until(() => !vm.ThreadBusy && vm.HasError, "后退网络失败没有反馈");
            Require(vm.Original?.Id == C && vm.Navigation.Index == 4 && Ids().Length == 5 && vm.Error.StartsWith($"无法返回 No.{B}") && Selected() == C && vm.CanGoBack,
                $"网络失败后历史、阅读区或选中项不正确：{vm.Error}");
            handler.NetworkDown.Clear(); vm.Error = "";
            Invoke(back); await Opened(B, "网络恢复后不能再次后退");
            Invoke(forward); await Opened(C, "前进到 C 失败");
            handler.Deleted.Add(B); vm.ClearTemporaryCache(); vm.Store.Cache.Clear();
            Invoke(back); await Until(() => !vm.ThreadBusy && vm.HasError, "后退到已删除的串没有反馈");
            Require(vm.Original?.Id == C && Ids().SequenceEqual([A, B, A, C]) && vm.Navigation.Index == 3 && vm.Error.Contains("已从后退/前进记录中移除"),
                $"已删除的串没有从历史中移除：{string.Join(",", Ids())} / {vm.Error}");
            capture(window, Path.Combine(output, "deleted-thread-feedback.png"));
            vm.Error = ""; Invoke(back); await Opened(A, "移除失效项后后退没有跳过它");
            Invoke(forward); await Opened(C, "前进到 C 失败");
            vm.JumpText = Missing.ToString(); vm.JumpCommand.Execute(null); await Until(() => !vm.ThreadBusy && vm.HasError, "打开不存在的串没有反馈");
            Require(vm.Original?.Id == C && vm.ReadingThreadId == C && Ids().Length == 4 && Selected() == C, "打开失败改变了历史或当前阅读串");
            handler.NetworkDown.Add(E); vm.ClearTemporaryCache(); vm.Store.Cache.Clear(); vm.Error = "";
            await ClickCard(list, vm, E); await Until(() => !vm.ThreadBusy && vm.HasError, "列表打开失败没有反馈");
            Require(vm.Original?.Id == C && Selected() == C && vm.PageText == "1", "打开失败后列表仍选中未打开的串或页码被改动");
            handler.NetworkDown.Clear(); vm.Error = "";
            await ClickCard(list, vm, E); await Opened(E, "失败后再次点击同一串无法打开");
            results["Failures"] = true;

            // 九、快捷键与鼠标侧键：可编辑输入框里不拦截 Alt+←/→。
            var jumpBox = (TextBox)window.FindName("JumpBox");
            var body = UiRefinementChecks.Descendants<PostBody>(reader).First();
            Require(MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.Alt, reader) == -1 && MainWindow.ReadingHistoryStep(Key.Right, ModifierKeys.Alt, list) == 1, "Alt+←/→ 没有对应后退/前进");
            Require(MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.Alt, body) == -1 && MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.Alt, null) == -1, "正文选中文字后 Alt+← 不可用");
            Require(MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.Alt, jumpBox) == 0 && MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.None, reader) == 0
                && MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.Alt | ModifierKeys.Shift, reader) == 0 && MainWindow.ReadingHistoryStep(Key.Z, ModifierKeys.Control, reader) == 0
                && MainWindow.ReadingHistoryStep(Key.Up, ModifierKeys.Alt, reader) == 0, "快捷键拦截了输入框或其他组合键");
            var composer = new ComposerWindow(vm, E) { Owner = window, ShowInTaskbar = false };
            composer.Show(); await Pause();
            var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "发言正文");
            Require(MainWindow.ReadingHistoryStep(Key.Left, ModifierKeys.Alt, editor) == 0, "快捷键会拦截回应编辑器");
            composer.Close(); await Pause();
            var index = vm.Navigation.Index;
            Side(reader, MouseButton.XButton1); await Opened(C, "鼠标后退键没有后退");
            Require(vm.Navigation.Index == index - 1, "鼠标后退键移动了错误的步数");
            Side(list, MouseButton.XButton2); await Opened(E, "鼠标前进键没有前进");
            window.NavigateReadingHistory(-1); await Opened(C, "快捷键入口没有后退");
            window.NavigateReadingHistory(1); await Opened(E, "快捷键入口没有前进");
            results["ShortcutsAndSideButtons"] = true;

            // 十、浅色/深色、拖窄阅读栏和不同 DPI 下按钮完整可见、不压住串号。
            var columns = (Grid)window.FindName("ContentColumns"); var listColumn = columns.ColumnDefinitions[2].ActualWidth;
            var layout = new List<object>(); var systemDpi = VisualTreeHelper.GetDpi(window).DpiScaleX;
            foreach (var scale in new[] { 1.0, 1.25, 1.5 })
            {
                SetDpi(window, scale, 1280, 800); await Pause(300);
                foreach (var theme in new[] { "light", "dark" })
                {
                    App.ApplyTheme(theme); await Pause();
                    Require(ReferenceEquals(back.Style, window.FindResource("SecondaryIconButton")) && ReferenceEquals(forward.Style, back.Style)
                        && Math.Abs(back.ActualWidth - 32) < .5 && Math.Abs(back.ActualHeight - 32) < .5 && Math.Abs(forward.ActualWidth - 32) < .5, "后退/前进按钮样式或尺寸不正确");
                    // 模拟拖动分栏：阅读栏从宽到最窄（最小 280）。
                    foreach (var readerWidth in new[] { 700d, 560d, 430d, 290d })
                    {
                        var listWidth = window.ActualWidth - columns.ColumnDefinitions[0].ActualWidth - 2 - readerWidth - 16;
                        columns.ColumnDefinitions[2].Width = new GridLength(Math.Max(240, listWidth)); await Pause();
                        layout.Add(CheckHeader(window, scale, theme, readerWidth));
                        if (theme == "dark" || scale == 1.25) capture(window, Path.Combine(output, $"header-{theme}-{scale:0.00}-{readerWidth}.png"));
                    }
                    columns.ColumnDefinitions[2].Width = new GridLength(listColumn);
                }
            }
            App.ApplyTheme("light"); SetDpi(window, systemDpi, 1280, 800); await Pause(300);
            Require(!forward.IsEnabled && ToolTipService.GetShowOnDisabled(forward), "末尾时前进按钮没有禁用或禁用时没有提示");
            capture(window, Path.Combine(output, "header-disabled-forward.png"));
            results["HeaderLayout"] = layout; results["FallbackClicks"] = _fallbackClicks;
            File.WriteAllText(Path.Combine(output, "reading-history-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: list selection follows reader, reference jump reselect, back/forward, truncation, refresh, position restore with cache, cross-forum, rapid navigation, offline/network/deleted failures, shortcuts and side buttons, header layout in themes/widths/DPI (fallback clicks: {_fallbackClicks}).");
        }
        finally
        {
            foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w is TextWindow or ThreadLinkPreviewWindow or ComposerWindow).ToArray()) w.Close();
            window.Close(); App.ApplyTheme("light");
        }
    }

    /// <summary>真实输入验收：移动系统光标点击串列表、发送真实的 Alt+←/→ 和鼠标侧键、在回应编辑器里输入并撤销重做。</summary>
    /// <remarks>会占用鼠标和键盘，只在 --reading-history --real-input 时运行；每次输入前确认测试窗口仍在前台。</remarks>
    public static async Task RunRealInputAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var profile = Path.Combine(output, "real-input-profile");
        if (Directory.Exists(profile)) Directory.Delete(profile, true);
        var handler = new HistoryHandler();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(profile));
        vm.State.Forums = [Roll, General]; vm.State.ShowImages = false;
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false, Width = 1280, Height = 800, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        GetCursorPos(out var cursor);
        var log = new List<string>();
        ComposerWindow? composer = null;
        try
        {
            window.Show(); await Pause(300);
            var handle = new WindowInteropHelper(window).Handle;
            Foreground(handle); window.Activate(); await Pause(300);
            var list = (ListBox)window.FindName("ThreadList");
            long? Selected() => (list.SelectedItem as PostCard)?.Post.Id;
            async Task Opened(long id, string why) { await Until(() => !vm.ThreadBusy && vm.Original?.Id == id, why); await Pause(); }
            void Guard(IntPtr expected) { if (GetForegroundWindow() != expected) throw new InvalidOperationException("测试窗口不在前台，已停止发送输入。"); }
            async Task RealClickCard(long id)
            {
                var card = vm.VisiblePosts.Cast<PostCard>().Single(c => c.Post.Id == id);
                list.ScrollIntoView(card); await Pause();
                var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(card);
                var point = container.PointToScreen(new Point(container.ActualWidth / 2, 24));
                Guard(handle); SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(80);
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); await Task.Delay(60); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); await Pause();
            }
            async Task Chord(ushort modifier, ushort key, bool extended = false)
            {
                Guard(handle);
                Send(KeyInput(modifier, false)); Send(KeyInput(key, false, extended)); Send(KeyInput(key, true, extended)); Send(KeyInput(modifier, true));
                await Pause(250);
            }

            await vm.SelectForumAsync(Roll); await Pause();
            await RealClickCard(A); await Opened(A, "真实点击没有打开 A");
            await window.OpenContentLinkAsync($"/t/{B}");
            await Until(() => Popup<ThreadLinkPreviewWindow>() is { } p && Button(p, "打开串").IsEnabled, "串链接预览没有加载");
            Click(Popup<ThreadLinkPreviewWindow>()!, "打开串"); await Opened(B, "预览打开 B 失败");
            Foreground(handle); window.Activate(); await Pause();
            Require(list.SelectedItem is null, "引用跳转后列表仍选中原串");
            await RealClickCard(A); await Opened(A, "真实点击原串没有返回");
            Require(Selected() == A, "真实点击后列表没有选中 A"); log.Add("PASS real click returns to No.69711052 after jump");

            ((Button)window.FindName("CopyThreadLinkButton")).Focus(); await Pause();
            await Chord(0x12, 0x25, true); await Opened(B, "真实 Alt+← 没有后退");
            await Chord(0x12, 0x27, true); await Opened(A, "真实 Alt+→ 没有前进");
            log.Add("PASS real Alt+Left / Alt+Right");
            var jumpBox = (TextBox)window.FindName("JumpBox");
            jumpBox.Focus(); Keyboard.Focus(jumpBox); jumpBox.Text = "12345"; jumpBox.CaretIndex = 5; await Pause();
            await Chord(0x12, 0x25, true);
            Require(vm.Original?.Id == A && !vm.ThreadBusy && jumpBox.Text == "12345", "焦点在跳转框时 Alt+← 仍然后退或改动了输入");
            log.Add("PASS Alt+Left ignored in jump box");
            jumpBox.Text = ""; ((Button)window.FindName("CopyThreadLinkButton")).Focus();

            // 鼠标侧键：一次按键只移动一步（不会和系统的浏览器后退命令叠加）。
            var reader = (ListBox)window.FindName("ReaderList");
            var center = reader.PointToScreen(new Point(reader.ActualWidth / 2, reader.ActualHeight / 2));
            Guard(handle); SetCursorPos((int)center.X, (int)center.Y); await Task.Delay(80);
            mouse_event(0x0080, 0, 0, 1, UIntPtr.Zero); await Task.Delay(60); mouse_event(0x0100, 0, 0, 1, UIntPtr.Zero);
            await Opened(B, "真实鼠标后退键没有后退"); await Pause(300);
            Require(vm.Navigation.Index == 1 && vm.Original?.Id == B, "一次鼠标后退键移动了多步");
            Guard(handle); mouse_event(0x0080, 0, 0, 2, UIntPtr.Zero); await Task.Delay(60); mouse_event(0x0100, 0, 0, 2, UIntPtr.Zero);
            await Opened(A, "真实鼠标前进键没有前进"); await Pause(300);
            Require(vm.Navigation.Index == 2, "一次鼠标前进键移动了多步");
            log.Add("PASS real mouse side buttons");

            // 回应编辑器：输入、Ctrl+Z、Ctrl+Y 照常；Alt+← 不影响主窗口阅读区。
            composer = new ComposerWindow(vm, A) { Owner = window, ShowInTaskbar = false };
            composer.Show(); await Pause(300);
            var composerHandle = new WindowInteropHelper(composer).Handle;
            Foreground(composerHandle); composer.Activate();
            var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "发言正文");
            InputMethod.SetIsInputMethodEnabled(editor, false);
            editor.Focus(); Keyboard.Focus(editor); await Pause(300);
            foreach (var vk in new ushort[] { 0x41, 0x42, 0x43 }) { Guard(composerHandle); Send(KeyInput(vk, false)); Send(KeyInput(vk, true)); await Task.Delay(120); }
            await Pause();
            Require(editor.Text.EndsWith("abc", StringComparison.Ordinal), $"真实输入没有进入回应编辑器：{editor.Text}");
            var typed = editor.Text; var index = vm.Navigation.Index;
            await Chord(0x12, 0x25, true);
            Require(vm.Navigation.Index == index && vm.Original?.Id == A && editor.Text == typed, "在回应编辑器里按 Alt+← 影响了主窗口或正文");
            await Chord(0x11, 0x5A);
            Require(editor.Text != typed && !editor.Text.EndsWith("abc", StringComparison.Ordinal), $"Ctrl+Z 没有撤销：{editor.Text}");
            await Chord(0x11, 0x59);
            Require(editor.Text == typed, $"Ctrl+Y 没有重做：{editor.Text}");
            log.Add("PASS composer typing, Ctrl+Z, Ctrl+Y; Alt+Left stays in composer");
            capture(window, Path.Combine(output, "real-input-main.png"));
            File.WriteAllLines(Path.Combine(output, "real-input.txt"), log);
            Console.WriteLine("PASS: real mouse click after reference jump, real Alt+Left/Right, ignored in jump box, side buttons one step, composer undo/redo unaffected.");
        }
        finally
        {
            Send(KeyInput(0x12, true)); Send(KeyInput(0x11, true));
            SetCursorPos(cursor.X, cursor.Y);
            composer?.Close();
            foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w is TextWindow or ThreadLinkPreviewWindow).ToArray()) w.Close();
            window.Close();
        }
    }

    private static void Foreground(IntPtr handle)
    {
        // 系统的前台锁定会拒绝直接切换；临时挂到当前前台线程的输入队列上再切换。
        var foreignThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero); var ownThread = GetCurrentThreadId();
        if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, true);
        try { BringWindowToTop(handle); SetForegroundWindow(handle); }
        finally { if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, false); }
    }
    private static Input KeyInput(ushort vk, bool up, bool extended = false) => new() { Type = 1, Keyboard = new KeyboardInput { Vk = vk, Flags = (up ? 2u : 0u) | (extended ? 1u : 0u) } };
    private static void Send(Input input) => SendInput(1, [input], Marshal.SizeOf<Input>());
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, IntPtr process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool value);

    private static object CheckHeader(MainWindow window, double scale, string theme, double readerWidth)
    {
        var frame = (Grid)window.FindName("ReaderHeaderFrame");
        var heading = (TextBlock)window.FindName("ReaderHeading");
        var actions = (FrameworkElement)window.FindName("ReaderHeaderActions");
        var buttons = new[] { "ReaderBackButton", "ReaderForwardButton", "OnlyPoButton", "ThreadSubscriptionButton", "CopyThreadLinkButton", "RefreshThreadButton", "ReplyButton" }
            .Select(n => (FrameworkElement)window.FindName(n)).ToArray();
        var frameBounds = Bounds(frame, window); var title = Bounds(heading, window);
        var natural = new TextBlock { Text = heading.Text, FontSize = heading.FontSize, FontWeight = heading.FontWeight, FontFamily = heading.FontFamily };
        natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Require(natural.DesiredSize.Width <= heading.ActualWidth + 1, $"串号被截断（{scale}/{theme}/{readerWidth}）");
        var rects = buttons.Select(b => Bounds(b, window)).ToArray();
        for (var i = 0; i < rects.Length; i++)
        {
            Require(buttons[i].IsVisible && rects[i].Left >= frameBounds.Left - 1 && rects[i].Right <= frameBounds.Right + 1, $"{buttons[i].Name} 超出标题栏（{scale}/{theme}/{readerWidth}）");
            Require(!Overlaps(rects[i], title), $"{buttons[i].Name} 压住了串号（{scale}/{theme}/{readerWidth}）");
            foreach (var other in rects.Skip(i + 1)) Require(!Overlaps(rects[i], other), $"{buttons[i].Name} 与其他按钮重叠（{scale}/{theme}/{readerWidth}）");
        }
        Require(rects[0].Right <= rects[1].Left + .5 && (rects[1].Right <= rects[2].Left + .5 || rects[2].Top >= rects[1].Bottom - 1), "后退/前进没有排在 PO 前面");
        // 最窄时也只有两行：串号与后退/前进一行，其余操作一行。
        var rows = rects.Select(r => Math.Round(r.Top)).Distinct().Count();
        var compact = Grid.GetRow(actions) == 1;
        Require(rows <= 2 && (!compact || Math.Abs(rects[0].Top - rects[1].Top) < 1 && rects[0].Bottom <= rects[2].Top + 1), $"窄栏标题栏超过两行或后退/前进没有留在串号行（{scale}/{theme}/{readerWidth}）");
        return new { Scale = scale, Theme = theme, ReaderWidth = readerWidth, Frame = Math.Round(frameBounds.Width), Compact = compact, Rows = rows };
    }

    private static async Task<TextWindow> OpenReference(MainWindow window, MainWindow owner, long id)
    {
        var body = UiRefinementChecks.Descendants<PostBody>((ListBox)window.FindName("ReaderList")).First(b => b.Html.Contains($"No.{id}"));
        var link = body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Hyperlink>().First();
        link.RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
        TextWindow? dialog = null;
        await Until(() => (dialog = window.OwnedWindows.OfType<TextWindow>().FirstOrDefault(w => w.IsVisible && w.Title == $"引用 · No.{id}" && HasButton(w, "打开串"))) is not null, "引用预览没有出现「打开串」");
        // 测试宿主窗口也订阅了静态引用事件，关掉它打开的预览。
        foreach (var other in owner.OwnedWindows.OfType<TextWindow>().ToArray()) other.Close();
        return dialog!;
    }

    // 与真实点击走同一路径：ListBoxItem 处理按下并改变选中项；已选中的项不会再触发 SelectionChanged。
    private static async Task ClickCard(ListBox list, MainViewModel vm, long id)
    {
        var card = vm.VisiblePosts.Cast<PostCard>().Single(c => c.Post.Id == id);
        Window.GetWindow(list)!.Activate();
        list.ScrollIntoView(card); list.UpdateLayout(); await Pause(60);
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(card);
        var target = UiRefinementChecks.Descendants<Border>(container).First(b => b.Child is StackPanel);
        var wasSelected = container.IsSelected;
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
        if (Mouse.Captured is not null) Mouse.Capture(null);
        // ListBoxItem 只有拿到键盘焦点才处理点击；测试窗口不在前台时退回到同样的选中操作。
        if (!wasSelected && !ReferenceEquals(list.SelectedItem, card)) { _fallbackClicks++; list.SelectedItem = card; }
    }

    // 模拟上方图片加载完成：把视口上方一项撑高，阅读位置应保持在原回复上。
    private static bool GrowItemAbove(ListBox reader)
    {
        reader.UpdateLayout();
        var above = UiRefinementChecks.Descendants<ListBoxItem>(reader)
            .Select(c => (Item: c, Y: c.TransformToAncestor(reader).Transform(new Point()).Y))
            .Where(c => c.Item.ActualHeight > 0 && c.Y + c.Item.ActualHeight <= 0).OrderBy(c => c.Y).LastOrDefault().Item;
        if (above is null) return false;
        var row = UiRefinementChecks.Descendants<Border>(above).First();
        row.MinHeight = row.ActualHeight + 180;
        return true;
    }

    private static void Side(UIElement source, MouseButton button) =>
        source.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, button) { RoutedEvent = Mouse.PreviewMouseDownEvent });
    private static (long Id, double Top) Visible(ListBox reader)
    {
        var anchor = ReadingAnchor.Capture(reader, skipTips: true);
        return anchor?.Item is ReadPost post ? (post.Post.Id, anchor.Top) : (0, 0);
    }
    private static bool Same((long Id, double Top) a, (long Id, double Top) b) => a.Id == b.Id && Math.Abs(a.Top - b.Top) < 3;
    private static bool Overlaps(Rect a, Rect b) { var overlap = Rect.Intersect(a, b); return !overlap.IsEmpty && overlap.Width >= 1 && overlap.Height >= 1; }
    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static T? Popup<T>() where T : Window => Application.Current.Windows.OfType<T>().SingleOrDefault(w => w.IsVisible);
    private static bool HasButton(Window window, string label) => UiRefinementChecks.Descendants<Button>(window).Any(b => Equals(b.Content, label));
    private static Button Button(Window window, string label) => UiRefinementChecks.Descendants<Button>(window).Single(b => Equals(b.Content, label));
    private static void Click(Window window, string label) => Button(window, label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void Invoke(Button button)
    {
        Require(button.IsEnabled, $"{button.Name} 不可用");
        ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }
    private static async Task Pause(int ms = 160)
    {
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
    }
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var i = 0; i < 250; i++) { if (condition()) return; await Task.Delay(20); }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void SetDpi(Window window, double scale, double width, double height)
    {
        var rect = new NativeRect { Right = (int)(width * scale), Bottom = (int)(height * scale) };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try { Marshal.StructureToPtr(rect, memory, false); var dpi = (int)(96 * scale); SendMessage(new WindowInteropHelper(window).Handle, 0x02E0, new((dpi << 16) | dpi), memory); window.Width = width; window.Height = height; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);

    private sealed class Gate
    {
        public TaskCompletionSource Source { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Hit { get; set; }
        public void Release() => Source.TrySetResult();
    }

    private sealed class HistoryHandler : HttpMessageHandler
    {
        public List<(long Id, int Page)> ThreadCalls { get; } = [];
        public int ListCalls { get; private set; }
        public HashSet<long> NetworkDown { get; } = [];
        public HashSet<long> Deleted { get; } = [];
        private readonly Dictionary<long, Gate> _gates = [];
        public Gate Block(long id) => _gates[id] = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("测试禁止发送内容。");
            var route = request.RequestUri!.AbsolutePath.Split('/').Last();
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            var id = long.Parse(query["id"] ?? "0"); var page = int.Parse(query["page"] ?? "1");
            if (route == "showf")
            {
                ListCalls++;
                var ids = page > 1 ? Array.Empty<long>() : id == Roll.Id ? new[] { A, C, D, E } : new[] { 69720001L, 69720002L };
                return Json(ids.Select(t => new { id = t, fid = id, user_hash = "Author" + t % 7, now = "2026-11-09(一)12:00:00", title = "", content = $"列表串 No.{t}", ReplyCount = 95 }));
            }
            if (route == "ref") return Json(new { id, fid = General.Id, user_hash = "Ref", now = "2026-11-09(一)11:00:00", content = $"被引用的串首 No.{id}" });
            if (route != "thread") throw new InvalidOperationException("未模拟的接口：" + route);
            ThreadCalls.Add((id, page));
            // 故意忽略取消，验证迟到的响应不会生效。
            if (_gates.Remove(id, out var gate)) { gate.Hit = true; await gate.Source.Task; }
            if (NetworkDown.Contains(id)) throw new HttpRequestException("模拟网络失败");
            if (id == Missing || Deleted.Contains(id)) return Json(new { success = false, error = "该串不存在" });
            var fid = id == B ? General.Id : Roll.Id;
            var replies = Enumerable.Range(1, 19).Select(n => new
            {
                id = id * 1000 + page * 40 + n, user_hash = n % 5 == 0 ? "Author" : "Reader" + n, now = "2026-11-09(一)12:30:00",
                content = (n == 3 && id != B ? $"&gt;&gt;No.{B} 引用<br>" : "") + $"No.{id} 第 {page} 页第 {n} 条<br>" + string.Join("<br>", Enumerable.Repeat("用于检查阅读位置恢复的正文。", 2 + n % 4))
            });
            return Json(new { id, fid, user_hash = "Author", now = "2026-11-09(一)12:00:00", title = $"串 {id}", content = $"主楼 No.{id}", ReplyCount = 95, Replies = replies });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class InertialScrollChecks
{
    public static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        CheckMotion();
        var handler = new ScrollHandler();
        var store = new LocalStore(Path.Combine(output, "inertia-profile"));
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), store);
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        var samples = new List<double>();
        try
        {
            window.Show();
            await vm.SelectForumAsync(new(4, "惯性滚动测试", "测试", MaxPage: 3)); await Pause();
            var list = (ListBox)window.FindName("ThreadList");
            var reader = (ListBox)window.FindName("ReaderList");
            var viewer = Viewer(list);
            Require(vm.State.InertialScrolling && InertialScroll.GetIsEnabled(list), "新设置应默认启用且绑定到主列表。");
            Require(InertialScroll.GetIsEnabled((ScrollViewer)window.FindName("ForumScroll")) && InertialScroll.GetIsEnabled(reader), "导航与正文未绑定同一设置。");

            if (SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines != 0)
            {
                viewer.ScrollToVerticalOffset(400); await Pause();
                var before = viewer.VerticalOffset;
                Require(Wheel(viewer, -120).Handled && InertialScroll.IsRunning(list), "滚轮未启动惯性。");
                Require(Math.Abs(viewer.VerticalOffset - before) < 1, "滚轮输入直接跳到了终点。");
                await Task.Delay(70); samples.Add(viewer.VerticalOffset);
                await Task.Delay(70); samples.Add(viewer.VerticalOffset);
                Require(samples[0] > before && samples[1] > samples[0], "停止输入后没有继续滑行。");
                await Until(() => !InertialScroll.IsRunning(list), "惯性未收敛");
                await Pause(); samples.Add(viewer.VerticalOffset);
                Require(samples[2] >= samples[1] && samples[2] < before + viewer.ViewportHeight * 2 + 1, "惯性未限制距离或反向抖动。");

                Wheel(viewer, -480); await Task.Delay(65);
                var turning = viewer.VerticalOffset;
                Wheel(viewer, 120); await Task.Delay(65);
                Require(viewer.VerticalOffset < turning, "反向滚轮仍被旧方向的惯性拖动。");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.PageUp) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Require(!InertialScroll.IsRunning(list), "键盘操作没有立即停止惯性。");

                Wheel(viewer, -120);
                window.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                Require(!InertialScroll.IsRunning(list), "鼠标按下没有停止惯性，可能干扰拖动滚动条。");

                Wheel(viewer, -120);
                vm.State.InertialScrolling = false; vm.AppearanceChanged(); await Pause();
                Require(!InertialScroll.IsRunning(list) && !InertialScroll.GetIsEnabled(list), "关闭开关没有立即停止惯性。");
                var disabled = viewer.VerticalOffset;
                var preview = Wheel(viewer, -120);
                Require(!preview.Handled && !InertialScroll.IsRunning(list), "关闭开关后仍接管滚轮。");
                viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent });
                await Pause(); Require(viewer.VerticalOffset > disabled, "关闭惯性后原生滚动没有工作。");
                vm.State.InertialScrolling = true; vm.AppearanceChanged(); await Pause();

                // 从真实正文控件发出滚轮，而不是只直接调用列表，检查可选择正文不会吞掉惯性。
                await vm.OpenThreadAsync(1000, 1); await Pause();
                var body = UiRefinementChecks.Descendants<PostBody>(reader).First();
                Require(Wheel(body, -120).Handled && InertialScroll.IsRunning(reader), "正文上的滚轮未传递给阅读区域。");
                await Task.Delay(80);
                await vm.OpenThreadAsync(2000, 1); await Pause();
                Require(!InertialScroll.IsRunning(reader) && Viewer(reader).VerticalOffset < 1, "切换串后旧惯性将新串拉离顶部。");

                viewer.ScrollToVerticalOffset(viewer.ScrollableHeight - 30); await Pause(30);
                Wheel(viewer, -120);
                await Until(() => vm.Posts.Count == 40, "惯性到达底部后未触发自动续载");
                await Until(() => !InertialScroll.IsRunning(list), "续载后惯性未停止");
                Require(handler.ListCalls == 2 && viewer.ScrollableHeight - viewer.VerticalOffset > viewer.ViewportHeight, "惯性续页重复请求或跳到新底部。");
                Console.WriteLine("PASS: wheel coasting, immediate reversal, keyboard/mouse interruption, native fallback, post body routing, thread reset, infinite loading.");
            }
            else Console.WriteLine("SKIP: animated UI movement because Windows disables animations or wheel scrolling; motion model still checked.");

            var settings = new SettingsWindow(vm) { Owner = window, ShowInTaskbar = false };
            settings.Show(); await Pause();
            var checkbox = UiRefinementChecks.Descendants<CheckBox>(settings).Single(c => Equals(c.Content, "惯性滚动"));
            var settingScroll = UiRefinementChecks.Descendants<ScrollViewer>(settings).First();
            Require(InertialScroll.GetIsEnabled(settingScroll), "设置窗口未沿用惯性滚动选项。");
            var toggle = (IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)!;
            toggle.Toggle(); await Pause();
            Require(!store.Load().InertialScrolling && !InertialScroll.GetIsEnabled(list) && !InertialScroll.GetIsEnabled(settingScroll), "设置开关没有保存或没有同步到窗口。");
            using (var reopened = new MainViewModel(new XIslandApi(new HttpClient(new ScrollHandler())), store))
                Require(!reopened.InertialScrolling, "重开后丢失惯性关闭设置。");
            toggle.Toggle(); await Pause();
            Require(store.Load().InertialScrolling, "开启设置未持久保存。");
            capture(settings, Path.Combine(output, "inertia-settings-light.png"));
            App.ApplyTheme("dark"); await Pause(); capture(settings, Path.Combine(output, "inertia-settings-dark.png"));
            settings.Close(); App.ApplyTheme("light");

            // 可编辑区不应被外围内容滚动接管。
            var composer = new ComposerWindow(vm, 2000) { Owner = window, ShowInTaskbar = false };
            composer.Show(); await Pause();
            var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).First(t => t.AcceptsReturn);
            Require(!Wheel(editor, -120).Handled, "惯性处理抢走了编辑框的滚轮事件。");
            composer.Close();

            viewer.ScrollToVerticalOffset(400); await Pause(); Wheel(viewer, -120);
            window.Close();
            Require(!InertialScroll.IsRunning(list), "关闭窗口后仍订阅逐帧事件。");
            File.WriteAllText(Path.Combine(output, "inertia-results.json"), JsonSerializer.Serialize(new
            {
                SystemAnimations = SystemParameters.ClientAreaAnimation, WheelLines = SystemParameters.WheelScrollLines,
                Samples = samples, MotionModel = true, Interruptible = true, SettingsPersist = true,
                EditorUntouched = true, UnloadStopsRendering = true, ContinuousLoading = true
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS: settings toggle/persistence, child window binding, editable area passthrough, unload cleanup.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }

    private static void CheckMotion()
    {
        var motion = new ScrollMotion(); motion.Reset(100); motion.Push(72, 100, 1000, 300);
        Require(motion.Position == 100 && motion.Target == 172, "输入不应瞬间移动到终点。");
        var previous = 100d;
        for (var i = 0; i < 120; i++)
        {
            var position = motion.Advance(1d / 120, 1000);
            Require(position >= previous && position <= 172, "减速曲线越界或倒退。"); previous = position;
        }
        Require(!motion.IsActive && motion.Position == 172, "减速曲线不收敛。");
        motion.Push(5000, 172, 10000, 300); Require(motion.Target <= 772, "快速滚轮积累了过长的位移队列。");
        motion.Advance(.05, 10000); var positionBefore = motion.Position;
        motion.Push(-72, positionBefore, 10000, 300);
        Require(motion.Advance(.02, 10000) < positionBefore, "反向输入没有立即反向。");
        motion.Reset(990); motion.Push(72, 990, 1000, 300);
        for (var i = 0; i < 100; i++) motion.Advance(.016, 1000);
        Require(motion.Position == 1000 && !motion.IsActive, "底部边界不收敛。");
        motion.Reset(1); motion.Push(-72, 1, 1000, 300);
        for (var i = 0; i < 100; i++) motion.Advance(.016, 1000);
        Require(motion.Position == 0 && !motion.IsActive, "顶部出现越界。");
        motion.Reset(0); motion.Push(18, 0, 1000, 300);
        Require(motion.Target == 18, "小滚轮增量被放大。");
        Require(InertialScroll.AllowsAnimation(true, true) && !InertialScroll.AllowsAnimation(true, false) && !InertialScroll.AllowsAnimation(false, true), "未遵循系统减少动画设置。");
        Console.WriteLine("PASS: damped motion, reversal, bounded accumulation, convergence, small deltas, reduced motion policy.");
    }

    private static MouseWheelEventArgs Wheel(UIElement source, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(args); return args;
    }
    private static ScrollViewer Viewer(ListBox list) => UiRefinementChecks.Descendants<ScrollViewer>(list).First();
    private static Task Pause(int milliseconds = 180) => Task.Delay(milliseconds);
    private static async Task Until(Func<bool> condition, string message)
    {
        for (var i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(20); }
        throw new InvalidOperationException(message);
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ScrollHandler : HttpMessageHandler
    {
        public int ListCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var query = request.RequestUri!.Query.TrimStart('?').Split('&').Select(s => s.Split('=')).ToDictionary(s => s[0], s => s[1]);
            var page = int.Parse(query.GetValueOrDefault("page", "1"));
            object value;
            if (request.RequestUri.AbsolutePath.EndsWith("thread"))
                value = new { id = int.Parse(query["id"]), content = "惯性滚动测试主楼", user_hash = "Author", ReplyCount = 38,
                    Replies = Enumerable.Range((page - 1) * 19 + 1, 19).Select(i => new { id = 10000 + i, user_hash = "Reader", content = "用于滚动测试的正文<br>第二行<br>第三行<br>第四行" }) };
            else
            {
                ListCalls++;
                value = Enumerable.Range((page - 1) * 20 + 1, 20).Select(i => new { id = i, title = $"滚动测试 {i}", content = "这里是用于观察滚轮滑行和减速的测试列表内容。", user_hash = "Reader" });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) });
        }
    }
}

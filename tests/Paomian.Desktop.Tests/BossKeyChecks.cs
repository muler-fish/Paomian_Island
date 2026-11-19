using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class BossKeyChecks
{
    private static readonly ShortcutKey Standard = new(Key.H, ModifierKeys.Control | ModifierKeys.Alt);

    internal static async Task RunAsync(MainWindow main, string output, nint browser, nint explorer, Action<Window, string> capture)
    {
        CheckExternal(browser, "msedge");
        CheckExternal(explorer, "explorer");
        using var input = new FocusDesktopInput();
        using var probe = new HwndSource(new HwndSourceParameters("BossKeyTest") { ParentWindow = -3 });
        using var log = new StreamWriter(Path.Combine(output, "boss-key-results.txt")) { AutoFlush = true };
        var vm = main.Model;
        var shortcuts = vm.Shortcuts;
        var standard = Standard;
        var custom = new ShortcutKey(Key.J, ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        Require(!shortcuts.BossKeyEnabled && shortcuts.Get(ShortcutId.BossKey).Single() == standard, "初始配置不是关闭且使用建议组合键");
        Require(Available(standard), "默认组合键当前已被其他程序占用");
        vm.State.ListImages = ImageModes.Hidden; vm.State.ReaderImages = ImageModes.Click;
        vm.AppearanceChanged();
        await vm.OpenThreadAsync(KeyboardReadingFixture.Thread);
        await Idle();
        var list = (ListBox)main.FindName("ReaderList");
        var viewport = UiRefinementChecks.Descendants<ScrollViewer>(list).First();
        viewport.ScrollToVerticalOffset(320);
        await Idle();
        var offset = viewport.VerticalOffset;
        Require(offset > 0, "未建立真实阅读位置");

        await FocusDesktopInput.ActivateAsync(Handle(main));
        var settings = new SettingsWindow(vm) { Owner = main };
        await DuringModal(settings, async () =>
        {
            var navigation = Find<ListBox>(settings, "设置分类");
            navigation.SelectedItem = "快捷键";
            await Idle();
            var toggle = Find<CheckBox>(settings, "开启老板键");
            Require(RegisterTestKey(probe.Handle, 10, standard), "无法准备启用冲突");
            try
            {
                toggle.IsChecked = true;
                Require(toggle.IsChecked == false && !shortcuts.BossKeyEnabled &&
                    UiRefinementChecks.Descendants<TextBlock>(settings).Any(t => t.IsVisible && t.Text.Contains("占用")), "启用失败时开关或错误提示不正确");
            }
            finally { UnregisterHotKey(probe.Handle, 10); }
            toggle.IsChecked = true;
            Require(shortcuts.BossKeyEnabled && !Available(standard), "设置未注册全局老板键");
            foreach (var theme in new[] { "light", "dark" })
            {
                App.ApplyTheme(theme); await Idle(); capture(settings, Path.Combine(output, "shortcut-" + theme + ".png"));
            }
            Require(shortcuts.TrySet(ShortcutId.BossKey, new(Key.A)) is not null &&
                shortcuts.TrySet(ShortcutId.BossKey, new(Key.F12, standard.Modifiers)) is not null &&
                shortcuts.TrySet(ShortcutId.BossKey, new(Key.L, ModifierKeys.Control)) is not null, "未拒绝普通输入、保留键或主窗冲突");
            Require(shortcuts.TrySet(ShortcutId.RotateImage, standard) is not null, "看图命令可占用已启用的全局组合键");
            Require(RegisterTestKey(probe.Handle, 10, custom), "无法准备快捷键冲突");
            try
            {
                Require(shortcuts.TrySet(ShortcutId.BossKey, custom)?.Contains("占用") == true &&
                    shortcuts.Get(ShortcutId.BossKey).Single() == standard && !Available(standard), "注册失败后丢失原老板键");
            }
            finally { UnregisterHotKey(probe.Handle, 10); }
            await ToggleFrom(browser, standard, true, repeat: true);
            Require(!Visible(main) && !Visible(settings) && !IsWindowEnabled(Handle(main)), "模态设置隐藏时仍显示或解除了模态限制");
            await ToggleFrom(browser, standard, false);
            Require(GetForegroundWindow() == Handle(settings) && !IsWindowEnabled(Handle(main)), "恢复设置后焦点或模态关系错误");
            var recorder = settings.Dispatcher.InvokeAsync(async () =>
            {
                var dialog = Application.Current.Windows.OfType<DialogWindow>().Single(w => w.Title == "修改快捷键");
                try
                {
                    await FocusDesktopInput.ActivateAsync(Handle(dialog));
                    FocusDesktopInput.BossChord(Handle(dialog), custom);
                    await Until(() => !dialog.IsVisible, "录入组合键");
                }
                finally { if (dialog.IsVisible) dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            Find<Button>(settings, "老板键快捷键：修改").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await await recorder.Task;
            Require(Available(standard) && !Available(custom) && new AppShortcuts(vm.Store.Load()).Get(ShortcutId.BossKey).Single() == custom, "修改未安全切换或持久化");
            Find<Button>(settings, "老板键快捷键：恢复默认").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Available(custom) && !Available(standard), "恢复默认没有切换注册");
            toggle.IsChecked = false; Require(Available(standard), "关闭后仍占用组合键");
            toggle.IsChecked = true; Require(!Available(standard), "再次启用未注册");
            Require(GetForegroundWindow() == Handle(settings), "修改快捷键后焦点离开设置");
        });
        Require(GetForegroundWindow() == Handle(main), "关闭模态设置未返回主窗");
        log.WriteLine("通过设置开关、录入、恢复、冲突和重复注册。Edge 前台触发，长按只隐藏一次，恢复到原模态设置。");

        main.Title = "泡面岛 · 任务切换验收";
        await BossKeyShellChecks.CheckSwitchListsAsync(browser, main.Title, true);
        await ToggleFrom(browser, standard, true);
        await BossKeyShellChecks.CheckSwitchListsAsync(browser, main.Title, false);
        await ToggleFrom(browser, standard, false);
        await BossKeyShellChecks.CheckSwitchListsAsync(browser, main.Title, true);
        main.Title = AppIdentity.DisplayName;
        await FocusDesktopInput.ActivateAsync(Handle(main));
        log.WriteLine("Windows 任务栏和 Alt+Tab 在隐藏前、隐藏中、恢复后均符合预期。");

        var first = new TextWindow("引用 · 测试 A", "引用内容", true) { Owner = main };
        first.Show();
        var second = new TextWindow("引用 · 测试 B", "嵌套引用内容", true) { Owner = first };
        second.Show();
        var image = new ImageWindow([new Post { Id = 54, ImagePath = "test", Extension = ".png" }], 54,
            (_, _) => Task.FromResult(new ImageFile([], null))) { Owner = second };
        image.Show(); await Idle();
        await ToggleFrom(explorer, standard, true);
        Require(new Window[] { main, first, second, image }.All(w => !Visible(w)), "辅助窗口仍在桌面");
        Require(GetForegroundWindow() == explorer, "从资源管理器隐藏时抢走了外部焦点");
        vm.Notify("后台完成", FeedbackArea.Reader);
        await vm.LoadSubscriptionsAsync(true);
        Require(vm.SubscriptionKnown && !Visible(main), "后台同步中断或把窗口重新显示");
        await main.OpenContentLinkAsync("https://www.nmbxd1.com/t/710002");
        await Task.Delay(150);
        Require(main.BossKey.IsHidden && main.OwnedWindows.Count == 1, "后台回调重新显示了窗口");
        await BossKeyShellChecks.UseIconAsync(menuItem: null);
        await Until(() => !main.BossKey.IsHidden, "托盘双击恢复");
        Require(new Window[] { main, first, second, image }.All(Visible) && GetForegroundWindow() == Handle(image), "托盘恢复未激活原看图窗口");
        image.Close();
        Require(GetForegroundWindow() == Handle(second), "看图关闭未返回嵌套引用");
        second.Close(); Require(GetForegroundWindow() == Handle(first), "嵌套引用关闭未返回直接 Owner");
        first.Close(); Require(GetForegroundWindow() == Handle(main), "引用关闭未返回主窗");
        Require(vm.ReadingThreadId == KeyboardReadingFixture.Thread && Math.Abs(viewport.VerticalOffset - offset) < 1, "阅读串或阅读位置改变");
        log.WriteLine("资源管理器前台触发，主窗、嵌套引用和看图均隐藏。托盘双击恢复后逐级关闭焦点正确，保留阅读偏移 " + offset);

        var composer = new ComposerWindow(vm, KeyboardReadingFixture.Thread) { Owner = main };
        await DuringModal(composer, async () =>
        {
            var editor = UiRefinementChecks.Descendants<EmojiEditor>(composer).Single();
            editor.Text = "第一行\r\n未完成的回应"; editor.Focus(); editor.Select(5, 3);
            var selection = editor.SelectedText;
            await ToggleFrom(Handle(composer), standard, true);
            Require(GetForegroundWindow() != Handle(composer) && GetForegroundWindow() != Handle(main), "隐藏的编辑器仍占用系统前台输入");
            var cookies = new CookieWindow(vm) { Owner = composer };
            cookies.Show();
            Require(Handle(cookies) == 0 && !Visible(composer), "隐藏时的新窗口提前显示");
            await BossKeyShellChecks.UseIconAsync(menuItem: "显示泡面岛");
            await Until(() => Visible(cookies), "恢复后显示后续窗口");
            cookies.Close();
            Require(Visible(composer) && !IsWindowEnabled(Handle(main)) && GetForegroundWindow() == Handle(composer) &&
                editor.Text == "第一行\r\n未完成的回应" && editor.SelectedText == selection, "编辑器选区、内容或原生模态状态改变");
        });
        log.WriteLine("编辑器保留选区、正文、模态和草稿。隐藏期间的新饼干窗口等待托盘菜单恢复。");

        var picker = new Microsoft.Win32.OpenFileDialog { Title = "老板键文件选择验收", InitialDirectory = output };
        var pickerCheck = main.Dispatcher.InvokeAsync(async () =>
        {
            var handle = FindWindow("#32770", picker.Title);
            Require(handle != 0 && GetWindow(handle, 4) == Handle(main), "原生文件选择器未归测试主窗所有");
            try
            {
                await ToggleFrom(explorer, standard, true);
                Require(!IsWindowVisible(handle), "文件选择器残留在桌面");
                await ToggleFrom(explorer, standard, false);
                Require(IsWindowVisible(handle) && GetForegroundWindow() == handle && !IsWindowEnabled(Handle(main)), "文件选择器的模态或激活关系改变");
            }
            finally { PostMessage(handle, 0x10, 0, 0); }
        }, DispatcherPriority.ApplicationIdle);
        picker.ShowDialog(main);
        await await pickerCheck.Task;
        log.WriteLine("原生文件选择器隐藏、恢复及取消通过。");

        main.WindowState = WindowState.Maximized; await Idle();
        main.WindowState = WindowState.Minimized; await Idle();
        await ToggleFrom(explorer, standard, true);
        await ToggleFrom(explorer, standard, false);
        Require(main.WindowState == WindowState.Maximized && Visible(main), "从最小化恢复时丢失最大化");
        main.WindowState = WindowState.Normal; await Idle();
        var bounds = main.RestoreBounds;
        await ToggleFrom(Handle(main), standard, true);
        Require(shortcuts.SetBossKeyEnabled(false) is null && !main.BossKey.IsHidden && Available(standard) && main.RestoreBounds == bounds, "隐藏时停用未恢复原窗口或注销热键");
        Require(shortcuts.TrySet(ShortcutId.BossKey, custom) is null && Available(custom), "关闭时注册了热键");
        Require(shortcuts.SetBossKeyEnabled(true) is null, "自定义老板键无法启用");
        await ToggleFrom(browser, custom, true);
        await BossKeyShellChecks.UseIconAsync(menuItem: "退出泡面岛");
        await Until(() => Handle(main) == 0, "从托盘正常退出");
        Require(Available(custom), "退出残留老板键");
        log.WriteLine("最小化还原为最大化，普通窗口保持原矩形。隐藏期间停用可恢复。托盘退出后可重新取得热键。");

        var restarted = new MainWindow(new(new XIslandApi(new HttpClient(new KeyboardReadingFixture())), vm.Store));
        restarted.Show(); await Idle();
        Require(restarted.Model.Shortcuts.BossKeyEnabled && !Available(custom), "重启未恢复自定义全局注册");
        restarted.Close(); Require(Available(custom), "普通关闭未释放热键");
        Require(RegisterTestKey(probe.Handle, 10, custom), "无法准备启动冲突");
        try
        {
            var conflict = new MainWindow(new(new XIslandApi(new HttpClient(new KeyboardReadingFixture())), vm.Store));
            conflict.Show(); await Idle();
            Require(conflict.Model.Shortcuts.BossKeyStartupError?.Contains("占用") == true && conflict.Model.State.BossKeyEnabled, "启动冲突未报告或意外修改首选项");
            conflict.Close();
        }
        finally { UnregisterHotKey(probe.Handle, 10); }
        log.WriteLine("重启读取自定义键，启动冲突保留设置，普通关闭释放资源。");
        Console.WriteLine("PASS: real global keyboard input, shell tray, modal ownership, foreground restoration, settings and restart.");

        bool Available(ShortcutKey key)
        {
            if (!RegisterTestKey(probe.Handle, 11, key)) return false;
            UnregisterHotKey(probe.Handle, 11); return true;
        }
        async Task ToggleFrom(nint foreground, ShortcutKey key, bool hide, bool repeat = false)
        {
            Require(!Available(key) && shortcuts.Get(ShortcutId.BossKey).Single() == key, "测试组合键未注册");
            await FocusDesktopInput.ActivateAsync(foreground);
            FocusDesktopInput.BossChord(foreground, key, repeat);
            await Until(() => main.BossKey.IsHidden == hide && Visible(main) != hide, hide ? "全局隐藏" : "全局恢复");
            await Task.Delay(80);
            Require(main.BossKey.IsHidden == hide, "单次按键发生重复切换");
        }
    }

    internal static async Task CheckExecutableAsync(string executable, string output, nint browser)
    {
        CheckExternal(browser, "msedge");
        using var input = new FocusDesktopInput();
        Require(RegisterTestKey(0, 13, Standard), "启动交付程序前默认组合键已被其他程序占用");
        UnregisterHotKey(0, 13);
        var store = new LocalStore(Path.Combine(Path.GetFullPath(output), "profile-" + Guid.NewGuid().ToString("N")));
        store.Save(new UserState { BossKeyEnabled = true, AutoCheckUpdates = false });
        // 仅给交付 EXE 的子进程使用回环代理，返回 503，不转发或记录请求。
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        using var stop = new CancellationTokenSource();
        var connections = 0;
        var reject = RejectRequestsAsync();
        var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
        var address = $"http://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}";
        foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" }) start.Environment[key] = address;
        start.Environment["NO_PROXY"] = "";
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(store.Root);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动交付程序");
        nint hwnd = 0;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (hwnd == 0 || Volatile.Read(ref connections) == 0)
            {
                Require(!process.HasExited, "交付程序启动时退出");
                await Task.Delay(100, timeout.Token);
                process.Refresh(); hwnd = process.MainWindowHandle;
            }
            if (RegisterTestKey(0, 13, Standard))
            {
                UnregisterHotKey(0, 13);
                throw new InvalidOperationException("交付程序未从设置注册热键");
            }
            await FocusDesktopInput.ActivateAsync(browser);
            FocusDesktopInput.BossChord(browser, Standard);
            await Until(() => !IsWindowVisible(hwnd), "交付 EXE 全局隐藏");
            await BossKeyShellChecks.UseIconAsync(null, process.Id);
            await Until(() => IsWindowVisible(hwnd), "交付 EXE 托盘双击恢复");
            await FocusDesktopInput.ActivateAsync(hwnd);
            FocusDesktopInput.BossChord(hwnd, Standard);
            await Until(() => !IsWindowVisible(hwnd), "交付 EXE 再次隐藏");
            await BossKeyShellChecks.UseIconAsync("退出泡面岛", process.Id);
            await process.WaitForExitAsync(timeout.Token);
            Require(process.ExitCode == 0 && RegisterTestKey(0, 13, Standard), "交付程序退出异常或未注销热键");
            UnregisterHotKey(0, 13);
            var result = "交付 EXE 从隔离资料启动，Edge 前台触发、托盘双击恢复、托盘退出、进程退出码 0 和热键释放通过。";
            File.WriteAllText(Path.Combine(output, "result.txt"), result);
            Console.WriteLine(result);
        }
        finally
        {
            if (!process.HasExited && hwnd != 0) PostMessage(hwnd, 0x10, 0, 0);
            stop.Cancel();
            await reject;
        }
        async Task RejectRequestsAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await proxy.AcceptTcpClientAsync(stop.Token);
                    Interlocked.Increment(ref connections);
                    try { await client.GetStream().WriteAsync("HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), stop.Token); }
                    catch (IOException) { }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static async Task DuringModal(ChildWindow window, Func<Task> check)
    {
        var completed = false;
        var run = window.Dispatcher.InvokeAsync(async () =>
        {
            try { await check(); completed = true; }
            finally { window.Close(); }
        }, DispatcherPriority.ApplicationIdle);
        window.ShowDialog();
        var returnedEarly = !completed && !run.Task.Unwrap().IsCompleted;
        await await run.Task;
        Require(!returnedEarly, "隐藏提前结束了 ShowDialog");
    }

    private static T Find<T>(Window window, string name) where T : FrameworkElement =>
        UiRefinementChecks.Descendants<T>(window).Single(control => AutomationProperties.GetName(control) == name);
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static nint Handle(Window window) => new WindowInteropHelper(window).Handle;
    private static bool Visible(Window window) => IsWindowVisible(Handle(window));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static async Task Until(Func<bool> done, string stage)
    {
        for (var i = 0; i < 160 && !done(); i++) await Task.Delay(25);
        Require(done(), stage + "超时");
    }
    private static void CheckExternal(nint window, string expected)
    {
        GetWindowThreadProcessId(window, out var pid);
        using var process = Process.GetProcessById((int)pid);
        Require(process.ProcessName.Equals(expected, StringComparison.OrdinalIgnoreCase), "外部窗口不属于指定应用");
    }
    private static bool RegisterTestKey(nint hwnd, int id, ShortcutKey key) =>
        RegisterHotKey(hwnd, id, (uint)key.Modifiers, (uint)KeyInterop.VirtualKeyFromKey(key.Key));
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string cls, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
}

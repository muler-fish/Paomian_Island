using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class WindowFocusChecks
{
    internal static XIslandApi CreateApi() => new(new HttpClient(new Fixture()));

    internal static async Task RunAsync(MainWindow main, string output, nint external, bool baseline, bool desktopShell)
    {
        using var log = new StreamWriter(Path.Combine(output, "window-lifecycle.log")) { AutoFlush = true };
        var names = new Dictionary<Window, string>();
        var failures = new List<string>();
        using var input = external != 0 ? new FocusDesktopInput() : null;
        Window? previous = null;
        var tracing = true;
        try
        {
            Watch(main, "主窗口");
            await main.Model.OpenThreadAsync(100);
            await Idle();
            var reader = (ListBox)main.FindName("ReaderList");
            var reply = UiRefinementChecks.Descendants<PostBody>(reader).Single(body => body.Html.Contains("测试回应"));
            if (input is null)
            {
                previous = new Window { Width = 320, Height = 180, Title = "测试切换窗口" };
                Watch(previous, "切换窗口");
                previous.Show();
                Record("切换窗口.Show 后", previous);
                main.Activate();
            }
            else
            {
                await FocusDesktopInput.ActivateAsync(external);
                Record("外部应用在前台", main);
                await FocusDesktopInput.ActivateAsync(Handle(main));
            }
            await Idle();
            CheckActive(main, "从切换窗口回到主窗口");

            var first = await OpenQuote(main, reply, "A", 87654321);
            var second = await OpenQuote(first, Body(first), "B", 87654322);
            await CloseQuote(second, first, escape: true);
            if (input is not null && baseline && GetForegroundWindow() != Handle(first))
            {
                Record("旧版 A 失焦后重新激活 A，再测试关闭", first);
                await FocusDesktopInput.ActivateAsync(Handle(first));
            }
            await CloseQuote(first, main, escape: false);
            if (!baseline)
            {
                await CheckMainInput();
                first = await OpenQuote(main, reply, "单层引用", 87654321);
                await CloseQuote(first, main, escape: true);

                first = await OpenQuote(main, reply, "深层 A", 87654321);
                second = await OpenQuote(first, Body(first), "深层 B", 87654322);
                var third = await OpenQuote(second, Body(second), "深层 C", 87654323);
                await CloseQuote(third, second, escape: true);
                await CloseQuote(second, first, escape: false);
                await CloseQuote(first, main, escape: true);

                await CheckSettings();
                await CheckDrafts();
                if (input is null || desktopShell) await CheckBackgroundClose(reply);
                await CheckMinimize(reply);
            }

            Require(failures.Count == 0, string.Join(Environment.NewLine, failures));
            Console.WriteLine("PASS: reference ownership and native activation after each close.");
        }
        finally { tracing = false; previous?.Close(); }

        async Task<TextWindow> OpenQuote(Window source, PostBody body, string name, long id)
        {
            Record($"打开 {name} 前（Show）", source);
            body.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<Hyperlink>().Single()
                .RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
            var quote = Application.Current.Windows.OfType<TextWindow>().Single(w => w.Title == $"引用 · No.{id}");
            Watch(quote, name);
            await Until(() => Body(quote).Html.StartsWith("测试引用", StringComparison.Ordinal));
            Require(Body(quote).Html.StartsWith("测试引用", StringComparison.Ordinal), "引用未读取到模拟响应");
            await Idle();
            CheckOwner(quote, source);
            CheckActive(quote, $"打开 {name} 后");
            return quote;
        }

        async Task CloseQuote(TextWindow quote, Window owner, bool escape)
        {
            Record($"关闭 {names[quote]} 前", quote);
            if (escape)
                Escape(quote);
            else
                Click(UiRefinementChecks.Descendants<Button>(quote).Single(b => AutomationProperties.GetName(b) == "关闭"), quote);
            await Until(() => !quote.IsVisible);
            Require(!quote.IsVisible, $"{names[quote]} 未关闭");
            await Idle();
            CheckActive(owner, $"关闭 {names[quote]} 后");
        }

        async Task CheckMainInput()
        {
            var jump = (TextBox)main.FindName("JumpBox");
            if (input is null)
                jump.Focus();
            else
            {
                FocusDesktopInput.Click(jump, Handle(main));
                await Until(() => jump.IsKeyboardFocusWithin);
                FocusDesktopInput.Key(Handle(main), 0x37);
                await Until(() => jump.Text == "7");
                Require(jump.Text == "7", "引用关闭后主窗口没有收到鼠标点击及键盘输入");
                jump.Clear();
            }
            Require(jump.IsKeyboardFocusWithin && GetFocus() == Handle(main), "主窗口未立即恢复原生键盘焦点");
            Record("主窗口接收输入", main);
        }

        async Task CheckSettings()
        {
            Record("打开设置前（ShowDialog）", main);
            var closing = main.Dispatcher.InvokeAsync(() =>
            {
                var settings = Application.Current.Windows.OfType<SettingsWindow>().Single();
                Watch(settings, "设置");
                CheckOwner(settings, main);
                CheckActive(settings, "打开设置后");
                Require(!IsWindowEnabled(Handle(main)), "设置打开时主窗口仍可交互");
                Escape(settings);
            }, DispatcherPriority.ApplicationIdle);
            Click((Button)main.FindName("SettingsButton"), main);
            await closing.Task;
            await Until(() => !Application.Current.Windows.OfType<SettingsWindow>().Any());
            CheckActive(main, "关闭设置后");
            Require(IsWindowEnabled(Handle(main)), "关闭设置后主窗口仍被禁用");
        }

        async Task CheckDrafts()
        {
            main.Model.State.Drafts.Clear();
            main.Model.State.Drafts.Add(new Draft("test", 4, 100, "", "测试草稿", "", false));
            var draftsButton = UiRefinementChecks.Descendants<Button>(main).Single(button =>
                button.Content is StackPanel panel && panel.Children.OfType<TextBlock>().Any(text => text.Text == "草稿箱"));
            Record("打开草稿箱前（ShowDialog）", main);
            var closing = main.Dispatcher.InvokeAsync(() =>
            {
                var drafts = Application.Current.Windows.OfType<DraftWindow>().Single();
                Watch(drafts, "草稿箱");
                CheckOwner(drafts, main);
                var editorCheck = main.Dispatcher.InvokeAsync(() =>
                {
                    var editor = Application.Current.Windows.OfType<ComposerWindow>().Single();
                    Watch(editor, "草稿编辑器");
                    CheckOwner(editor, drafts);
                    CheckActive(editor, "打开草稿编辑器后");
                    editor.Close();
                }, DispatcherPriority.ApplicationIdle);
                Record("打开草稿编辑器前（ShowDialog）", drafts);
                UiRefinementChecks.Descendants<Button>(drafts).Single(b => Equals(b.Content, "继续编辑"))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                editorCheck.Task.GetAwaiter().GetResult();
                CheckActive(drafts, "关闭草稿编辑器后");
                drafts.Close();
            }, DispatcherPriority.ApplicationIdle);
            draftsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closing.Task;
            CheckActive(main, "关闭草稿箱后");
        }

        async Task CheckBackgroundClose(PostBody reply)
        {
            var quote = await OpenQuote(main, reply, "后台关闭", 87654321);
            if (input is null)
            {
                previous!.Activate();
                await Idle();
                quote.Close();
                CheckActive(previous, "切换后从后台关闭");
                main.Activate();
                return;
            }

            // 让外部应用进入最近使用窗口，再从引用窗打开任务切换器。
            await FocusDesktopInput.ActivateAsync(external);
            await FocusDesktopInput.ActivateAsync(Handle(quote));
            await FocusDesktopInput.AltTabAsync(Handle(quote));
            if (GetForegroundWindow() == Handle(quote))
                await FocusDesktopInput.AltTabAsync(Handle(quote), advanceTwice: true);
            Record("Alt+Tab 后", quote);
            var foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out var pid);
            Require(foreground != 0 && pid != Environment.ProcessId, "Alt+Tab 未切换到外部应用");
            quote.Close();
            await Idle();
            Record("Alt+Tab 后在后台关闭", main);
            Require(GetForegroundWindow() == foreground, "后台关闭抢回系统前台");
            await FocusDesktopInput.ActivateAsync(Handle(main));

            quote = await OpenQuote(main, reply, "任务栏切换", 87654321);
            FocusDesktopInput.ClickTaskbar(Handle(quote), external);
            await Until(() => GetForegroundWindow() == external);
            Require(GetForegroundWindow() == external, "任务栏未切换到指定外部窗口");
            quote.Close();
            await Idle();
            Record("任务栏切换后在后台关闭", main);
            Require(GetForegroundWindow() == external, "任务栏切换后关闭抢回系统前台");
            await FocusDesktopInput.ActivateAsync(Handle(main));
        }

        async Task CheckMinimize(PostBody reply)
        {
            var quote = await OpenQuote(main, reply, "最小化场景", 87654321);
            main.WindowState = WindowState.Minimized;
            await Idle();
            main.WindowState = WindowState.Normal;
            await Idle();
            if (input is null) quote.Activate();
            else await FocusDesktopInput.ActivateAsync(Handle(quote));
            await CloseQuote(quote, main, escape: true);
            await CheckMainInput();
        }

        void Escape(Window window)
        {
            if (input is not null) FocusDesktopInput.Key(Handle(window), 0x1B);
            else window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        }

        void Click(Button button, Window window)
        {
            if (input is not null) FocusDesktopInput.Click(button, Handle(window));
            else button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        void CheckOwner(Window window, Window owner)
        {
            if (!ReferenceEquals(window.Owner, owner) || GetWindow(Handle(window), 4) != Handle(owner))
                failures.Add($"{names[window]} 的 WPF 或 HWND Owner 不是 {names[owner]}");
        }

        void Watch(Window window, string name)
        {
            names.Add(window, name);
            window.Activated += (_, _) => Record("Activated", window);
            window.Deactivated += (_, _) => Record("Deactivated", window);
            window.Closing += (_, _) => Record("Closing", window);
            window.Closed += (_, _) => Record("Closed", window);
        }

        void CheckActive(Window window, string stage)
        {
            Record(stage, window);
            var foreground = GetForegroundWindow();
            if (!window.IsActive || GetActiveWindow() != Handle(window) || (input is not null || foreground != 0) && foreground != Handle(window))
                failures.Add($"{stage} 未激活 {names[window]}");
        }

        void Record(string stage, Window window)
        {
            if (!tracing) return;
            var foreground = GetForegroundWindow();
            _ = GetWindowThreadProcessId(foreground, out var pid);
            var owner = window.Owner;
            log.WriteLine($"{stage} window={names[window]}/{window.GetType().Name}:{Describe(Handle(window))} owner={Describe(owner is null ? 0 : Handle(owner))} foreground={Describe(foreground)} pid={pid} active={Describe(GetActiveWindow())} IsActive={window.IsActive}");
        }

        string Describe(nint handle)
        {
            if (handle == 0) return "0";
            var name = names.FirstOrDefault(entry => Handle(entry.Key) == handle).Value;
            return name is null ? $"0x{handle:X}" : $"{name}/0x{handle:X}";
        }
    }

    private static PostBody Body(Window window) => UiRefinementChecks.Descendants<PostBody>(window).Single();
    private static nint Handle(Window window) => new WindowInteropHelper(window).Handle;
    private static async Task Idle() => await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    private static async Task Until(Func<bool> done) { for (var i = 0; i < 40 && !done(); i++) await Task.Delay(25); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Require(request.Method == HttpMethod.Get, "窗口测试只读");
            var path = request.RequestUri!;
            if (path.AbsolutePath.EndsWith("/ref", StringComparison.Ordinal))
            {
                await Task.Delay(30, ct);
                var id = long.Parse(System.Web.HttpUtility.ParseQueryString(path.Query)["id"]!);
                var content = id switch
                {
                    87654321 => "测试引用 &gt;&gt;No.87654322",
                    87654322 => "测试引用 <a href='https://www.nmbxd1.com/t/87654323'>&gt;&gt;No.87654323</a>",
                    _ => "测试引用",
                };
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id, content })) };
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"id":100,"fid":4,"user_hash":"fixture","ReplyCount":1,"content":"测试主楼",
                 "Replies":[{"id":101,"fid":4,"user_hash":"fixture","content":"测试回应 &gt;&gt;No.87654321"}]}
                """) };
        }
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
}

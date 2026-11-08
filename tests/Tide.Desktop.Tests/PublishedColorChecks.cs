using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>
/// 对实际交付的 EXE 做调色板验收：独立资料目录启动，经 UI 自动化打开设置，用真实鼠标在色域和色相条上点击、拖动，
/// 设置标识颜色、切换深色，关闭程序后重新启动检查颜色仍在。会移动系统光标，只在显式指定 --exe-colors 时运行。
/// 程序启动时会照常向 X 岛读取板块和公告（只读请求），不发送任何内容；自动检查更新已关闭。
/// </summary>
internal static class PublishedColorChecks
{
    // 只结束本检查自己启动的进程，不按程序名结束，避免误关用户正在使用的实例。
    private static readonly List<Process> Launched = [];

    internal static int Run(string executable, string outputDirectory)
    {
        var output = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Directory.CreateDirectory(output);
        var profileRoot = Path.Combine(output, "profile");
        var timeline = new Forum(1, "综合线", "时间线", IsTimeline: true);
        new LocalStore(profileRoot).Save(new UserState { Forums = [timeline, new(4, "综合版1", "综合")], FavoriteForums = [timeline], AutoCheckUpdates = false, Theme = "light" });
        GetCursorPos(out var cursor);
        var report = new Dictionary<string, object>();
        try
        {
            string finalAccent, finalCookie;
            {
                var app = Launch(executable, profileRoot, output);
                var settings = OpenSettings(app);
                Select(settings, "外观"); Step("已切到外观");
                Invoke(Find(settings, "自定义强调色调色")); Step("已打开调色板");
                var area = FindInProcess(app.Id, "自定义强调色色域");
                var hue = FindInProcess(app.Id, "自定义强调色色相条");
                var hex = Find(settings, "自定义强调色 HEX");
                Step("找到色域、色相条和 HEX");
                var startHue = Personalization.ToHsv(Personalization.Rgb(Value(hex))).H;
                // 单击
                Click(At(area, .8, .2)); Thread.Sleep(300);
                var (h1, s1, v1) = Hsv(Value(hex));
                Require(Math.Abs(s1 - .8) < .04 && Math.Abs(v1 - .8) < .04 && Math.Abs(h1 - startHue) < 3, $"点击色域结果不对：{Value(hex)}");
                // 按住拖动，途中读取 HEX，确认连续变化
                var trail = new List<string>();
                Move(At(area, .2, .2)); Thread.Sleep(60); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(80);
                for (var i = 1; i <= 8; i++) { Move(At(area, .2 + .0875 * i, .2 + .0625 * i)); Thread.Sleep(90); trail.Add(Value(hex)); }
                Shot(app.Id, Path.Combine(output, "exe-dragging.png"));
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(300);
                var (_, s2, v2) = Hsv(Value(hex));
                Require(Math.Abs(s2 - .9) < .04 && Math.Abs(v2 - .3) < .04 && trail.Distinct().Count() >= 5, $"拖动结果不对：{Value(hex)} / {string.Join(",", trail)}");
                // 色相条
                Click(At(hue, 2d / 3, .5)); Thread.Sleep(300);
                Require(Math.Abs(Hsv(Value(hex)).H - 240) < 5, $"色相条结果不对：{Value(hex)}");
                finalAccent = Value(hex);
                Shot(app.Id, Path.Combine(output, "exe-accent-light.png"));
                // 点击设置页上方空白处关闭调色板
                Click(At(settings, .5, .05)); Thread.Sleep(300);
                Require(TryFindInProcess(app.Id, "自定义强调色色域") is null, "点击外部没有关闭调色板");
                // HEX 输入框写入后，调色板位置跟随
                // 标识颜色：展开后设置饼干 ID，再用调色板点一下
                Toggle(Find(settings, "标识颜色"));
                var cookieHex = Find(settings, "饼干 ID颜色 HEX");
                ((ValuePattern)cookieHex.GetCurrentPattern(ValuePattern.Pattern)).SetValue("#1F7A8C"); Thread.Sleep(300);
                Invoke(Find(settings, "饼干 ID颜色调色")); Thread.Sleep(300);
                var cookieArea = FindInProcess(app.Id, "饼干 ID颜色色域");
                Click(At(cookieArea, .6, .3)); Thread.Sleep(300);
                finalCookie = Value(cookieHex);
                Require(finalCookie != "#1F7A8C" && Math.Abs(Hsv(finalCookie).H - Hsv("#1F7A8C").H) < 3 && Value(hex) == finalAccent, $"饼干 ID 调色结果不对或影响了强调色：{finalCookie}");
                Shot(app.Id, Path.Combine(output, "exe-identity-light.png"));
                Click(At(settings, .5, .05)); Thread.Sleep(300);
                // 深色主题
                SelectCombo(settings, "外观主题", "深色"); Thread.Sleep(500);
                Invoke(Find(settings, "自定义强调色调色")); Thread.Sleep(300);
                Shot(app.Id, Path.Combine(output, "exe-accent-dark.png"));
                Click(At(settings, .5, .05)); Thread.Sleep(300);
                Invoke(Find(settings, "完成")); Thread.Sleep(300);
                Close(app);
            }
            var saved = new LocalStore(profileRoot).Load();
            Require(saved.AccentColor == finalAccent && saved.IdentityColors.Cookie == finalCookie && saved.Theme == "dark", $"关闭后保存的颜色不对：{saved.AccentColor} {saved.IdentityColors.Cookie}");
            // 重新启动：设置页显示上次的颜色
            {
                var app = Launch(executable, profileRoot, output);
                var settings = OpenSettings(app);
                Select(settings, "外观");
                var accent = Value(Find(settings, "自定义强调色 HEX"));
                Toggle(Find(settings, "标识颜色"));
                var cookie = Value(Find(settings, "饼干 ID颜色 HEX"));
                Shot(app.Id, Path.Combine(output, "exe-restarted-dark.png"));
                Require(accent == finalAccent && cookie == finalCookie, $"重启后颜色丢失：{accent} {cookie}");
                Invoke(Find(settings, "完成")); Thread.Sleep(300);
                Close(app);
            }
            report["Accent"] = finalAccent; report["Cookie"] = finalCookie;
            File.WriteAllText(Path.Combine(output, "exe-colors.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS published exe colors: real mouse click/drag on color area, hue bar, outside click closes, identity picker independent, dark theme, persistence across restart. " + JsonSerializer.Serialize(report));
            return 0;
        }
        finally
        {
            SetCursorPos(cursor.X, cursor.Y);
            foreach (var process in Launched) { try { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } } catch (InvalidOperationException) { } process.Dispose(); }
        }
    }

    private static void Step(string text) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {text}"); Console.Out.Flush(); }

    private static Process Launch(string executable, string profile, string output)
    {
        var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
        start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(output, "runtime-extract");
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(profile);
        var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动交付程序。");
        Launched.Add(process); Step("已启动 " + process.Id);
        for (var i = 0; i < 150 && process.MainWindowHandle == IntPtr.Zero; i++) { Thread.Sleep(100); process.Refresh(); if (process.HasExited) throw new InvalidOperationException("交付程序提前退出。"); }
        Step("主窗口已创建"); Thread.Sleep(1500);
        // 启动公告是模态窗口，出现时先关掉，不改动「不再显示」。
        for (var i = 0; i < 30; i++)
        {
            var notice = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id), new PropertyCondition(AutomationElement.NameProperty, "来自 X 岛的通知")));
            if (notice is not null) { ((WindowPattern)notice.GetCurrentPattern(WindowPattern.Pattern)).Close(); Thread.Sleep(500); break; }
            Thread.Sleep(100);
        }
        return process;
    }

    private static AutomationElement OpenSettings(Process app)
    {
        Step("打开设置");
        var main = AutomationElement.FromHandle(app.MainWindowHandle);
        Foreground(app.MainWindowHandle);
        var button = main.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new PropertyCondition(AutomationElement.AutomationIdProperty, "SettingsButton")))
            ?? throw new InvalidOperationException("找不到设置按钮。");
        Step("找到设置按钮"); Invoke(button); Step("已点击设置按钮");
        for (var i = 0; i < 50; i++)
        {
            // 设置窗口有 Owner，在自动化树里挂在主窗口下面，不在桌面顶层。
            var dialog = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window), new PropertyCondition(AutomationElement.NameProperty, "设置"));
            var settings = main.FindFirst(TreeScope.Children, dialog) ?? AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id), dialog));
            if (settings is not null) { Thread.Sleep(400); Foreground(new IntPtr(settings.Current.NativeWindowHandle)); return settings; }
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("设置窗口没有打开。");
    }

    // 先等设置对话框真正关闭（UI 自动化的点击是异步的），再关主窗口；主窗口不响应时改发 WM_CLOSE。
    private static void Close(Process app)
    {
        var main = AutomationElement.FromHandle(app.MainWindowHandle);
        var dialog = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window), new PropertyCondition(AutomationElement.NameProperty, "设置"));
        for (var i = 0; i < 50 && main.FindFirst(TreeScope.Children, dialog) is not null; i++) Thread.Sleep(100);
        Require(main.FindFirst(TreeScope.Children, dialog) is null, "设置窗口没有关闭");
        Step("关闭程序");
        ((WindowPattern)main.GetCurrentPattern(WindowPattern.Pattern)).Close();
        if (app.WaitForExit(8000)) return;
        PostMessage(app.MainWindowHandle, 0x0010, IntPtr.Zero, IntPtr.Zero);
        if (!app.WaitForExit(8000)) throw new InvalidOperationException("交付程序没有正常退出。");
    }

    private static AutomationElement Find(AutomationElement root, string name)
    {
        for (var i = 0; i < 30; i++)
        {
            if (root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) is { } found) return found;
            Thread.Sleep(100);
        }
        throw new InvalidOperationException("找不到界面元素：" + name);
    }
    // 弹出的调色板是单独的顶层窗口，按进程查所有顶层窗口。
    private static AutomationElement? TryFindInProcess(int pid, string name)
    {
        // 调色板是 Popup 的独立窗口；它可能在桌面顶层，也可能挂在所属窗口下面，按进程把顶层窗口逐个往下找。
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)))
            if (window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name)) is { } found) return found;
        return null;
    }
    private static AutomationElement FindInProcess(int pid, string name)
    {
        for (var i = 0; i < 30; i++) { if (TryFindInProcess(pid, name) is { } found) return found; Thread.Sleep(100); }
        throw new InvalidOperationException("找不到调色板元素：" + name);
    }
    private static void Invoke(AutomationElement element) { ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Thread.Sleep(300); }
    // 「标识颜色」是 Expander：优先用展开模式，其次用切换模式。
    private static void Toggle(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) ((ExpandCollapsePattern)expand).Expand();
        else ((TogglePattern)element.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        Thread.Sleep(400);
    }
    private static void Select(AutomationElement settings, string category) { ((SelectionItemPattern)Find(settings, category).GetCurrentPattern(SelectionItemPattern.Pattern)).Select(); Thread.Sleep(400); }
    private static void SelectCombo(AutomationElement settings, string combo, string item)
    {
        // 设置行的标签文字和下拉框同名，按控件类型区分。
        var box = settings.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox), new PropertyCondition(AutomationElement.NameProperty, combo)))
            ?? throw new InvalidOperationException("找不到下拉框：" + combo);
        ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand(); Thread.Sleep(300);
        var option = box.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem), new PropertyCondition(AutomationElement.NameProperty, item)))
            ?? throw new InvalidOperationException("找不到选项：" + item);
        ((SelectionItemPattern)option.GetCurrentPattern(SelectionItemPattern.Pattern)).Select(); Thread.Sleep(200);
        ((ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Collapse();
    }
    private static string Value(AutomationElement element) => ((ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    private static (double H, double S, double V) Hsv(string hex) => Personalization.ToHsv(Personalization.Rgb(hex));
    private static Point At(AutomationElement element, double fx, double fy)
    {
        var r = element.Current.BoundingRectangle;
        return new(r.Left + r.Width * fx, r.Top + r.Height * fy);
    }
    private static void Move(Point p) => SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
    private static void Click(Point p) { Move(p); Thread.Sleep(80); mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(60); mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); }
    private static void Foreground(IntPtr handle)
    {
        var foreignThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero); var ownThread = GetCurrentThreadId();
        if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, true);
        try { BringWindowToTop(handle); SetForegroundWindow(handle); }
        finally { if (foreignThread != ownThread) AttachThreadInput(ownThread, foreignThread, false); }
        Thread.Sleep(300);
    }
    // 按该进程所有顶层窗口的并集从屏幕截图（设置窗口和调色板一起）。
    private static void Shot(int pid, string path)
    {
        Rect? area = null;
        var mainWindow = AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, pid), new PropertyCondition(AutomationElement.NameProperty, AppIdentity.WindowTitle)));
        if (mainWindow?.FindFirst(TreeScope.Children, new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window), new PropertyCondition(AutomationElement.NameProperty, "设置"))) is { } dialog)
            area = dialog.Current.BoundingRectangle;
        foreach (AutomationElement window in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)))
            if (window.Current.Name != AppIdentity.WindowTitle && !window.Current.BoundingRectangle.IsEmpty) { var r = window.Current.BoundingRectangle; area = area is { } a ? Rect.Union(a, r) : r; }
        if (area is not { } bounds) return;
        int x = (int)bounds.Left, y = (int)bounds.Top, w = (int)Math.Ceiling(bounds.Width), h = (int)Math.Ceiling(bounds.Height);
        var dc = GetDC(IntPtr.Zero); var memory = CreateCompatibleDC(dc); var bitmap = CreateCompatibleBitmap(dc, w, h); var old = SelectObject(memory, bitmap);
        try
        {
            BitBlt(memory, 0, 0, w, h, dc, x, y, 0x00CC0020);
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
        }
        finally { SelectObject(memory, old); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(IntPtr.Zero, dc); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, IntPtr process);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool value);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint operation);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
}

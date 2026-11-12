using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--cache-responsiveness")
        {
            try { CacheResponsivenessChecks.Run(args.ElementAtOrDefault(1) ?? "artifacts/cache-responsiveness"); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        // 只走真实视图模型和模拟传输，不创建窗口或操作系统输入。
        if (args.FirstOrDefault() == "--navigation-failures")
        {
            try { DispatcherChecks.Run(() => NavigationFailureChecks.RunAsync(args.ElementAtOrDefault(1) ?? "artifacts/navigation-failures")); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Length == 2 && args[0] == "--subscription-live")
        {
            try { SubscriptionLiveProbe.RunAsync(args[1]).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        }
        // 交付 EXE 的调色板验收：会移动系统光标，只在显式指定时运行。
        if (args.Length == 3 && args[0] == "--exe-colors")
        {
            try { return PublishedColorChecks.Run(args[1], args[2]); }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        if (args.Length == 3 && args[0] is "--exe" or "--exe-with-notice" or "--exe-with-tips")
        {
            try { return CheckPublishedExecutable(args[1], args[2], args[0] == "--exe-with-notice", args[0] == "--exe-with-tips"); }
            catch (Exception ex)
            {
                Directory.CreateDirectory(args[2]);
                File.WriteAllText(Path.Combine(args[2], "failure.txt"), ex.ToString());
                Console.Error.WriteLine(ex); return 1;
            }
        }
        var refinedOnly = args.FirstOrDefault() == "--refined-reader";
        var layoutOnly = args.FirstOrDefault() == "--layout";
        var childOnly = args.FirstOrDefault() == "--child-windows";
        var forumOnly = args.FirstOrDefault() == "--forum-navigation";
        var noticeOnly = args.FirstOrDefault() == "--notices";
        var readerOnly = args.FirstOrDefault() == "--reader-controls";
        var subscriptionOnly = args.FirstOrDefault() == "--subscriptions";
        var cacheOnly = args.FirstOrDefault() == "--subscription-cache";
        var incrementalOnly = args.FirstOrDefault() == "--incremental";
        var inertiaOnly = args.FirstOrDefault() == "--inertia";
        var navigationOnly = args.FirstOrDefault() == "--thread-navigation";
        var progressOnly = args.FirstOrDefault() == "--reading-progress";
        var cookieOnly = args.FirstOrDefault() == "--cookie-import";
        var emoticonsOnly = args.FirstOrDefault() == "--emoticons";
        var fontOnly = args.FirstOrDefault() == "--emoticon-font";
        var typographyOnly = args.FirstOrDefault() == "--typography";
        var emojiOnly = args.FirstOrDefault() == "--emoji";
        var tipsOnly = args.FirstOrDefault() == "--tips";
        var spoilersOnly = args.FirstOrDefault() == "--spoilers";
        var floatingOnly = args.FirstOrDefault() == "--floating-actions";
        var previewOnly = args.FirstOrDefault() == "--post-preview";
        var layoutMemoryOnly = args.FirstOrDefault() == "--layout-memory";
        var imagesOnly = args.FirstOrDefault() == "--images";
        var linksOnly = args.FirstOrDefault() == "--links";
        var contextActionsOnly = args.FirstOrDefault() == "--context-actions";
        var feedbackMotionOnly = args.FirstOrDefault() == "--feedback-motion";
        var readingPolishOnly = args.FirstOrDefault() == "--reading-polish";
        var postMenuOnly = args.FirstOrDefault() == "--post-menu";
        var menuSplitOnly = args.FirstOrDefault() == "--menu-split";
        var spoilerGeometryOnly = args.FirstOrDefault() == "--spoiler-geometry";
        var settingsUiOnly = args.FirstOrDefault() == "--settings-ui";
        var brandingOnly = args.FirstOrDefault() == "--branding";
        var fontTrialOnly = args.FirstOrDefault() == "--font-trial";
        var readingIdentityOnly = args.FirstOrDefault() == "--reading-identity";
        var sentPostsOnly = args.FirstOrDefault() == "--sent-posts";
        var reportOnly = args.FirstOrDefault() == "--reports";
        var fontComparisonOnly = args.FirstOrDefault() == "--font-comparison";
        var output = Path.GetFullPath((layoutOnly || childOnly || forumOnly || noticeOnly || readerOnly || subscriptionOnly || cacheOnly || incrementalOnly || inertiaOnly || navigationOnly || progressOnly || cookieOnly || emoticonsOnly || fontOnly || typographyOnly || emojiOnly || tipsOnly || spoilersOnly || floatingOnly || previewOnly || layoutMemoryOnly || imagesOnly || linksOnly || contextActionsOnly || feedbackMotionOnly || readingPolishOnly || fontComparisonOnly || reportOnly || postMenuOnly || fontTrialOnly ? args.ElementAtOrDefault(1) : args.FirstOrDefault()) ?? "artifacts/window-qa");
        if (readingIdentityOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/reading-identity");
        if (sentPostsOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/sent-posts");
        if (menuSplitOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/menu-split");
        if (spoilerGeometryOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/spoiler-geometry");
        if (settingsUiOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/settings-ui");
        if (brandingOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/branding");
        if (refinedOnly) output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/refined-reader-migration/qa");
        if (args.FirstOrDefault() == "--reader-feedback") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/reader-feedback");
        if (args.FirstOrDefault() == "--compact-cookie") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/compact-cookie");
        if (args.FirstOrDefault() == "--cookie-manager") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/cookie-manager");
        if (args.FirstOrDefault() == "--emoticon-custom") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/emoticon-custom");
        if (args.FirstOrDefault() == "--real-ime") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/real-ime");
        if (args.FirstOrDefault() == "--settings-personalization") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/settings-personalization");
        if (args.FirstOrDefault() == "--emoticon-layout") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/emoticon-layout");
        if (args.FirstOrDefault() == "--emoticon-perf") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/emoticon-perf");
        if (args.FirstOrDefault() == "--composer-input") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/composer-input");
        if (args.FirstOrDefault() == "--subscription-display") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/subscription-display");
        if (args.FirstOrDefault() == "--settings-layout") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/settings-layout");
        if (args.FirstOrDefault() == "--image-notice") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/image-notice");
        if (args.FirstOrDefault() == "--gif") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/gif-playback");
        if (args.FirstOrDefault() == "--gif-copy") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/gif-copy");
        if (args.FirstOrDefault() == "--reading-history") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/reading-history");
        if (args.FirstOrDefault() == "--ui-copy") output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? "artifacts/ui-copy");
        Directory.CreateDirectory(output);
        // 使用真正的主窗口及同一份样式资源，但不启动 App 的联网流程。
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, Resources = LoadResources() };
        App.ApplyTheme("light");
        var vm = new MainViewModel(refinedOnly ? RefinedReaderChecks.CreateApi() : UiRefinementChecks.CreateApi(), new LocalStore(Path.Combine(output, "profile")));
        var window = new MainWindow(vm) { ShowInTaskbar = args.Contains("--hold") };
        var started = false;
        window.ContentRendered += async (_, _) =>
        {
            if (started) return;
            started = true;
            try
            {
                if (args.FirstOrDefault() == "--ui-copy")
                {
                    await UiCopyChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--reading-history")
                {
                    // --real-input 会移动系统光标并发送真实按键，只在明确指定时运行。
                    if (args.Contains("--real-input")) await ReadingHistoryChecks.RunRealInputAsync(window, output, Capture);
                    else await ReadingHistoryChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--gif-copy")
                {
                    await GifClipboardChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--gif")
                {
                    await GifChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--image-notice")
                {
                    await ImageViewerChecks.CheckNoticeAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--settings-layout")
                {
                    await SettingsLayoutChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--emoticon-custom")
                {
                    await EmoticonCustomChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--settings-personalization")
                {
                    await SettingsPersonalizationChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--emoticon-layout")
                {
                    await EmoticonLayoutChecks.RunAsync(window, output);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--emoticon-perf")
                {
                    await EmoticonPerfChecks.RunAsync(window, output);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--composer-input")
                {
                    await ComposerInputChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                // 使用真实的系统输入法并发送按键，会占用键盘；只在显式指定时运行。
                if (args.FirstOrDefault() == "--real-ime")
                {
                    await RealImeProbe.RunAsync(window, output);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--subscription-display")
                {
                    await SubscriptionDisplayChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--cookie-manager")
                {
                    await CookieManagerChecks.RunAsync(window, output, Capture);
                    await CookieReplyChecks.RunAsync(window, output, Capture, selectorOnly: true);
                    await ContinuationVisualChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--compact-cookie")
                {
                    if (!args.Contains("--loading-only")) await CookieReplyChecks.RunAsync(window, output, Capture, selectorOnly: true);
                    await ContinuationVisualChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (args.FirstOrDefault() == "--reader-feedback")
                {
                    await ReferencePolishChecks.RunAsync(window, output, Capture);
                    await ReadingAdjustmentsChecks.RunAsync(window, output, Capture);
                    await CookieReplyChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (refinedOnly)
                {
                    await RefinedReaderChecks.RunAsync(window, output, Capture, args.Contains("--inspect-only"));
                    if (args.Contains("--hold")) { window.Title += " · 界面验收（隔离资料）"; window.Closed += (_, _) => app.Shutdown(0); return; }
                    app.Shutdown(0); return;
                }
                if (spoilerGeometryOnly) { await SpoilerGeometryChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (settingsUiOnly) { await SettingsUiChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (brandingOnly) { await BrandingUiChecks.RunAsync(window, output, Capture, args.Contains("--assets")); app.Shutdown(0); return; }
                if (postMenuOnly) { await ContextActionChecks.RunAsync(window, output, Capture, postMenuOnly: true); app.Shutdown(0); return; }
                if (menuSplitOnly) { await ContextActionChecks.RunAsync(window, output, Capture, menuSplitOnly: true); app.Shutdown(0); return; }
                if (fontTrialOnly) { await FontTrialChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (readingIdentityOnly) { await ReadingIdentityChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (sentPostsOnly) { await SentPostsChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (reportOnly) { await ReportChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (readingPolishOnly) { await ReadingPolishChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (fontComparisonOnly) { await ReadingPolishChecks.CompareFontsAsync(window, output, Capture); app.Shutdown(0); return; }
                if (feedbackMotionOnly) { await FeedbackMotionChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (contextActionsOnly) { await ContextActionChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (linksOnly) { await ContentLinkChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (imagesOnly) { await ImageViewerChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (layoutMemoryOnly) { await LayoutMemoryChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (previewOnly) { await PostPreviewChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (floatingOnly) { await FloatingActionsChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (spoilersOnly) { await SpoilerChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (tipsOnly) { await ThreadTipsChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (typographyOnly) { await TypographyChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (emojiOnly) { await EmojiChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (fontOnly) { await EmoticonFontChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (emoticonsOnly) { await EmoticonChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (cookieOnly) { await CookieImportChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (progressOnly) { await ReadingProgressChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (navigationOnly) { await ThreadNavigationChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (inertiaOnly) { await InertialScrollChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (incrementalOnly) { await IncrementalLoadingChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (cacheOnly) { await SubscriptionCacheChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (subscriptionOnly) { await SubscriptionChecks.RunAsync(window, output, Capture); app.Shutdown(0); return; }
                if (readerOnly)
                {
                    await ReaderControlsChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0); return;
                }
                if (noticeOnly)
                {
                    await SiteNoticeChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0);
                    return;
                }
                if (forumOnly)
                {
                    await ForumFavoritesStateChecks.RunAsync(output);
                    await ForumNavigationChecks.RunAsync(window, vm, output, Capture);
                    app.Shutdown(0);
                    return;
                }
                if (childOnly)
                {
                    await ChildWindowChecks.RunAsync(window, vm, output, Capture);
                    app.Shutdown(0);
                    return;
                }
                if (layoutOnly)
                {
                    await TextLayoutChecks.RunAsync(window, output, Capture);
                    app.Shutdown(0);
                    return;
                }
                // 等窗口完成布局，再检查可见窗口及实际标题栏按钮的屏幕坐标。
                await Task.Delay(250);
                var report = Inspect(window);
                File.WriteAllText(Path.Combine(output, "window-results.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
                Capture(window, Path.Combine(output, "startup-window.png"));
                Console.WriteLine(JsonSerializer.Serialize(report));
                if (!report.WindowFits || !report.CaptionButtonsVisible) throw new InvalidOperationException("启动窗口或标题栏按钮超出屏幕工作区。");
                var scenarioCount = CheckSizingScenarios();
                // 最大化后再还原也必须保留可见标题栏，不应因为尺寸限制而恢复到屏幕外。
                window.WindowState = WindowState.Maximized;
                await Task.Delay(150);
                var maximized = Inspect(window);
                if (!maximized.CaptionButtonsVisible) throw new InvalidOperationException("最大化后标题栏按钮不可见。");
                window.WindowState = WindowState.Normal;
                await Task.Delay(150);
                var restored = Inspect(window);
                if (!restored.WindowFits || !restored.CaptionButtonsVisible) throw new InvalidOperationException("还原后窗口越界。");
                var dialogs = new List<WindowReport>();
                foreach (var dialog in new Window[] { new ComposerWindow(vm, 123), new CookieWindow(vm) })
                {
                    dialog.Owner = window; dialog.ShowInTaskbar = false; dialog.Show();
                    await Task.Delay(150);
                    var dialogReport = Inspect(dialog); dialogs.Add(dialogReport);
                    if (!dialogReport.WindowFits || !dialogReport.CaptionButtonsVisible) throw new InvalidOperationException("弹窗越界。");
                    dialog.Close();
                }
                File.WriteAllText(Path.Combine(output, "regression-results.json"), JsonSerializer.Serialize(new { Startup = report, Maximized = maximized, Restored = restored, Dialogs = dialogs, SizingScenarios = scenarioCount }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"PASS: startup, maximize/restore, two dialogs, {scenarioCount} screen/DPI sizing scenarios.");
                await FluentWindowChecks.RunAsync(window, output, Capture);
                await ChildWindowChecks.RunAsync(window, vm, output, Capture);
                await UiRefinementChecks.RunAsync(window, vm, output, Capture);
                await ReaderControlsChecks.RunAsync(window, output, Capture);
                await SubscriptionChecks.RunAsync(window, output, Capture);
                await SubscriptionCacheChecks.RunAsync(window, output, Capture);
                await IncrementalLoadingChecks.RunAsync(window, output, Capture);
                await InertialScrollChecks.RunAsync(window, output, Capture);
                await ThreadNavigationChecks.RunAsync(window, output, Capture);
                await ReadingProgressChecks.RunAsync(window, output, Capture);
                await ReadingHistoryChecks.RunAsync(window, output, Capture);
                await CookieImportChecks.RunAsync(window, output, Capture);
                await EmoticonChecks.RunAsync(window, output, Capture);
                await TextLayoutChecks.RunAsync(window, output, Capture);
                await ForumFavoritesStateChecks.RunAsync(output);
                await ForumNavigationChecks.RunAsync(window, vm, output, Capture);
                await SiteNoticeChecks.RunAsync(window, output, Capture);
                app.Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString());
                Console.Error.WriteLine(ex);
                app.Shutdown(1);
            }
        };
        return app.Run(window);
    }

    // 独立检查实际交付的单文件 EXE，使用临时资料目录，完成后正常关闭测试实例。
    private static int CheckPublishedExecutable(string executable, string outputDirectory, bool requireNotice = false, bool requireTips = false)
    {
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        // 只为隔离测试目录准备目录和收藏快照；实际 EXE 的窗口验收不需要站点返回内容。
        var profile = new LocalStore(Path.Combine(output, "profile"));
        if (!File.Exists(Path.Combine(profile.Root, "state.json")))
        {
            var timeline = new Forum(1, "综合线", "时间线", IsTimeline: true);
            var board = new Forum(4, "综合版1", "综合");
            profile.Save(new UserState { Forums = [timeline, board, new(17, "动画", "兴趣")], FavoriteForums = [timeline, board] });
        }
        var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        // 隔离并记录单文件启动时的原生运行时展开体积，避免只报告压缩 EXE 大小。
        start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.Combine(output, "runtime-extract");
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(Path.Combine(output, "profile"));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动交付程序。");
        try
        {
            for (var attempt = 0; attempt < 100; attempt++)
            {
                Thread.Sleep(100); process.Refresh();
                if (process.HasExited) throw new InvalidOperationException("交付程序在窗口创建前退出。");
                if (process.MainWindowHandle != IntPtr.Zero) break;
            }
            if (process.MainWindowHandle == IntPtr.Zero) throw new InvalidOperationException("等待交付程序窗口超时。");
            Thread.Sleep(300);
            // 自定义按钮属于 WPF 可访问性树，不能用 DWM 的系统标题栏占位矩形代替。
            var root = WaitPublishedWindow(process.Id, AppIdentity.WindowTitle);
            var mainHandle = new IntPtr(root.Current.NativeWindowHandle);
            var caption = PublishedCaptionBounds(root);
            var report = Inspect(mainHandle, caption);
            Capture(mainHandle, Path.Combine(output, "startup-window.png"));
            var version = FileVersionInfo.GetVersionInfo(start.FileName).FileVersion;
            // 这三个窗口不依赖接口数据。即使站点维护，也能对最终单文件做真实的标题栏验收。
            var children = new Dictionary<string, WindowReport>();
            // 等待独立通知请求完成；有公告时确认当前真实正文与免打扰保存，再继续其他窗口验收。
            if (root.Current.IsEnabled) root.SetFocus();
            AutomationElement? notice = null;
            for (var attempt = 0; attempt < 180 && notice is null; attempt++)
            {
                var handle = IntPtr.Zero;
                EnumWindows((h, _) =>
                {
                    GetWindowThreadProcessId(h, out var pid);
                    if (pid == process.Id)
                    {
                        var name = new StringBuilder(256); GetWindowText(h, name, name.Capacity);
                        if (name.ToString() == "来自 X 岛的通知") handle = h;
                    }
                    return true;
                }, IntPtr.Zero);
                if (handle != IntPtr.Zero) notice = AutomationElement.FromHandle(handle);
                else Thread.Sleep(50);
            }
            var noticeShown = notice is not null;
            if (requireNotice && notice is null) throw new InvalidOperationException("本次最终 EXE 验收要求启动时显示真实站点公告，但没有收到公告窗口。");
            if (notice is not null)
            {
                Thread.Sleep(250);
                children["site-notice"] = InspectPublishedChild(notice, output, "site-notice");
                var body = notice.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "X 岛通知正文"))
                    ?? throw new InvalidOperationException("真实通知正文控件不可访问。");
                var content = ((TextPattern)body.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.GetText(-1);
                if (string.IsNullOrWhiteSpace(content)) throw new InvalidOperationException("真实通知正文为空。");
                File.WriteAllText(Path.Combine(output, "notice-text.txt"), content);
                var check = notice.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "不再显示该条通知"))!;
                ((TogglePattern)check.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                InvokePublishedButton(notice, AutomationElement.NameProperty, "确定通知");
                for (var attempt = 0; attempt < 100 && (!root.Current.IsEnabled || profile.Load().SuppressedNoticeKeys.Count == 0); attempt++) Thread.Sleep(50);
                if (!root.Current.IsEnabled || profile.Load().SuppressedNoticeKeys.Count == 0)
                    throw new InvalidOperationException("真实通知关闭后未恢复主窗口或没有保存免打扰。");
            }
            if (requireTips)
            {
                var jump = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "JumpBox"))!;
                ((ValuePattern)jump.GetCurrentPattern(ValuePattern.Pattern)).SetValue("51288554");
                InvokePublishedButton(root, AutomationElement.NameProperty, "跳转到串");
                var originalCondition = new PropertyCondition(AutomationElement.NameProperty, "引用 No.51288554");
                for (var attempt = 0; attempt < 600 && root.FindFirst(TreeScope.Descendants, originalCondition) is null; attempt++) Thread.Sleep(50);
                if (root.FindFirst(TreeScope.Descendants, originalCondition) is null) throw new InvalidOperationException("真实 tips 验证串未加载。");
                var list = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "串正文与回复"))!;
                ((ScrollPattern)list.GetCurrentPattern(ScrollPattern.Pattern)).SetScrollPercent(ScrollPattern.NoScroll, 100);
                var tipCondition = new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "X 岛提示"), new PropertyCondition(AutomationElement.IsOffscreenProperty, false));
                for (var attempt = 0; attempt < 100 && list.FindFirst(TreeScope.Descendants, tipCondition) is null; attempt++) Thread.Sleep(50);
                if (list.FindFirst(TreeScope.Descendants, tipCondition) is null) throw new InvalidOperationException("最终 EXE 未显示实际 API 下发的 tips。");
                // 触底会续载，图片也可能随后撑开楼层；重新定位提示所在行后再留截图证据。
                Thread.Sleep(1000);
                var tipLabel = list.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "X 岛提示"))!;
                var tipRow = tipLabel;
                while (tipRow is not null && tipRow.Current.ControlType != ControlType.ListItem)
                    tipRow = TreeWalker.ControlViewWalker.GetParent(tipRow);
                if (tipRow is null || !tipRow.TryGetCurrentPattern(ScrollItemPattern.Pattern, out var tipScroll))
                    throw new InvalidOperationException("无法定位真实提示所在行。");
                ((ScrollItemPattern)tipScroll).ScrollIntoView();
                Thread.Sleep(700);
                ((ScrollItemPattern)tipScroll).ScrollIntoView();
                Thread.Sleep(150);
                if (list.FindFirst(TreeScope.Descendants, tipCondition) is null)
                    throw new InvalidOperationException("提示在布局稳定后不在可见区域。");
                if (list.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "引用 No.9999999"), new PropertyCondition(AutomationElement.IsOffscreenProperty, false))) is not null)
                    throw new InvalidOperationException("真实 tips 仍可引用伪楼号。");
                Capture(mainHandle, Path.Combine(output, "tips-live.png"));
            }
            // 发串现在属于版块/时间线内容栏。主动选择入口，不依赖启动网络请求的完成时刻。
            InvokePublishedButton(root, AutomationElement.AutomationIdProperty, "HomeTimelineButton");
            for (var attempt = 0; attempt < 100 && root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "NewThreadButton"), new PropertyCondition(AutomationElement.IsOffscreenProperty, false))) is null; attempt++) Thread.Sleep(50);
            InvokePublishedButton(root, AutomationElement.AutomationIdProperty, "NewThreadButton");
            var composer = WaitPublishedWindow(process.Id, "发串");
            children["new-thread"] = InspectPublishedChild(composer, output, "new-thread");
            InvokePublishedButton(composer, AutomationElement.NameProperty, "颜文字");
            AutomationElement? emoticon = null;
            // WPF Popup 是独立 HWND，在本次测试进程的窗口中定位按钮。
            for (var attempt = 0; attempt < 50 && emoticon is null; attempt++)
            {
                var surfaces = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
                foreach (AutomationElement surface in surfaces)
                {
                    emoticon = surface.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "(=ﾟωﾟ)="), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
                    if (emoticon is not null && !emoticon.Current.IsOffscreen) break;
                    emoticon = null;
                }
                if (emoticon is null) Thread.Sleep(30);
            }
            if (emoticon is null) throw new InvalidOperationException("交付程序没有可见的颜文字下拉选项。");
            ((InvokePattern)emoticon.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            // 等待弹出层关闭及正文重新可访问再读插入结果。
            var insertedEmoticon = false;
            for (var attempt = 0; attempt < 100 && !insertedEmoticon; attempt++)
            {
                var postContent = composer.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "发言正文"));
                insertedEmoticon = postContent is not null && ((ValuePattern)postContent.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "(=ﾟωﾟ)=";
                if (!insertedEmoticon) Thread.Sleep(30);
            }
            if (!insertedEmoticon) throw new InvalidOperationException("最终 EXE 没有把颜文字插入正文。");
            // 单文件发布也要目视核对内嵌字体，防止源码验证通过但打包漏掉资源。
            var fontInput = composer.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "发言正文"))!;
            const string emojiDraft = "(`ᝫ´ )\r\n/̵͇̿̿/’̿’̿ ̿ ̿̿ ̿̿ ̿̿\r\n( ﾟᯅ 。)\r\n😀 ❤️ 👍🏽 👨‍👩‍👧‍👦 🇨🇳 1️⃣\r\n↙(`ヮ´ )↗ 开摆！\r\n中文输入 🫠 🌈\r\n\r\n";
            ((ValuePattern)fontInput.GetCurrentPattern(ValuePattern.Pattern)).SetValue(emojiDraft);
            Thread.Sleep(200); Capture(new IntPtr(composer.Current.NativeWindowHandle), Path.Combine(output, "emoticon-fonts.png"));
            InvokePublishedButton(composer, AutomationElement.NameProperty, "管理饼干");
            var cookies = WaitPublishedWindow(process.Id, "饼干管理");
            children["cookies"] = InspectPublishedChild(cookies, output, "cookies");
            foreach (var name in new[] { "选择二维码图片", "粘贴二维码", "导入并使用" })
            {
                var entry = cookies.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)));
                if (entry is null || entry.Current.IsOffscreen) throw new InvalidOperationException("交付程序饼干导入入口不可见：" + name);
            }
            var cookieName = cookies.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "饼干名称"));
            if (cookieName is null || cookieName.Current.IsOffscreen || cookieName.Current.ControlType != ControlType.Text)
                throw new InvalidOperationException("交付程序的饼干名称不是可见的只读文本。");
            if (cookies.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "饼干备注名称")) is not null)
                throw new InvalidOperationException("交付程序仍包含可编辑的饼干备注。");
            ClosePublishedChild(cookies, composer);
            ClosePublishedChild(composer, root);
            if (!profile.Load().Drafts.Any(d => d.Content == emojiDraft)) throw new InvalidOperationException("最终 EXE 没有完整保存 emoji 与尾部空行。");
            InvokePublishedButton(root, AutomationElement.AutomationIdProperty, "NewThreadButton");
            composer = WaitPublishedWindow(process.Id, "发串");
            var restoredEditor = composer.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "发言正文"))!;
            if (((ValuePattern)restoredEditor.GetCurrentPattern(ValuePattern.Pattern)).Current.Value != emojiDraft)
                throw new InvalidOperationException("最终 EXE 重新打开草稿时 emoji 或换行丢失。");
            ClosePublishedChild(composer, root);
            InvokePublishedButton(root, AutomationElement.NameProperty, "设置与关于");
            var settings = WaitPublishedWindow(process.Id, "设置");
            children["settings"] = InspectPublishedChild(settings, output, "settings");
            var spoilers = settings.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "启用防剧透"))
                ?? throw new InvalidOperationException("交付程序未包含防剧透选项。");
            var spoilerToggle = (TogglePattern)spoilers.GetCurrentPattern(TogglePattern.Pattern);
            if (spoilers.Current.IsOffscreen || spoilerToggle.Current.ToggleState != ToggleState.Off)
                throw new InvalidOperationException("防剧透选项不在首屏或没有默认关闭。");
            spoilerToggle.Toggle();
            for (var attempt = 0; attempt < 100 && !profile.Load().HideSpoilers; attempt++) Thread.Sleep(50);
            if (!profile.Load().HideSpoilers) throw new InvalidOperationException("交付程序没有保存防剧透开启设置。");
            spoilerToggle.Toggle();
            for (var attempt = 0; attempt < 100 && profile.Load().HideSpoilers; attempt++) Thread.Sleep(50);
            if (profile.Load().HideSpoilers) throw new InvalidOperationException("交付程序没有保存防剧透关闭设置。");
            // 最终单文件也验证本轮开关，避免只测到测试工程中的源码版本。
            var inertia = settings.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "惯性滚动"))
                ?? throw new InvalidOperationException("交付程序未包含惯性滚动选项。");
            if (inertia.Current.IsOffscreen) throw new InvalidOperationException("惯性滚动选项不在设置首屏。");
            var inertiaToggle = (TogglePattern)inertia.GetCurrentPattern(TogglePattern.Pattern);
            if (inertiaToggle.Current.ToggleState != ToggleState.On) throw new InvalidOperationException("惯性滚动未默认开启。");
            inertiaToggle.Toggle();
            for (var attempt = 0; attempt < 100 && profile.Load().InertialScrolling; attempt++) Thread.Sleep(50);
            if (profile.Load().InertialScrolling) throw new InvalidOperationException("交付程序没有保存惯性关闭设置。");
            inertiaToggle.Toggle();
            for (var attempt = 0; attempt < 100 && !profile.Load().InertialScrolling; attempt++) Thread.Sleep(50);
            if (!profile.Load().InertialScrolling) throw new InvalidOperationException("交付程序没有保存惯性开启设置。");
            InvokePublishedButton(settings, AutomationElement.NameProperty, "管理订阅号");
            var subscriptions = WaitPublishedWindow(process.Id, "订阅号管理");
            children["subscriptions"] = InspectPublishedChild(subscriptions, output, "subscriptions");
            var numberInput = subscriptions.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, "订阅号"), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
                ?? throw new InvalidOperationException("交付程序的明文订阅号输入框不可访问。");
            if (numberInput.Current.IsPassword || string.IsNullOrEmpty(((ValuePattern)numberInput.GetCurrentPattern(ValuePattern.Pattern)).Current.Value))
                throw new InvalidOperationException("交付程序的已保存订阅号不可见。");
            ClosePublishedChild(subscriptions, settings);
            ClosePublishedChild(settings, root);
            var star = root.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "取消收藏版块：综合线"),
                new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty, true)))
                ?? throw new InvalidOperationException("交付程序没有显示可切换的版块收藏星标。");
            var starToggle = (TogglePattern)star.GetCurrentPattern(TogglePattern.Pattern);
            if (starToggle.Current.ToggleState != ToggleState.On) throw new InvalidOperationException("交付程序星标没有恢复收藏状态。");
            starToggle.Toggle();
            for (var attempt = 0; attempt < 100 && profile.Load().FavoriteForums.Any(f => f.Key == "t1"); attempt++) Thread.Sleep(50);
            if (profile.Load().FavoriteForums.Any(f => f.Key == "t1")) throw new InvalidOperationException("交付程序星标取消没有保存。");
            // 文件写入完成早于 WPF 布局和 DWM 呈现；先确认快捷项已经移除，再截取更新后的画面。
            var removedStarCondition = new AndCondition(
                new PropertyCondition(AutomationElement.NameProperty, "取消收藏版块：综合线"),
                new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty, true));
            for (var attempt = 0; attempt < 100 && root.FindFirst(TreeScope.Descendants, removedStarCondition) is not null; attempt++) Thread.Sleep(50);
            if (root.FindFirst(TreeScope.Descendants, removedStarCondition) is not null)
                throw new InvalidOperationException("交付程序收藏快捷项未随取消操作移除。");
            Thread.Sleep(250);
            Capture(mainHandle, Path.Combine(output, "favorite-removed.png"));
            var releaseBytes = Directory.EnumerateFiles(Path.GetDirectoryName(start.FileName)!, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            var extractRoot = start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"]!;
            var extractedBytes = Directory.Exists(extractRoot) ? Directory.EnumerateFiles(extractRoot, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
            if (releaseBytes + extractedBytes >= 100_000_000) throw new InvalidOperationException("应用文件加首次展开运行时超过 100 MB。");
            var result = new { Version = version, ReleaseBytes = releaseBytes, ExtractedRuntimeBytes = extractedBytes, ApplicationFootprintBytes = releaseBytes + extractedBytes, EmojiDraftRestored = true, LiveTipsShown = requireTips, Startup = report, Children = children, ModalOwnerRestored = root.Current.IsEnabled,
                ForumFavoriteRestoredAndRemoved = true, InertialScrollingToggleSaved = true, SpoilerProtectionToggleSaved = true, CookieQrImportEntriesVisible = true, EmoticonDropdownAndInsertion = insertedEmoticon, StartupNoticeShown = noticeShown, NoticeSuppressionSaved = noticeShown && profile.Load().SuppressedNoticeKeys.Count > 0 };
            File.WriteAllText(Path.Combine(output, "published-window-results.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(result));
            return report.WindowFits && report.CaptionButtonsVisible ? 0 : 1;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.CloseMainWindow();
                // 只清理本方法创建的隔离测试进程，避免失败的模态检查留下后台实例。
                if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
            }
        }
    }

    private static Rect PublishedCaptionBounds(AutomationElement root)
    {
        var bounds = Rect.Empty;
        foreach (var id in new[] { "TitleBarMinimizeButton", "TitleBarMaximizeButton", "TitleBarCloseButton" })
        {
            var button = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
            if (button is null || button.Current.IsOffscreen || button.Current.BoundingRectangle.Width < 24)
                throw new InvalidOperationException("交付程序自定义标题栏按钮不可见：" + id);
            bounds.Union(button.Current.BoundingRectangle);
        }
        return bounds;
    }

    private static void InvokePublishedButton(AutomationElement root, AutomationProperty property, string value)
    {
        var button = root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(property, value),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
            ?? throw new InvalidOperationException("交付程序按钮未找到：" + value);
        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }

    private static AutomationElement WaitPublishedWindow(int processId, string title)
    {
        // OwnedWindow 在可访问性树中可能挂在父窗口下；先按进程枚举 HWND，再读取其控件。
        var titles = new List<string>();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var found = IntPtr.Zero;
            titles.Clear();
            EnumWindows((handle, _) =>
            {
                GetWindowThreadProcessId(handle, out var ownerId);
                if (ownerId != processId) return true;
                var name = new StringBuilder(256); GetWindowText(handle, name, name.Capacity);
                titles.Add(name.ToString());
                if (name.ToString() == title) found = handle;
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) { Thread.Sleep(200); return AutomationElement.FromHandle(found); }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("等待交付程序子窗口超时：" + title + "；当前窗口：" + string.Join(" / ", titles));
    }

    private static WindowReport InspectPublishedChild(AutomationElement window, string output, string name)
    {
        var handle = new IntPtr(window.Current.NativeWindowHandle);
        var report = Inspect(handle, PublishedCaptionBounds(window));
        Capture(handle, Path.Combine(output, name + ".png"));
        if (!report.WindowFits || !report.CaptionButtonsVisible) throw new InvalidOperationException(name + " 子窗口越界。");
        return report;
    }

    private static void ClosePublishedChild(AutomationElement window, AutomationElement owner)
    {
        InvokePublishedButton(window, AutomationElement.AutomationIdProperty, "TitleBarCloseButton");
        for (var attempt = 0; attempt < 100; attempt++)
        {
            Thread.Sleep(50);
            if (owner.Current.IsEnabled) return;
        }
        throw new InvalidOperationException("交付程序关闭子窗口后没有恢复父窗口交互。");
    }

    private static int CheckSizingScenarios()
    {
        var count = 0;
        foreach (var work in new[] { new Rect(0, 0, 1280, 680), new Rect(0, 0, 1366, 728), new Rect(0, 0, 1920, 1020), new Rect(0, 0, 3840, 2080), new Rect(-1920, 40, 1920, 1040), new Rect(1960, -900, 1240, 860) })
        foreach (var scale in new[] { 1d, 1.25, 1.5, 1.75, 2 })
        {
            var result = WindowPlacement.Calculate(work, scale, new(1280, 800), new(900, 600));
            var b = result.Bounds;
            var pixels = new Rect(b.Left * scale, b.Top * scale, b.Width * scale, b.Height * scale);
            if (!work.Contains(pixels) || result.Minimum.Width > b.Width || result.Minimum.Height > b.Height)
                throw new InvalidOperationException($"工作区 {work}、缩放 {scale} 的尺寸计算越界。");
            count++;
        }
        return count;
    }

    private static ResourceDictionary LoadResources()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"));
        var dictionary = new XElement(presentation + "ResourceDictionary",
            new XAttribute("xmlns", presentation.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"),
            new XAttribute(XNamespace.Xmlns + "local", "clr-namespace:Tide.Desktop;assembly=Tide"),
            new XAttribute(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml"),
            source.Root!.Element(presentation + "Application.Resources")!.Element(presentation + "ResourceDictionary")!.Elements());
        foreach (var element in dictionary.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:Tide.Desktop"))
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:Tide.Desktop;assembly=Tide");
        using var reader = dictionary.CreateReader();
        return (ResourceDictionary)XamlReader.Load(reader);
    }

    internal static void RequireWindowFits(Window window)
    {
        var report = Inspect(window);
        if (!report.WindowFits || !report.CaptionButtonsVisible)
            throw new InvalidOperationException(window.Title + " / " + window.WindowState + " 边界检查失败：" + JsonSerializer.Serialize(report));
    }

    private static WindowReport Inspect(Window window)
    {
        // Fluent 标题栏按钮位于 WPF 内容区；DWM 的旧系统标题栏矩形不能证明它们可见。
        // Refined Reader 弹窗不带 WPF UI 标题栏，只有自定义的 32×32 关闭键；以它作为标题区操作的边界。
        if (window is Wpf.Ui.Controls.FluentWindow && !UiRefinementChecks.Descendants<Wpf.Ui.Controls.TitleBar>(window).Any())
        {
            var close = UiRefinementChecks.Descendants<System.Windows.Controls.Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "关闭");
            if (!close.IsVisible || close.ActualWidth < 32 || close.ActualHeight < 32) throw new InvalidOperationException("弹窗关闭键不可见或尺寸不足。");
            var closeBounds = new Rect(close.PointToScreen(new Point()), close.PointToScreen(new Point(close.ActualWidth, close.ActualHeight)));
            return Inspect(new WindowInteropHelper(window).Handle, closeBounds);
        }
        if (window is Wpf.Ui.Controls.FluentWindow)
        {
            var title = UiRefinementChecks.Descendants<Wpf.Ui.Controls.TitleBar>(window).Single();
            var bounds = Rect.Empty;
            foreach (var name in new[] { "PART_MinimizeButton", "PART_MaximizeButton", "PART_CloseButton" })
            {
                var button = (FrameworkElement)title.Template.FindName(name, title);
                if (!button.IsVisible || button.ActualWidth < 24 || button.ActualHeight < 24)
                    throw new InvalidOperationException("自定义标题栏按钮不可见或尺寸不足。");
                bounds.Union(new Rect(button.PointToScreen(new Point()), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight))));
            }
            Rect? contentFrame = null;
            if (window.WindowState == WindowState.Maximized && window.Content is FrameworkElement content)
                contentFrame = new Rect(content.PointToScreen(new Point()), content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight)));
            return Inspect(new WindowInteropHelper(window).Handle, bounds, contentFrame);
        }
        return Inspect(new WindowInteropHelper(window).Handle);
    }

    private static WindowReport Inspect(IntPtr handle, Rect? customCaption = null, Rect? contentFrame = null)
    {
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("读取窗口边界失败。");
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) throw new InvalidOperationException("读取显示器工作区失败。");
        NativeRect buttons;
        if (customCaption is Rect custom)
            buttons = new NativeRect { Left = (int)custom.Left, Top = (int)custom.Top, Right = (int)Math.Ceiling(custom.Right), Bottom = (int)Math.Ceiling(custom.Bottom) };
        else
        {
            if (DwmGetWindowAttribute(handle, 5, out var caption, Marshal.SizeOf<NativeRect>()) != 0 || caption.Right <= caption.Left)
                throw new InvalidOperationException("读取原生标题栏按钮位置失败。");
            buttons = new NativeRect { Left = bounds.Left + caption.Left, Top = bounds.Top + caption.Top, Right = bounds.Left + caption.Right, Bottom = bounds.Top + caption.Bottom };
        }
        // FluentWindow 最大化时 DWM 也可能报告屏幕外的缩放边框；使用实际客户内容边界判断是否裁切。
        if (DwmGetWindowAttribute(handle, 9, out var frame, Marshal.SizeOf<NativeRect>()) != 0)
            throw new InvalidOperationException("读取可见窗口边界失败。");
        if (contentFrame is Rect contentBounds)
            frame = new NativeRect { Left = (int)Math.Round(contentBounds.Left), Top = (int)Math.Round(contentBounds.Top), Right = (int)Math.Round(contentBounds.Right), Bottom = (int)Math.Round(contentBounds.Bottom) };
        return new(GetDpiForWindow(handle), bounds.Value, frame.Value, monitor.Work.Value, buttons.Value,
            contentFrame is null ? "DWM" : "WPF content (maximized)",
            monitor.Work.Contains(frame), buttons.Right > buttons.Left && buttons.Bottom > buttons.Top && monitor.Work.Contains(buttons) && frame.Contains(buttons));
    }

    // PrintWindow 包含原生标题栏，区别于仅渲染 WPF 内容区域的截图。
    private static void Capture(Window window, string path)
    {
        // 隔离桌面中的 PrintWindow 可能成功却返回白图；进程内检查直接渲染 WPF 可视树。
        // 最终 EXE 的跨进程窗口检查仍使用下面的原生截图路径。
        window.UpdateLayout();
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX),
            (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }

    private static void Capture(IntPtr handle, string path)
    {
        if (!GetWindowRect(handle, out var bounds)) throw new InvalidOperationException("读取截图尺寸失败。");
        var dc = GetDC(handle);
        var memory = CreateCompatibleDC(dc);
        var bitmap = CreateCompatibleBitmap(dc, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!PrintWindow(handle, memory, 2)) throw new InvalidOperationException("窗口截图失败。");
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(path); png.Save(file);
        }
        finally { SelectObject(memory, previous); DeleteObject(bitmap); DeleteDC(memory); ReleaseDC(handle, dc); }
    }

    private sealed record Bounds(int Left, int Top, int Right, int Bottom);
    private sealed record WindowReport(uint Dpi, Bounds Window, Bounds VisibleFrame, Bounds WorkArea, Bounds CaptionButtons, string FrameSource, bool WindowFits, bool CaptionButtonsVisible);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Bounds Value => new(Left, Top, Right, Bottom);
        public readonly bool Contains(NativeRect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
        public readonly NativeRect Intersect(NativeRect other) => new() { Left = Math.Max(Left, other.Left), Top = Math.Max(Top, other.Top), Right = Math.Min(Right, other.Right), Bottom = Math.Min(Bottom, other.Bottom) };
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder title, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out NativeRect rect, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr value);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(IntPtr dc);
}

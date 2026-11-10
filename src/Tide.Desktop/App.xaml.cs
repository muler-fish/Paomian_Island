using System.IO;
using System.Windows;
using System.Windows.Media;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>程序入口：确定资料目录、限制重复实例、组装服务并创建主窗口。</summary>
public partial class App : Application
{
    private Mutex? _instance;
    private bool _ownsInstance;
    private UpdateController? _updates;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string? Value(string flag) { var i = Array.IndexOf(e.Args, flag); return i >= 0 && i + 1 < e.Args.Length ? e.Args[i + 1] : null; }
        if (Value("--install-update") is { } plan)
        { ShutdownMode = ShutdownMode.OnExplicitShutdown; await UpdateController.RunInstallerAsync(plan); Shutdown(); return; }
        // 显式指定的数据目录优先；验证模式默认使用输出目录下的 profile，日常运行放在 LocalAppData。
        var smoke = Value("--smoke-dir");
        var root = Value("--data-dir") ?? (smoke is not null ? Path.Combine(Path.GetFullPath(smoke), "profile") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppIdentity.DataDirectoryName));
        try
        {
            // 用完整资料路径生成互斥名称：同一目录只能有一个实例，不同资料目录可分别运行。
            var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..24];
            _instance = new Mutex(true, AppIdentity.MutexPrefix + key, out _ownsInstance);
            if (!_ownsInstance)
            {
                MessageBox.Show($"{AppIdentity.DisplayName}已经在运行，请从任务栏打开。", AppIdentity.DisplayName); Shutdown(); return;
            }
            var vm = new MainViewModel(new XIslandApi(), new LocalStore(root));
            // 先应用已保存主题和个性化颜色再显示窗口，避免启动时先闪出错误的颜色。
            Personalization.Load(vm.State); ApplyTheme(vm.State.Theme);
            _updates = vm.Updates;
            var window = new MainWindow(vm); MainWindow = window;
            if (smoke is not null) window.ShowInTaskbar = false;
            window.Show();
            // 验证入口执行真实只读请求和截图；正常启动独立并行获取通知，读串维护不影响公告。
            if (smoke is not null) { await SmokeRunner.RunAsync(window, vm, smoke); Shutdown(); }
            else
            {
                using var notices = new SiteNoticeClient();
                using var presenter = new StartupNoticePresenter(window, vm, notices);
                _ = vm.Updates.CleanupAsync();
                await Task.WhenAll(vm.InitializeAsync(), presenter.RunAsync(), vm.State.AutoCheckUpdates ? vm.Updates.CheckAsync(automatic: true) : Task.CompletedTask);
            }
        }
        catch (Exception ex)
        {
            if (smoke is not null) { Directory.CreateDirectory(smoke); File.WriteAllText(Path.Combine(smoke, "failure.txt"), ex.ToString()); }
            else MessageBox.Show("启动未完成：" + ex.Message, AppIdentity.DisplayName, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _updates?.Exit();
        if (_ownsInstance) _instance?.ReleaseMutex();
        _instance?.Dispose(); base.OnExit(e);
    }

    internal static void ApplySpoilerProtection(bool enabled)
    {
        // 仅真正切换时重绘；修改字号或主题不收起已经展开的内容。
        if (Current is not null && !Equals(Current.Resources["HideSpoilers"], enabled))
            Current.Resources["HideSpoilers"] = enabled;
    }

    /// <summary>强调色或标识颜色改变后立即生效；高对比度模式沿用系统色，不覆盖。</summary>
    internal static void ApplyPersonalization(string theme)
    {
        if (SystemParameters.HighContrast) return;
        var dark = theme == "dark";
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(Personalization.AccentFor(dark), dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light, false);
        ReaderTheme.Refresh(dark);
    }

    public static void ApplyTheme(string theme)
    {
        // 同步 WPF UI 控件主题和阅读区语义画刷，避免深色窗口中出现浅色输入框。
        var dark = theme == "dark";
        var libraryTheme = SystemParameters.HighContrast ? Wpf.Ui.Appearance.ApplicationTheme.HighContrast
            : dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light;
        Wpf.Ui.Appearance.ApplicationThemeManager.Apply(libraryTheme, Wpf.Ui.Controls.WindowBackdropType.None, false);
        Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(Personalization.AccentFor(dark), libraryTheme, false);
        ReaderTheme.Apply(dark);
        // 高对比度沿用系统色；饼干 ID、SAGE 文本和 PO 标签继续承担状态区分。
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "Canvas", "Surface", "Sidebar", "AccentSoft", "DangerSoft", "PoBackground", "CookieBackground", "AdminCookieBackground", "SageBackground" }) Current.Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Text", "Muted", "Accent", "Danger", "Line", "FavoriteStar", "PoForeground", "CookieForeground", "AdminCookieForeground", "OnSage", "PostGreen", "PostRed" }) Current.Resources[key] = SystemColors.WindowTextBrush;
            Current.Resources["Hover"] = SystemColors.ControlBrush;
            foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed" })
                Current.Resources[key] = SystemColors.HighlightBrush;
            foreach (var key in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
                Current.Resources[key] = SystemColors.HighlightTextBrush;
        }
    }
}

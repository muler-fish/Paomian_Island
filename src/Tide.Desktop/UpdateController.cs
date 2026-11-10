using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using Tide.Core;

namespace Tide.Desktop;

internal sealed class UpdateController(MainViewModel vm)
{
    internal static string Version => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    private readonly ReleaseUpdater _updater = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DateTime _created = DateTime.UtcNow;
    internal event Action? Changed;
    internal string Status { get; private set; } = "尚未检查更新";
    internal bool Busy { get; private set; }
    internal AppRelease? Available { get; private set; }
    internal StagedUpdate? Staged { get; private set; }
    private string Root => Path.Combine(vm.Store.Root, "updates");
    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    internal async Task CheckAsync(bool automatic = false)
    {
        if (Busy || Staged is not null) return;
        Busy = true; SetStatus("正在检查更新…");
        try
        {
            Available = await _updater.CheckAsync(Version, _lifetime.Token);
            SetStatus(Available is null ? $"已是最新版本 · {DateTime.Now:HH:mm} 检查" : $"发现新版本 {Available.Version}");
        }
        catch (Exception ex) { SetStatus("检查失败：" + Friendly(ex)); }
        finally { Busy = false; Changed?.Invoke(); }
        if (automatic && Available is not null && vm.State.InstallUpdatesOnExit && !_lifetime.IsCancellationRequested) await DownloadAsync();
    }
    internal async Task DownloadAsync()
    {
        if (Busy || Available is null || Staged is not null) return;
        Busy = true; SetStatus("正在下载更新…");
        try
        {
            var progress = new Progress<int>(value => SetStatus($"正在下载更新 · {value}%"));
            Staged = await _updater.DownloadAsync(Available, Root, progress, _lifetime.Token);
            SetStatus("更新已准备好，可重启安装" + (vm.State.InstallUpdatesOnExit ? "，或在退出时安装" : ""));
        }
        catch (Exception ex) { SetStatus("下载失败：" + Friendly(ex)); }
        finally { Busy = false; Changed?.Invoke(); }
    }
    internal void RestartToInstall()
    {
        if (Staged is null) return;
        try { LaunchInstaller(true); Application.Current.Shutdown(); }
        catch (Exception ex) { SetStatus("无法安装：" + Friendly(ex)); }
    }
    private bool _installerStarted;
    private void LaunchInstaller(bool restart)
    {
        if (_installerStarted || Staged is null) return;
        var plan = UpdateInstaller.Prepare(Staged, vm.Store.Root, Environment.ProcessPath!, restart);
        var start = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(plan)!, AppIdentity.UpdaterExecutableName)) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--install-update"); start.ArgumentList.Add(plan);
        _ = Process.Start(start) ?? throw new IOException("无法启动更新程序。");
        _installerStarted = true;
    }
    internal void Exit()
    {
        _lifetime.Cancel();
        if (vm.State.InstallUpdatesOnExit && Staged is not null && !_installerStarted)
        {
            try { LaunchInstaller(false); }
            catch (Exception ex) { MessageBox.Show("更新尚未安装，原程序保留。\n" + Friendly(ex), "更新"); }
        }
        _updater.Dispose();
        if (Staged is not null && !_installerStarted)
            try { ReleaseUpdater.DeleteStage(Path.GetDirectoryName(Staged.Directory)!, Root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    internal static async Task RunInstallerAsync(string planPath)
    {
        var stage = Path.GetDirectoryName(Path.GetFullPath(planPath))!;
        try
        {
            var plan = UpdateInstaller.ReadPlan(planPath);
            if (!string.Equals(Environment.ProcessPath, Path.Combine(stage, AppIdentity.UpdaterExecutableName), StringComparison.OrdinalIgnoreCase)) throw new IOException("更新程序路径不匹配。");
            try
            {
                using var parent = Process.GetProcessById(plan.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks)
                {
                    using var wait = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                    await parent.WaitForExitAsync(wait.Token);
                }
            }
            catch (ArgumentException) { }
            await Task.Run(() => UpdateInstaller.Apply(planPath, plan));
            File.WriteAllText(Path.Combine(stage, "result.txt"), "success");
            if (plan.Restart)
            {
                var start = new ProcessStartInfo(plan.Target) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
                start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(plan.Profile);
                Process.Start(start);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("自动更新未完成，原版本和个人资料已保留。\n" + Friendly(ex), "更新失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    internal async Task CleanupAsync()
    {
        // A restarted app can briefly overlap the exiting helper; skip locked stages and try next launch.
        await Task.Delay(2000);
        if (!Directory.Exists(Root)) return;
        await Task.Run(() =>
        {
            try
            {
                DiskCache.NoLinks(Root);
                foreach (var dir in Directory.EnumerateDirectories(Root))
                {
                    try
                    {
                        if (Directory.GetCreationTimeUtc(dir) >= _created) continue;
                        if (Directory.Exists(Path.Combine(dir, "backup")) && !File.Exists(Path.Combine(dir, "result.txt"))) continue;
                        var helper = Path.Combine(dir, AppIdentity.UpdaterExecutableName);
                        if (File.Exists(helper)) { using var probe = File.Open(helper, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                        ReleaseUpdater.DeleteStage(dir, Root);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }
    private static string Friendly(Exception ex) => ex is OperationCanceledException ? "请求已取消或超时，可稍后重试。" : ex.Message;
}

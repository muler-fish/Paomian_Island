using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using Paomian.Core;

namespace Paomian.Desktop.Tests;

internal static partial class Program
{
    private static void CheckPublishedUpdateNotice(string oldExecutable, string newExecutable, string output)
    {
        // 每个进程的网络均指向仅接收回环连接的拒绝端点，操作仅针对本次创建的进程。
        using var proxy = new TcpListener(IPAddress.Loopback, 0);
        proxy.Start();
        using var stop = new CancellationTokenSource();
        var proxyLoop = RejectAsync();
        var run = Directory.CreateDirectory(Path.Combine(output, Guid.NewGuid().ToString("N"))).FullName;
        var clean = Path.Combine(run, "clean");
        var upgraded = Path.Combine(run, "upgraded");
        try
        {
            Launch(newExecutable, clean, "fresh");
            Launch(oldExecutable, upgraded, "legacy");
            Launch(newExecutable, upgraded, "interrupt");
            Launch(newExecutable, upgraded, "acknowledge");
            Launch(oldExecutable, upgraded, "rollback");
            Launch(newExecutable, upgraded, "confirmed");
            CheckInstalledUpdate(oldExecutable, newExecutable, run, StartIsolated, Launch);
            File.WriteAllText(Path.Combine(output, "result.txt"),
                "PASS: empty profile, legacy profile, manual upgrade, interrupted dialog, acknowledgement, rollback, real installer replacement and restart using both packaged executables.");
        }
        finally { stop.Cancel(); proxyLoop.GetAwaiter().GetResult(); }

        void Launch(string executable, string profile, string scenario)
        {
            using var process = StartIsolated(executable, ["--data-dir", profile]);
            try
            {
                var main = WaitPublishedWindow(process.Id, AppIdentity.WindowTitle);
                if (scenario is "interrupt" or "acknowledge")
                {
                    var dialog = WaitPublishedWindow(process.Id, "更新内容");
                    if (scenario == "interrupt") { process.Kill(); process.WaitForExit(); return; }
                    AcknowledgePublishedDialog(dialog, main);
                }
                else
                {
                    if (scenario is "fresh" or "confirmed")
                    {
                        if (!SpinWait.SpinUntil(() => File.Exists(Path.Combine(profile, "update-notice.json")), 5000))
                            throw new IOException("启动未建立通知记录");
                    }
                    Thread.Sleep(750);
                    if (!main.Current.IsEnabled) throw new IOException(scenario + " 意外显示了模态窗口");
                }
                ClosePublishedMain(process, main);
            }
            finally { StopPublishedProcess(process); }
        }

        Process StartIsolated(string executable, string[] arguments)
        {
            var start = new ProcessStartInfo(Path.GetFullPath(executable)) { UseShellExecute = false };
            var address = $"http://127.0.0.1:{((IPEndPoint)proxy.LocalEndpoint).Port}";
            foreach (var key in new[] { "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY" }) start.Environment[key] = address;
            start.Environment["NO_PROXY"] = "";
            foreach (var arg in arguments) start.ArgumentList.Add(arg);
            return Process.Start(start) ?? throw new IOException("无法启动验收程序");
        }
        async Task RejectAsync()
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var client = await proxy.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                    try { await client.GetStream().WriteAsync("HTTP/1.1 503 Service Unavailable\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"u8.ToArray(), stop.Token).ConfigureAwait(false); }
                    catch (IOException) { }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        }
    }

    private static void AcknowledgePublishedDialog(AutomationElement dialog, AutomationElement main)
    {
        InvokePublishedButton(dialog, AutomationElement.NameProperty, "知道了");
        if (!SpinWait.SpinUntil(() => main.Current.IsEnabled, 5000)) throw new IOException("确认后主窗未恢复");
    }

    private static void ClosePublishedMain(Process process, AutomationElement main)
    {
        ((WindowPattern)main.GetCurrentPattern(WindowPattern.Pattern)).Close();
        if (!process.WaitForExit(10000) || process.ExitCode != 0) throw new IOException("验收进程退出失败");
    }

    private static void StopPublishedProcess(Process process)
    {
        if (!process.HasExited) { process.Kill(); process.WaitForExit(); }
    }

    private static void CheckInstalledUpdate(string oldExecutable, string newExecutable, string run,
        Func<string, string[], Process> startIsolated, Action<string, string, string> launch)
    {
        var install = Directory.CreateDirectory(Path.Combine(run, "install")).FullName;
        var profile = Path.Combine(run, "installer-profile");
        var target = Path.Combine(install, AppIdentity.ExecutableName);
        File.Copy(oldExecutable, target);
        using var old = startIsolated(target, ["--data-dir", profile]);
        var oldClosed = false;
        try
        {
            var oldWindow = WaitPublishedHandle(old.Id, AppIdentity.WindowTitle);
            var stage = Path.Combine(profile, "updates", Guid.NewGuid().ToString("N"));
            var package = Directory.CreateDirectory(Path.Combine(stage, "package")).FullName;
            var source = Path.GetDirectoryName(Path.GetFullPath(newExecutable))!;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(package, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination);
            }
            var newHash = ReleaseUpdater.Hash(newExecutable);
            var planPath = UpdateInstaller.Prepare(new(package, newHash), profile, target, restart: true);
            // Prepare 从测试驱动调用，安装助手应等待隔离安装目录中实际运行的旧客户端。
            var plan = UpdateInstaller.ReadPlan(planPath) with { ParentId = old.Id, ParentStartTicks = old.StartTime.ToUniversalTime().Ticks };
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
            var helperPath = Path.Combine(stage, AppIdentity.UpdaterExecutableName);
            if (ReleaseUpdater.Hash(helperPath) != ReleaseUpdater.Hash(oldExecutable)) throw new IOException("助手不是旧版程序");
            if (!PublishedColorChecks.PostMessage(oldWindow, 0x0010, IntPtr.Zero, IntPtr.Zero) ||
                !old.WaitForExit(10000) || old.ExitCode != 0)
                throw new IOException("旧客户端退出失败");
            old.Close();
            oldClosed = true;
            // 确认 Windows 已释放原 EXE，再交给真实旧版助手替换。
            if (!SpinWait.SpinUntil(() =>
                {
                    try { using var probe = File.Open(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
                    catch (IOException) { return false; }
                    catch (UnauthorizedAccessException) { return false; }
                }, 10000))
                throw new IOException("旧客户端退出后未释放程序文件");
            using var helper = startIsolated(helperPath, ["--install-update", planPath]);
            try
            {
                if (!helper.WaitForExit(25000))
                {
                    var failure = WaitPublishedWindow(helper.Id, "更新失败");
                    var detail = failure.FindAll(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                        .Cast<AutomationElement>().Select(element => element.Current.Name);
                    throw new IOException("安装助手未完成重启。" + string.Join(" ", detail));
                }
                if (helper.ExitCode != 0) throw new IOException($"安装助手退出码 {helper.ExitCode}");
                using var restarted = FindPublishedProcess(target);
                try
                {
                    var main = WaitPublishedWindow(restarted.Id, AppIdentity.WindowTitle);
                    if (main.Current.IsEnabled)
                    {
                        var handle = new IntPtr(main.Current.NativeWindowHandle);
                        ShowWindow(handle, 9);
                        PublishedColorChecks.Foreground(handle);
                    }
                    var dialog = WaitPublishedWindow(restarted.Id, "更新内容");
                    var sourceVersion = FileVersionInfo.GetVersionInfo(oldExecutable).ProductVersion!.Split('+')[0];
                    var targetVersion = FileVersionInfo.GetVersionInfo(newExecutable).ProductVersion!.Split('+')[0];
                    var stateFile = Path.Combine(profile, "update-notice.json");
                    using var state = JsonDocument.Parse(File.ReadAllText(stateFile));
                    var pending = state.RootElement.GetProperty("Pending");
                    if (pending.GetProperty("Source").GetString() != sourceVersion ||
                        pending.GetProperty("Target").GetString() != targetVersion || ReleaseUpdater.Hash(target) != newHash)
                        throw new IOException("助手重启后安装文件或通知版本范围不符");
                    AcknowledgePublishedDialog(dialog, main);
                    ClosePublishedMain(restarted, main);
                    launch(target, profile, "confirmed");
                    using var confirmed = JsonDocument.Parse(File.ReadAllText(stateFile));
                    if (confirmed.RootElement.GetProperty("Pending").ValueKind != JsonValueKind.Null ||
                        confirmed.RootElement.GetProperty("LastAcknowledged").GetString() != targetVersion)
                        throw new IOException("重启后安装通知确认状态不符");
                    File.WriteAllText(Path.Combine(run, "installer-result.txt"),
                        $"PASS {sourceVersion} -> {targetVersion}; restarted with isolated profile; notification acknowledged; installed SHA-256 {newHash}");
                }
                finally { StopPublishedProcess(restarted); }
            }
            finally { StopPublishedProcess(helper); }
        }
        finally { if (!oldClosed) StopPublishedProcess(old); }
    }

    private static Process FindPublishedProcess(string executable)
    {
        // 助手生成的子进程没有句柄传给测试驱动，仅匹配本次新建安装目录中的程序。
        foreach (var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (string.Equals(candidate.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                {
                    // 在进程仍在运行时取得句柄，退出后也可核验这个非测试驱动直接启动的进程的退出码。
                    _ = candidate.SafeHandle;
                    return candidate;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            candidate.Dispose();
        }
        throw new IOException("没有找到安装助手重启的新进程");
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
}

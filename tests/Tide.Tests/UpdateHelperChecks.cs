using System.Diagnostics;
using System.Text.Json;
using Tide.Core;

internal static class UpdateHelperChecks
{
    internal static async Task RunAsync(string executable)
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "preview7-update-helper", Guid.NewGuid().ToString("N")));
        var profile = Path.Combine(root, "profile"); var install = Path.Combine(root, "install");
        var stage = Path.Combine(profile, "updates", Guid.NewGuid().ToString("N")); var package = Path.Combine(stage, "package");
        Directory.CreateDirectory(package); Directory.CreateDirectory(install);
        var target = Path.Combine(install, "Tide.exe"); File.Copy(executable, target); File.Copy(executable, Path.Combine(package, "Tide.exe"));
        var hash = ReleaseUpdater.Hash(executable); File.WriteAllText(Path.Combine(package, "SHA256SUMS.txt"), hash + "  Tide.exe\n");
        File.WriteAllText(Path.Combine(install, "SHA256SUMS.txt"), "old fixture checksum");
        File.WriteAllText(Path.Combine(profile, "personal.txt"), "keep");
        var planPath = UpdateInstaller.Prepare(new(package, hash), profile, target, false);
        var parentStart = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        parentStart.ArgumentList.Add("--update-parent");
        using var parent = Process.Start(parentStart)!;
        var plan = UpdateInstaller.ReadPlan(planPath) with { ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks };
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        var start = new ProcessStartInfo(Path.Combine(stage, "TideUpdater.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--install-update"); start.ArgumentList.Add(planPath);
        using var updater = Process.Start(start)!;
        await Task.Delay(200);
        if (!parent.HasExited && File.ReadAllText(Path.Combine(install, "SHA256SUMS.txt")) != "old fixture checksum") throw new Exception("installer ignored running parent");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await updater.WaitForExitAsync(timeout.Token); }
        catch { if (!updater.HasExited) updater.Kill(); throw; }
        if (updater.ExitCode != 0 || !File.Exists(Path.Combine(stage, "result.txt")) || ReleaseUpdater.Hash(target) != hash || File.ReadAllText(Path.Combine(profile, "personal.txt")) != "keep")
            throw new Exception("published update helper failed");
        Console.WriteLine("PASS published helper waits for parent, applies verified package, preserves personal data");
        ReleaseUpdater.DeleteStage(stage, Path.Combine(profile, "updates"));
    }
}

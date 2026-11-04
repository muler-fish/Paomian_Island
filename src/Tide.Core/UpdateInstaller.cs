using System.Text.Json;

namespace Tide.Core;

public sealed record UpdatePlan(string Profile, string Target, string OldHash, Dictionary<string, string> Files, int ParentId, long ParentStartTicks, bool Restart);

public static class UpdateInstaller
{
    public static string Prepare(StagedUpdate update, string profile, string target, bool restart)
    {
        profile = Path.GetFullPath(profile); target = Path.GetFullPath(target);
        var stage = Path.GetDirectoryName(Path.GetFullPath(update.Directory))!;
        ValidateLocation(stage, profile);
        var targetName = Path.GetFileName(target);
        if (!AppIdentity.IsExecutableName(targetName)) throw new IOException("自动更新仅支持正式发布的可执行文件。");
        DiskCache.NoLinks(target);
        var files = new Dictionary<string, string>();
        foreach (var file in Directory.EnumerateFiles(update.Directory, "*", SearchOption.AllDirectories))
        {
            DiskCache.NoLinks(file); var name = Path.GetRelativePath(update.Directory, file).Replace('\\', '/');
            if (!ReleaseUpdater.AllowedFile(name)) throw new IOException("更新文件无效。");
            files[name] = ReleaseUpdater.Hash(file);
        }
        var executables = files.Keys.Where(AppIdentity.IsExecutableName).ToArray();
        if (executables.Length != 1 || files[executables[0]] != update.ExecutableHash || !files.ContainsKey("SHA256SUMS.txt")) throw new IOException("暂存更新已变动，请重新下载。");
        // Preserve the installed filename and existing shortcuts, regardless of which compatible asset was downloaded.
        var packageName = executables[0];
        if (packageName != targetName)
        {
            File.Move(Path.Combine(update.Directory, packageName), Path.Combine(update.Directory, targetName));
            File.WriteAllText(Path.Combine(update.Directory, "SHA256SUMS.txt"), update.ExecutableHash + "  " + targetName + "\n");
            files.Remove(packageName); files[targetName] = update.ExecutableHash;
            files["SHA256SUMS.txt"] = ReleaseUpdater.Hash(Path.Combine(update.Directory, "SHA256SUMS.txt"));
        }
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var plan = new UpdatePlan(profile, target, ReleaseUpdater.Hash(target), files, process.Id, process.StartTime.ToUniversalTime().Ticks, restart);
        var planPath = Path.Combine(stage, "plan.json"); File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        // Run a copy of the currently trusted installer, never execute the downloaded package before validation.
        File.Copy(target, Path.Combine(stage, AppIdentity.UpdaterExecutableName), true);
        return planPath;
    }
    public static UpdatePlan ReadPlan(string planPath)
    {
        DiskCache.NoLinks(planPath);
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath)) ?? throw new IOException("更新计划无效。");
        ValidateLocation(Path.GetDirectoryName(Path.GetFullPath(planPath))!, plan.Profile);
        return plan;
    }
    public static void Apply(string planPath, UpdatePlan plan)
    {
        var stage = Path.GetDirectoryName(Path.GetFullPath(planPath))!; ValidateLocation(stage, plan.Profile);
        var target = Path.GetFullPath(plan.Target); DiskCache.NoLinks(target);
        var targetName = Path.GetFileName(target);
        if (!AppIdentity.IsExecutableName(targetName) || ReleaseUpdater.Hash(target) != plan.OldHash) throw new IOException("原程序已变动，本次更新已取消。");
        if (!plan.Files.ContainsKey(targetName) || plan.Files.Keys.Count(AppIdentity.IsExecutableName) != 1 || !plan.Files.ContainsKey("SHA256SUMS.txt") || plan.Files.Count > 100) throw new IOException("更新文件不完整。");
        var directory = Path.GetDirectoryName(target)!;
        var source = Path.Combine(stage, "package");
        var backup = Path.Combine(stage, "backup");
        foreach (var pair in plan.Files)
        {
            if (!ReleaseUpdater.AllowedFile(pair.Key)) throw new IOException("更新路径无效。");
            var input = Path.Combine(source, pair.Key); DiskCache.NoLinks(input); DiskCache.NoLinks(Path.Combine(directory, pair.Key));
            if (ReleaseUpdater.Hash(input) != pair.Value) throw new IOException("暂存文件校验失败。");
        }
        var changed = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var name in plan.Files.Keys.OrderBy(n => n == targetName ? 1 : 0))
            {
                var dest = Path.Combine(directory, name); var old = Path.Combine(backup, name);
                DiskCache.NoLinks(old);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest)) { Directory.CreateDirectory(Path.GetDirectoryName(old)!); File.Copy(dest, old, false); }
                else old = null;
                var temp = dest + ".tide-update";
                DiskCache.NoLinks(temp);
                File.Copy(Path.Combine(source, name), temp, false);
                try
                {
                    if (ReleaseUpdater.Hash(temp) != plan.Files[name]) throw new IOException("更新文件复制后校验失败。");
                    File.Move(temp, dest, true); changed.Add((dest, old));
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch
        {
            foreach (var entry in changed.AsEnumerable().Reverse())
                if (entry.Backup is null) File.Delete(entry.Target); else File.Copy(entry.Backup, entry.Target, true);
            throw;
        }
    }
    private static void ValidateLocation(string stage, string profile)
    {
        var root = Path.Combine(Path.GetFullPath(profile), "updates");
        if (!string.Equals(Path.GetDirectoryName(stage), root, StringComparison.OrdinalIgnoreCase) || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(stage), "^[a-f0-9]{32}$"))
            throw new IOException("更新暂存路径无效。");
        DiskCache.NoLinks(stage);
    }
}

using System.Diagnostics;
using System.Text.Json;

namespace Paomian.Core;

public static partial class UpdateInstaller
{
    // 旧助手的成功计划和原程序备份在重启后的首次清理前仍在。手工替换没有这两份凭据。
    internal static Version? FindPreviousVersion(string profile, string? executable, Version current)
    {
        var stages = Path.Combine(profile, "updates");
        if (executable is null || !AppIdentity.IsExecutableName(Path.GetFileName(executable)) || !Directory.Exists(stages))
            return null;

        try
        {
            DiskCache.NoLinks(stages);
            var name = Path.GetFileName(executable);
            string? installedHash = null;
            Version? previous = null;
            foreach (var stage in Directory.EnumerateDirectories(stages))
            {
                if (!Guid.TryParseExact(Path.GetFileName(stage), "N", out _)) continue;
                try
                {
                    var result = Path.Combine(stage, "result.txt");
                    DiskCache.NoLinks(result);
                    if (!File.Exists(result) || File.ReadAllText(result) != "success") continue;
                    var plan = ReadPlan(Path.Combine(stage, "plan.json"));
                    if (!string.Equals(plan.Target, executable, StringComparison.OrdinalIgnoreCase) ||
                        plan.Files is null || !plan.Files.TryGetValue(name, out var targetHash)) continue;
                    // 仅信任实际运行文件与旧备份都匹配的安装计划，忽略下载、失败和其他安装位置的暂存。
                    DiskCache.NoLinks(executable);
                    installedHash ??= ReleaseUpdater.Hash(executable);
                    if (installedHash != targetHash) continue;
                    var backup = Path.Combine(stage, "backup", name);
                    DiskCache.NoLinks(backup);
                    if (ReleaseUpdater.Hash(backup) != plan.OldHash) continue;
                    var version = FileVersionInfo.GetVersionInfo(backup).ProductVersion?.Split('+')[0];
                    if (version is null || ReleaseUpdater.StableVersion(version) is not { } source || source >= current)
                        continue;
                    if (previous is not null && previous != source) return null;
                    previous = source;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
                { /* 无法完整验证的暂存不作为历史依据。 */ }
            }
            return previous;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}

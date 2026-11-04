using System.IO.Compression;
using System.Text;
using Tide.Core;

internal static class ExecutableRenameChecks
{
    internal static void Run()
    {
        BrandingChecks.Run();
        var root = Path.GetFullPath(Path.Combine("artifacts", "executable-rename", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        foreach (var packageName in new[] { AppIdentity.ExecutableName, AppIdentity.LegacyExecutableName })
        foreach (var installedName in new[] { AppIdentity.ExecutableName, AppIdentity.LegacyExecutableName })
        {
            var caseRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
            var profile = Path.Combine(caseRoot, "profile");
            var stage = Path.Combine(profile, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var fixture = Path.Combine(caseRoot, "fixture.bin"); File.WriteAllText(fixture, "new executable fixture");
            var hash = ReleaseUpdater.Hash(fixture);
            var zip = Path.Combine(caseRoot, "package.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(fixture, packageName);
                using var writer = new StreamWriter(archive.CreateEntry("SHA256SUMS.txt").Open(), new UTF8Encoding(false));
                writer.Write(hash + "  " + packageName + "\n");
            }
            var extracted = Path.Combine(stage, "package");
            Require(ReleaseUpdater.ExtractVerified(zip, extracted) == hash, "package hash");
            var install = Path.Combine(caseRoot, "installed"); Directory.CreateDirectory(install);
            var target = Path.Combine(install, installedName); File.WriteAllText(target, "old executable");
            File.WriteAllText(Path.Combine(install, "SHA256SUMS.txt"), "old checksum");
            File.WriteAllText(Path.Combine(install, "personal.txt"), "unchanged");
            var planPath = UpdateInstaller.Prepare(new(extracted, hash), profile, target, false);
            var plan = UpdateInstaller.ReadPlan(planPath);
            Require(plan.Files.Keys.Count(AppIdentity.IsExecutableName) == 1 && plan.Files.ContainsKey(installedName), "installed filename not preserved");
            using (var locked = File.Open(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => UpdateInstaller.Apply(planPath, plan), "locked target");
            Require(File.ReadAllText(target) == "old executable" && File.ReadAllText(Path.Combine(install, "SHA256SUMS.txt")) == "old checksum", "rollback");
            // Each attempt receives its own backup directory in normal operation.
            var secondStage = Path.Combine(profile, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(secondStage);
            var secondPackage = Path.Combine(secondStage, "package"); ReleaseUpdater.ExtractVerified(zip, secondPackage);
            var secondPlan = UpdateInstaller.Prepare(new(secondPackage, hash), profile, target, false);
            UpdateInstaller.Apply(secondPlan, UpdateInstaller.ReadPlan(secondPlan));
            Require(ReleaseUpdater.Hash(target) == hash && Directory.GetFiles(install, "*.exe").Length == 1, "renamed install or duplicate executable");
            Require(File.ReadAllText(Path.Combine(install, "SHA256SUMS.txt")).Contains(installedName) && File.ReadAllText(Path.Combine(install, "personal.txt")) == "unchanged", "manifest or personal files");
        }
        var invalid = Path.Combine(root, "ambiguous.zip");
        using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
            foreach (var name in new[] { AppIdentity.ExecutableName, AppIdentity.LegacyExecutableName, "SHA256SUMS.txt" })
                using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("fixture");
        Reject(() => ReleaseUpdater.ExtractVerified(invalid, Path.Combine(root, "rejected")), "multiple executable names");
        Require(!ReleaseUpdater.AllowedFile("../Paomian Island.exe") && !ReleaseUpdater.AllowedFile("Other.exe"), "path restrictions");
        Console.WriteLine("PASS new/legacy executable packages x new/legacy installations, hash checks, filename preservation, rollback, personal files and ambiguous-package rejection.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        try { action(); } catch (IOException) { return; } catch (InvalidDataException) { return; } catch (UnauthorizedAccessException) { return; }
        throw new Exception("Expected rejection: " + message);
    }
}

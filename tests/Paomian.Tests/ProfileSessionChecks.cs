using System.Security.Cryptography;
using System.Text;
using Paomian.Core;

internal static class ProfileSessionChecks
{
    internal static void Run()
    {
        var parent = Directory.CreateTempSubdirectory("Paomian-profile-");
        try
        {
            var old = Path.Combine(parent.FullName, "TideIsland");
            var current = Path.Combine(parent.FullName, "PaomianIsland");
            var custom = Path.Combine(parent.FullName, "portable profile");
            static string OldMutex(string path) => @"Local\TideIsland-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..24];
            static bool OtherThread(Func<bool> action)
            {
                var result = false; Exception? error = null;
                var thread = new Thread(() => { try { result = action(); } catch (Exception ex) { error = ex; } });
                thread.Start(); if (!thread.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("profile lock probe timed out");
                if (error is not null) throw new InvalidOperationException("profile lock probe failed", error);
                return result;
            }
            void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
            void RejectDefault(string label)
            {
                try { using var unexpected = ProfileSession.TryOpen(parent.FullName); }
                catch (IOException) { return; }
                throw new Exception(label);
            }
            using (var startup = new Mutex(true, OldMutex(old), out _))
            {
                Check(OtherThread(() => ProfileSession.TryOpen(parent.FullName) is null) && !Directory.Exists(current), "old mutex blocks default");
                startup.ReleaseMutex();
            }
            using (var fresh = ProfileSession.TryOpen(parent.FullName))
            {
                Check(fresh?.Root == current && !Directory.Exists(old), "new default selected");
                new LocalStore(fresh!.Root).Save(new() { FontSize = 21 });
                using var previous = new Mutex(false, OldMutex(old), out var created);
                Check(!created && OtherThread(() => ProfileSession.TryOpen(parent.FullName, current) is null), "current lock preserved");
                using var other = ProfileSession.TryOpen(parent.FullName, custom);
                Check(other?.Root == custom, "explicit profile independent");
            }
            // 1.0.2 对显式路径会保留末尾分隔符，两种写法都是同一份资料。
            var withSlash = custom + Path.DirectorySeparatorChar;
            using (var legacySlash = new Mutex(true, OldMutex(withSlash), out _))
            {
                Check(OtherThread(() => { using var s = ProfileSession.TryOpen(parent.FullName, custom); return s is null; }), "1.0.2 trailing-separator lock");
                legacySlash.ReleaseMutex();
            }
            using (var portable = ProfileSession.TryOpen(parent.FullName, withSlash))
            {
                Check(portable?.Root == withSlash, "keep explicit path for existing cache key");
                using var second = ProfileSession.TryOpen(parent.FullName, custom);
                Check(second is null && OtherThread(() => { using var s = ProfileSession.TryOpen(parent.FullName, custom); return s is null; }), "equivalent profile paths share a lock");
                using var previous = new Mutex(true, OldMutex(custom), out var created);
                Check(!created, "new instance excludes 1.0.2 using plain path");
            }
            using (var again = ProfileSession.TryOpen(parent.FullName)) Check(new LocalStore(again!.Root).Load().FontSize == 21, "new data persisted");
            new LocalStore(old).Save(new() { FontSize = 17 });
            RejectDefault("two defaults rejected"); RejectDefault("two defaults rejected twice");
            using (var a = ProfileSession.TryOpen(parent.FullName, old))
            using (var b = ProfileSession.TryOpen(parent.FullName, current))
                Check(new LocalStore(a!.Root).Load().FontSize == 17 && new LocalStore(b!.Root).Load().FontSize == 21, "explicit old/new selection");
            using (var probe = ProfileSession.TryOpen(parent.FullName, smokeDirectory: custom)) Check(probe?.Root == Path.Combine(custom, "profile"), "smoke profile");
            using (var probe = ProfileSession.TryOpen(parent.FullName, custom, smokeDirectory: current)) Check(probe?.Root == custom, "data-dir wins");
            var otherBase = Directory.CreateDirectory(Path.Combine(parent.FullName, "unusable")).FullName;
            var file = Path.Combine(otherBase, "TideIsland"); File.WriteAllText(file, "keep");
            try { using var unexpected = ProfileSession.TryOpen(otherBase); throw new Exception("invalid default accepted"); }
            catch (IOException) { Check(File.ReadAllText(file) == "keep" && !Directory.Exists(Path.Combine(otherBase, "PaomianIsland")), "invalid default preserved"); }
        }
        finally { parent.Delete(true); }
    }
}

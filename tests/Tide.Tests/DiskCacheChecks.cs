using Tide.Core;

internal static class DiskCacheChecks
{
    internal static void Run()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "disk-cache", Guid.NewGuid().ToString("N")));
        var cache = new DiskCache(root); cache.Configure(null, 1);
        string Key(int n) => $"images/{n:X64}.bin";
        string FilePath(int n) => Path.Combine(cache.Root, Key(n));
        long ActualSize() => Directory.EnumerateFiles(Path.Combine(cache.Root, "images"), "*.bin").Sum(p => new FileInfo(p).Length);
        File.WriteAllText(Path.Combine(root, "state.json"), "personal data fixture");
        File.WriteAllText(Path.Combine(cache.Root, "unmanaged.txt"), "keep");

        cache.Write(Key(1), new byte[400_000]); cache.Write(Key(2), new byte[400_000]);
        cache.Write(Key(1), new byte[500_000]);
        Require(ActualSize() == 900_000 && File.Exists(FilePath(2)), "覆盖被重复计费或错误淘汰");
        File.SetLastWriteTimeUtc(FilePath(2), DateTime.UtcNow.AddHours(-2));
        cache.Write(Key(3), new byte[200_000]);
        Require(ActualSize() == 700_000 && !File.Exists(FilePath(2)), "超限未淘汰最旧文件");
        cache.Remove(Key(1)); cache.Write(Key(4), new byte[700_000]);
        Require(ActualSize() == 900_000 && File.Exists(FilePath(3)), "移除后容量统计未更新");
        cache.Write(Key(4), new byte[100_000]); cache.Write(Key(5), new byte[700_000]);
        Require(ActualSize() == 1_000_000 && File.Exists(FilePath(3)), "缩小覆盖后错误淘汰");
        File.SetLastWriteTimeUtc(FilePath(3), DateTime.UtcNow.AddHours(-3));
        File.SetLastWriteTimeUtc(FilePath(4), DateTime.UtcNow.AddHours(-2));
        Require(cache.Read(Key(3))?.Length == 200_000, "读取失败");
        cache.Write(Key(6), new byte[100_000]);
        Require(!File.Exists(FilePath(4)) && File.Exists(FilePath(3)) && ActualSize() == 1_000_000, "读取后的最近使用顺序丢失");

        cache.Clear();
        Require(ActualSize() == 0 && File.ReadAllText(Path.Combine(root, "state.json")) == "personal data fixture" &&
            File.ReadAllText(Path.Combine(cache.Root, "unmanaged.txt")) == "keep", "清理影响个人或非托管文件");
        cache.Write(Key(1), new byte[100_000]);
        using (File.Open(FilePath(1), FileMode.Open, FileAccess.Read, FileShare.Read)) cache.Write(Key(1), new byte[300_000]);
        cache.Write(Key(2), new byte[900_000]);
        Require(ActualSize() <= 1_000_000, "写失败后容量统计失效");
        cache.Clear();
        Parallel.For(0, 70, n => cache.Write(Key(n), new byte[20_000]));
        Require(ActualSize() <= 1_000_000, "并发写入突破上限");
        File.WriteAllBytes(FilePath(100), new byte[100_000]);
        Require(cache.SizeBytes() == ActualSize(), "显式统计没有校正外部文件变化");
        cache.Configure(null, 0); cache.Write(Key(1), [1]);
        Require(ActualSize() == 0, "禁用缓存未清空或继续写入");
        try { cache.Write("../state.json", [1]); throw new InvalidOperationException("接受了越界路径"); }
        catch (ArgumentException) { }
        Console.WriteLine("PASS: cache overwrite, remove, LRU, failure recovery, concurrency, recount, disable and personal data preservation.");
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

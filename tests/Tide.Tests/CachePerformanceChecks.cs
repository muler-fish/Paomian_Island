using System.Diagnostics;
using System.Text.Json;
using Tide.Core;

internal static class CachePerformanceChecks
{
    // 显式运行的离线基准，不把磁盘速度作为普通测试的通过条件。
    internal static void Run(string output)
    {
        var root = Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var results = new List<object>();
        foreach (var count in new[] { 100, 1000, 5000 })
        {
            var cache = new DiskCache(Path.Combine(root, count.ToString()));
            var folder = Path.Combine(cache.Root, "images"); Directory.CreateDirectory(folder);
            var seed = new byte[256];
            for (var i = 0; i < count; i++) File.WriteAllBytes(Path.Combine(folder, $"{i:X64}.bin"), seed);
            cache.Configure(null, 256);
            var payload = new byte[32 * 1024];
            cache.Write($"images/{count:X64}.bin", payload); // 预热 JIT 和文件操作。
            var samples = new List<double>();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 1; i <= 60; i++)
            {
                var started = Stopwatch.GetTimestamp();
                cache.Write($"images/{(count + i):X64}.bin", payload);
                samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            samples.Sort();
            var median = samples[samples.Count / 2]; var p95 = samples[(int)(samples.Count * .95) - 1];
            results.Add(new { Files = count, Writes = samples.Count, MedianMs = median, P95Ms = p95, AllocatedBytesPerWrite = allocated / samples.Count });
            Console.WriteLine($"CACHE {count} files: median={median:F3} ms, p95={p95:F3} ms, allocation={allocated / samples.Count:N0} bytes/write");
        }
        var path = Path.Combine(root, "results.json");
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(path);
    }
}

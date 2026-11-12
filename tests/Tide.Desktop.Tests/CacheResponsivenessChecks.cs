using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class CacheResponsivenessChecks
{
    internal static void Run(string output) => DispatcherChecks.Run(() => MeasureAsync(output));

    private static async Task MeasureAsync(string output)
    {
        var root = Path.GetFullPath(Path.Combine(output, Guid.NewGuid().ToString("N")));
        var folder = Path.Combine(root, "cache", "images"); Directory.CreateDirectory(folder);
        for (var i = 0; i < 5000; i++) File.WriteAllBytes(Path.Combine(folder, $"{i:X64}.bin"), new byte[256]);
        using var vm = new MainViewModel(new XIslandApi(new HttpClient(new Handler())), new LocalStore(root));
        await vm.OpenThreadAsync(101);
        var dispatcher = Dispatcher.CurrentDispatcher;
        var samples = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var load = vm.OpenThreadAsync(101, refresh: true);
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            await load;
            dispatcher.VerifyAccess();
            if (vm.Original?.Id != 101 || vm.HasError) throw new InvalidOperationException("读串未完成或出现缓存错误");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        samples.Sort();
        var result = new { Files = 5000, Calls = samples.Count, MedianEntryMs = samples[15], P95EntryMs = samples[28] };
        var path = Path.Combine(root, "results.json");
        File.WriteAllText(path, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"UI ENTRY median={result.MedianEntryMs:F3} ms, p95={result.P95EntryMs:F3} ms (synchronous fixture, no windows)");
        Console.WriteLine(path);
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || !request.RequestUri!.AbsolutePath.EndsWith("/thread", StringComparison.Ordinal))
                throw new InvalidOperationException("只允许模拟读串。");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { id = 101, fid = 4, content = new string('测', 4000), Replies = Array.Empty<object>() }))
            });
        }
    }
}

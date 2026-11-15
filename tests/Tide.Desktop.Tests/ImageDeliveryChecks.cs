using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ImageDeliveryChecks
{
    internal static async Task RunAsync(string output)
    {
        var failures = new List<Exception>();
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[12], 6);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var buffer = new MemoryStream(); encoder.Save(buffer);
        var image = buffer.ToArray();
        var cache = new DiskCache(Path.Combine(output, Guid.NewGuid().ToString("N")));
        await Check("旧缓存无法解码时自动重新下载", async () =>
        {
            foreach (var (part, width) in new[] { ("thumb", 700), ("image", 0) })
            {
                var url = $"https://image.nmb.best/{part}/fixture/cached.png";
                cache.Write(Key(url), "<html>temporary gateway response</html>"u8.ToArray());
                using var handler = new Handler(_ => new ByteArrayContent(image));
                using var http = new HttpClient(handler);
                var loaded = await ImageService.LoadAsync(http, cache, url, width, default);
                Require(loaded.Image is not null && handler.Reads == 1, $"{part} 仍读取无法解码的旧缓存。");
                var again = await ImageService.LoadAsync(http, cache, url, width, default);
                Require(again.Image is not null && handler.Reads == 1, $"{part} 没有复用已验证的图片。");
            }
        });
        await Check("坏响应不会进入缓存", async () =>
        {
            const string url = "https://image.nmb.best/thumb/fixture/transient.png";
            using var handler = new Handler(n => new ByteArrayContent(n == 1 ? image[..8] : image));
            using var http = new HttpClient(handler);
            var first = await ImageService.LoadAsync(http, cache, url, 700, default);
            Require(first.Image is null && cache.Read(Key(url)) is null, "截断的图片写进了缓存。");
            var second = await ImageService.LoadAsync(http, cache, url, 700, default);
            Require(second.Image is not null && handler.Reads == 2, "网络恢复后同一张图仍失败。");
        });
        await Check("成功状态中的 HTML 错误页不会当成原图", async () =>
        {
            const string url = "https://image.nmb.best/image/fixture/gateway.png";
            using var handler = new Handler(n => n == 1 ? new StringContent("<html>gateway error</html>", Encoding.UTF8, "text/html") : new ByteArrayContent(image));
            using var http = new HttpClient(handler);
            try
            {
                await ImageService.LoadAsync(http, cache, url, 0, default);
                throw new InvalidOperationException("成功状态的 HTML 错误页被返回为原图。");
            }
            catch (InvalidDataException) { }
            Require(cache.Read(Key(url)) is null, "HTML 响应被写入缓存。");
            Require((await ImageService.LoadAsync(http, cache, url, 0, default)).Image is not null, "错误页之后无法重新加载原图。");
        });
        await Check("收到响应头后图片正文仍有下载期限", async () =>
        {
            var stream = new WaitingStream();
            using var handler = new Handler(_ => new StreamContent(stream));
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(150) };
            using var lifetime = new CancellationTokenSource();
            var load = ImageService.LoadAsync(http, cache, "https://image.nmb.best/image/fixture/slow.png", 0, lifetime.Token);
            if (await Task.WhenAny(load, Task.Delay(1000)) != load)
            {
                lifetime.Cancel();
                try { await load; } catch (OperationCanceledException) { }
                throw new InvalidOperationException("收到头部后一直占用下载槽位。");
            }
            try
            {
                await load;
                throw new InvalidOperationException("停滞的下载没有报告超时。");
            }
            catch (TimeoutException) when (stream.Started) { }
        });
        await Check("图片请求优先协商 HTTP/2，可回落到 HTTP/1.1", async () =>
        {
            using var handler = new Handler(_ => new ByteArrayContent(image));
            using var http = new HttpClient(handler);
            await ImageService.LoadAsync(http, null, "https://image.nmb.best/thumb/fixture/protocol.png", 700, default);
            Require(handler.Version == HttpVersion.Version20 && handler.VersionPolicy == HttpVersionPolicy.RequestVersionOrLower,
                "图片请求仍只使用默认 HTTP/1.1。");
        });
        await Check("响应头或图片数据提前中断时重新下载一次", async () =>
        {
            var truncated = new TruncatedStream(image[..8]);
            using var headerFailure = new Handler(attempt => attempt == 1
                ? throw new HttpRequestException(HttpRequestError.ResponseEnded, "fixture ended before headers")
                : new ByteArrayContent(image));
            using var bodyFailure = new Handler(attempt => attempt == 1
                ? new StreamContent(truncated) : new ByteArrayContent(image));
            foreach (var (part, handler) in new[] { ("thumb", headerFailure), ("image", bodyFailure) })
            {
                using var http = new HttpClient(handler, disposeHandler: false);
                var url = $"https://image.nmb.best/{part}/fixture/disconnected.png";
                var loaded = await ImageService.LoadAsync(http, cache, url, 0, default);
                Require(handler.Reads == 2 && loaded.Image is not null && loaded.Bytes.SequenceEqual(image),
                    $"{part} 未在原响应中断后完整重取。");
                Require(cache.Read(Key(url))?.SequenceEqual(image) == true, $"{part} 把中断残片写入了缓存。");
            }
        });
        await Check("持续断流会停止重试并显示原因", async () =>
        {
            const string url = "https://image.nmb.best/image/fixture/persistent.png";
            using var handler = new Handler(_ => new StreamContent(new TruncatedStream(image[..8], HttpRequestError.HttpProtocolError)));
            using var http = new HttpClient(handler);
            try
            {
                await ImageService.LoadAsync(http, cache, url, 0, default);
                throw new InvalidOperationException("断流响应被当成图片。");
            }
            catch (HttpIOException error)
            {
                Require(handler.Reads == 2 && ImageService.ErrorMessage(error) == "图片传输中断", "持续断流没有限次或报告原因。");
                Require(cache.Read(Key(url)) is null, "持续断流污染了原图缓存。");
            }
        });
        await Check("HTTP 404 保留状态且只请求一次", async () =>
        {
            using var handler = new Handler(_ => new StringContent("missing"), HttpStatusCode.NotFound);
            using var http = new HttpClient(handler);
            try
            {
                await ImageService.LoadAsync(http, null, "https://image.nmb.best/image/fixture/missing.png", 0, default);
                throw new InvalidOperationException("未报告图片不存在。");
            }
            catch (HttpRequestException error)
            {
                Require(handler.Reads == 1 && error.StatusCode == HttpStatusCode.NotFound, "HTTP 404 被当成传输中断重试。");
            }
        });
        if (failures.Count > 0) throw new AggregateException(failures);

        async Task Check(string title, Func<Task> run)
        {
            try { await run(); Console.WriteLine($"PASS: {title}"); }
            catch (Exception ex) { failures.Add(ex); Console.Error.WriteLine($"FAIL: {title} ({ex.Message})"); }
        }
    }

    private static string Key(string url) => "images/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".bin";
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    private sealed class Handler(Func<int, HttpContent> content, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal int Reads { get; private set; }
        internal Version? Version { get; private set; }
        internal HttpVersionPolicy VersionPolicy { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Version = request.Version;
            VersionPolicy = request.VersionPolicy;
            return Task.FromResult(new HttpResponseMessage(status) { Content = content(++Reads) });
        }
    }

    private sealed class TruncatedStream(byte[] prefix, HttpRequestError error = HttpRequestError.ResponseEnded) : MemoryStream(prefix)
    {
        public override bool CanSeek => false;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            Position == Length ? ValueTask.FromException<int>(new HttpIOException(error, "fixture interrupted"))
                : base.ReadAsync(buffer, ct);
    }

    private sealed class WaitingStream : MemoryStream
    {
        internal bool Started { get; private set; }
        public override bool CanSeek => false;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            Started = true;
            await Task.Delay(Timeout.Infinite, ct);
            return 0;
        }
    }
}

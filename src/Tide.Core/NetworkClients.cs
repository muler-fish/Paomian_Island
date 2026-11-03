using System.Net;

namespace Tide.Core;

public static class NetworkClients
{
    public static bool IslandUseProxy { get; set; } = true;
    public static bool UpdateUseProxy { get; set; } = true;
    public static HttpClient Create(bool updates = false) => new(new ProxyRouteHandler(
        () => updates ? UpdateUseProxy : IslandUseProxy)) { Timeout = TimeSpan.FromSeconds(25) };
}

// Choose per request; changing settings never disposes an in-flight request or sends cookies to another scope.
public sealed class ProxyRouteHandler : HttpMessageHandler
{
    private readonly Func<bool> _useProxy;
    private readonly HttpMessageInvoker _proxy, _direct;
    public ProxyRouteHandler(Func<bool> useProxy, HttpMessageHandler? proxy = null, HttpMessageHandler? direct = null)
    {
        _useProxy = useProxy;
        _proxy = new(proxy ?? Create(true)); _direct = new(direct ?? Create(false));
    }
    private static HttpClientHandler Create(bool proxy) => new()
    { UseProxy = proxy, UseCookies = false, AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All };
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
        (_useProxy() ? _proxy : _direct).SendAsync(request, ct);
    protected override void Dispose(bool disposing) { if (disposing) { _proxy.Dispose(); _direct.Dispose(); } base.Dispose(disposing); }
}

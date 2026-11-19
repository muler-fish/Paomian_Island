using System.Net;
using System.Text.Json;
using Paomian.Core;

internal static class LinkRoutingChecks
{
    public static async Task RunAsync()
    {
        foreach (var url in new[] { "https://www.nmbxd1.com/t/123?page=2", "http://nmbxd.com/Forum/thread/id/123/page/2.html",
            "/Home/Forum/thread.html?id=123&page=2", "Forum/thread?id=123&page=2", "//www.nmbxd1.com/t/123?page=2" })
        { var link = IslandLink.Parse(url)!; Require(link.Kind == IslandLinkKind.Thread && link.Id == 123 && link.Page == 2, url); }
        Require(IslandLink.Parse("/t/123?r=456&page=3") is { Page: 3, ReplyId: 456 }, "reply query");
        Require(IslandLink.Parse("/t/123?page=3#r456") is { ReplyId: 456 }, "reply fragment");
        Require(ContentText.ParseThreadId("/Home/Forum/thread/id/123.html") == 123, "jump box uses same parser");
        foreach (var url in new[] { "/f/婆罗门一", "/f/%E5%A9%86%E7%BD%97%E9%97%A8%E4%B8%80" })
            Require(IslandLink.Parse(url) is { Kind: IslandLinkKind.Forum, Name: "婆罗门一" }, "encoded forum");
        Require(IslandLink.Parse("/Forum/showf?id=4&page=3") is { Kind: IslandLinkKind.Forum, Id: 4, Page: 3 }, "query forum");
        Require(IslandLink.Parse("/Home/Forum/showf/id/4/page/3.html") is { Kind: IslandLinkKind.Forum, Id: 4, Page: 3 }, "legacy forum");
        Require(IslandLink.Parse("/Forum/timeline/id/1/page/2") is { Kind: IslandLinkKind.Timeline, Id: 1, Page: 2 }, "timeline");
        foreach (var url in new[] { "https://www.nmbxd1.com.evil.example/t/123", "https://evil.example/t/123", "/Member",
            "/Api/thread?id=123", "/t/123/delete", "/t/123?page=0", "/t/123?page=2147483648", "/t/123?page=2&page=3",
            "/t/123?id=456", "/t/123#unknown", "https://name@www.nmbxd1.com/t/123", "https://www.nmbxd1.com:8443/t/123" })
            Require(IslandLink.Parse(url)?.Kind == IslandLinkKind.Web, "must keep webpage: " + url);
        foreach (var url in new[] { "javascript:alert(1)", "file:///t/123", "data:text/plain,test", "https://example.com\n/test", @"https://example.com\t\123" })
            Require(IslandLink.Parse(url) is null, "invalid protocol: " + url);
        Require(IslandLink.Parse("//outside.example/path")?.Url.Host == "outside.example", "protocol relative external URL");

        using var api = new XIslandApi(new HttpClient(new CatalogHandler()));
        var forums = await api.GetForumsAsync();
        Require(IslandLink.Parse("/f/技术宅")!.FindForum(forums) is { Name: "技术宅(代码)", Id: 30 }, "raw API name");
        Require(IslandLink.Parse("/f/技术宅(代码)")!.FindForum(forums)?.Id == 30, "display name");
        Require(IslandLink.Parse("/f/DANGER_U")!.FindForum(forums)?.Name == "DANGER/U/", "special alias");
        Require(IslandLink.Parse("/f/removed")!.FindForum(forums) is null, "unknown forum");
        var saved = JsonSerializer.Deserialize<List<Forum>>(JsonSerializer.Serialize(forums))!;
        Require(saved[1].UrlName == forums[1].UrlName && IslandLink.Parse("/f/技术宅")!.FindForum(saved)?.Id == 30, "persist URL name");
        Require(JsonSerializer.Deserialize<Forum>("""{"Id":30,"Name":"技术宅(代码)","Group":"综合"}""")!.UrlName == "", "old catalog loads");
        Console.WriteLine("PASS: link routes, encoded names, pages/replies, legacy forms, web fallback, protocol/host boundaries and catalog aliases.");
    }
    private static void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
    private sealed class CatalogHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("getForumList")
                ? """[{"name":"综合","forums":[{"id":30,"name":"技术宅","showName":"技术宅(代码)"},{"id":4,"name":"DANGER_U","showName":"DANGER/U/"}]}]"""
                : """[{"id":1,"name":"综合线","max_page":20}]""") });
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using Tide.Core;

// 局部验证入口：仅运行链接及其目录映射，避免重复执行无关功能回归。
if (args.Contains("--links")) { await LinkRoutingChecks.RunAsync(); return 0; }
if (args.Contains("--sent-thread")) { await SentThreadChecks.RunAsync(); return 0; }
if (args.Contains("--cookie-usage")) { CookieUsageChecks.Run(); return 0; }
if (args.Contains("--settings-services")) { await SettingsServicesChecks.RunAsync(); return 0; }
if (args.Contains("--branding")) { BrandingChecks.Run(); return 0; }
if (args.Contains("--executable-rename"))
{
    try { ExecutableRenameChecks.Run(); return 0; }
    catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
}
if (args.FirstOrDefault() == "--update-helper") { await UpdateHelperChecks.RunAsync(args[1]); return 0; }
if (args.Contains("--update-parent")) { await Task.Delay(2500); return 0; }
// 无第三方测试框架的控制台测试入口：注册用例、依次执行，最后通过进程退出码报告是否通过。
// 所有 HTTP 用例都注入 FakeHandler，只检查构造的请求和模拟响应，不会向真实站点发言。
var checks = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action action) => checks.Add((name, () => { action(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> action) => checks.Add((name, action));
AsyncTest("content links and forum aliases", LinkRoutingChecks.RunAsync);
void Assert(bool test, string why = "Assertion failed") { if (!test) throw new Exception(why); }
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
HttpResponseMessage Response(string value, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(value, Encoding.UTF8) };
void Cleanup(string path)
{
    // 递归清理前同时校验绝对路径和专用目录前缀，保证只删除本次创建的测试临时目录。
    var target = Path.GetFullPath(path);
    var parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("TideTests-", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing to remove a directory outside the test temp root.");
    Directory.Delete(target, true);
}

// 纯数据处理：覆盖数字字段兼容、正文转换、串号识别、饼干输入及图片签名。
AsyncTest("image paging filters photos, retains PO scope and advances across text pages", async () =>
{
    var original = new Post { Id = 10, UserHash = "po", ReplyCount = 57 };
    var image = new Post { Id = 12, ImagePath = "test/image", Extension = ".jpg", UserHash = "po" };
    var requested = new List<int>();
    var cursor = new ImagePageCursor(10, 1, 3, [10, 11], "po", (page, _) =>
    {
        requested.Add(page);
        return Task.FromResult(new ThreadPage(original, page == 2 ? [new Post { Id = 13 }] : [image, image, image with { Id = 14, UserHash = "other" }], page));
    });
    var second = await cursor.NextAsync(default); Assert(second.Images.Count == 0 && second.HasMore);
    var third = await cursor.NextAsync(default); Assert(third.Images.Count == 1 && third.Images[0].Id == 12 && !third.HasMore);
    await cursor.NextAsync(default); Assert(requested.SequenceEqual(new[] { 2, 3 }), "末页后不应重复请求");
});
AsyncTest("image paging retries failed and duplicate pages without advancing", async () =>
{
    var original = new Post { Id = 10, ReplyCount = 100 }; var calls = 0;
    var cursor = new ImagePageCursor(10, 1, 6, [11], null, (page, _) =>
    {
        Assert(page == 2); calls++;
        if (calls == 1) throw new HttpRequestException();
        return Task.FromResult(new ThreadPage(original, calls == 2 ? [new Post { Id = 11 }] : [], page));
    });
    await ThrowsAsync<HttpRequestException>(() => cursor.NextAsync(default));
    await ThrowsAsync<IslandException>(() => cursor.NextAsync(default));
    Assert(cursor.HasMore); var empty = await cursor.NextAsync(default);
    Assert(!empty.HasMore && !cursor.HasMore && calls == 3);
});
AsyncTest("image paging rejects wrong thread and canceled late responses", async () =>
{
    var cts = new CancellationTokenSource(); var calls = 0;
    var cursor = new ImagePageCursor(10, 1, 4, [], null, (page, _) =>
    {
        calls++; Assert(page == 2);
        if (calls == 1) return Task.FromResult(new ThreadPage(new Post { Id = 20 }, [], page));
        cts.Cancel(); return Task.FromResult(new ThreadPage(new Post { Id = 10 }, [], page));
    });
    await ThrowsAsync<IslandException>(() => cursor.NextAsync(default));
    await ThrowsAsync<OperationCanceledException>(() => cursor.NextAsync(cts.Token));
    Assert(cursor.HasMore); cts.Dispose();
});
Test("window layout defaults, persistence and invalid saved dimensions", () =>
{
    var root = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new LocalStore(root);
        File.WriteAllText(Path.Combine(root, "state.json"), "{\"FontSize\":18}");
        Assert(store.Load().WindowLayout is null, "旧资料应保持首次启动布局");
        var state = store.Load();
        state.WindowLayout = new() { Width = 1100, Height = 720, Maximized = true, NavigationWidth = 220, ListWidth = 410 };
        store.Save(state);
        var layout = store.Load().WindowLayout!;
        Assert(layout.Width == 1100 && layout.Height == 720 && layout.Maximized && layout.NavigationWidth == 220 && layout.ListWidth == 410);
        File.WriteAllText(Path.Combine(root, "state.json"), "{\"WindowLayout\":{\"Width\":1100,\"Height\":720,\"NavigationVisible\":false,\"NavigationWidth\":220,\"ListWidth\":410}}");
        state = store.Load(); store.Save(state);
        Assert(state.WindowLayout!.NavigationWidth == 220 && state.WindowLayout.ListWidth == 410 && !File.ReadAllText(Path.Combine(root, "state.json")).Contains("NavigationVisible"), "旧隐藏字段应忽略，不应丢失其他布局");
        File.WriteAllText(Path.Combine(root, "state.json"), "{\"WindowLayout\":{\"Width\":-10,\"Height\":999999,\"NavigationWidth\":3,\"ListWidth\":99999}}");
        layout = store.Load().WindowLayout!;
        Assert(layout.Width == 1280 && layout.Height == 16384 && layout.NavigationWidth == 148 && layout.ListWidth == 16384);
        layout.Width = double.NaN; layout.ListWidth = double.PositiveInfinity; layout.Normalize();
        Assert(layout.Width == 1280 && layout.ListWidth == 378);
    }
    finally { Cleanup(root); }
});

Test("post timestamps omit only the current year and use requested precision", () =>
{
    Assert(PostTime.Format("2026-10-27(二)20:38:07", currentYear: 2026) == "10/27 20:38");
    Assert(PostTime.Format("2026-10-27(二)20:38:07", true, 2026) == "10/27 20:38:07");
    Assert(PostTime.Format("2025-10-27(一)20:38:07", currentYear: 2026) == "2025/10/27 20:38");
    Assert(PostTime.Format("2025-10-27(一)20:38:07", true, 2026) == "2025/10/27 20:38:07");
    Assert(PostTime.Format("2027-01-01 00:00:00", true, 2026) == "2027/01/01 00:00:00");
    Assert(PostTime.Format("2026-12-31 23:59:59", true, 2027) == "2026/12/31 23:59:59");
});
Test("post timestamps validate dates and preserve missing or unknown precision", () =>
{
    Assert(PostTime.Format("2026/1/2（五）03:04:05", true, 2026) == "01/02 03:04:05");
    Assert(PostTime.Format("2024-02-29 01:02:03", true, 2026) == "2024/02/29 01:02:03");
    Assert(PostTime.Format("2026-10-27 20:38", true, 2026) == "10/27 20:38");
    Assert(PostTime.Format("2026-10-27", true, 2026) == "10/27");
    foreach (var value in new[] { "", "未知时间", "2026-02-30 20:38:07", "2026-10-27 25:38:07" })
        Assert(PostTime.Format(value, true, 2026) == value);
    Assert(PostTime.Format(null) == "");
});
Test("mixed number representations and optional fields", () =>
{
    using var doc = JsonDocument.Parse("""{"id":"123","fid":30,"ReplyCount":"31","user_hash":"Test123","content":"hello","admin":"1"}""");
    var p = XIslandApi.ParsePost(doc.RootElement);
    Assert(p.Id == 123 && p.Fid == 30 && p.ReplyCount == 31 && p.IsAdmin && p.Title == "");
    Assert(new ThreadPage(p, [], 1).TotalPages == 2);
});
Test("double-encoded island HTML and script suppression", () =>
{
    Assert(ContentText.Plain("a&lt;br /&gt;b &amp; c") == "a\nb & c");
    Assert(ContentText.Plain("<script>alert(1)</script>ok<style>x{}</style>") == "ok");
    Assert(ContentText.Plain("<b>safe</b>[h]secret[/h]", true) == "safe[隐藏内容]");
});
Test("thread navigation accepts only expected island links", () =>
{
    Assert(ContentText.ParseThreadId("No.69574374") == 69574374);
    Assert(ContentText.ParseThreadId(">>No.69574374") == 69574374);
    Assert(ContentText.ParseThreadId("https://www.nmbxd1.com/t/69574374?page=2") == 69574374);
    Assert(ContentText.ParseThreadId("https://evil.example/t/69574374") is null);
    Assert(ContentText.ParseThreadId("file:///t/69574374") is null);
    Assert(ContentText.ParseThreadId("0") is null);
});
Test("HTML formatting newlines do not duplicate visible breaks", () =>
{
    Assert(ContentText.Plain("第一行<br />\n第二行") == "第一行\n第二行");
    Assert(ContentText.Plain("第一行&lt;br /&gt;\r\n第二行") == "第一行\n第二行");
    Assert(ContentText.Plain("<p>第一段</p>\n<p>第二段</p>") == "第一段\n第二段");
    Assert(ContentText.Normalize("甲<BR/> \t\r\n　　乙") == "甲<BR/>　　乙", "必须保留下一行的全角缩进");
    Assert(ContentText.Normalize("甲<br>\n<br>\n乙") == "甲<br><br>乙", "连续 br 表示有意留空");
    Assert(ContentText.Normalize("甲\n\n  乙") == "甲\n\n  乙", "纯文本空行和空格不能被折叠");
    Assert(ContentText.Normalize("甲<br>\n\n乙") == "甲<br>\n乙", "仅移除标签后的一个源码换行");
});
Test("cookie imports retain existing percent encoding", () =>
{
    Assert(ContentText.NormalizeCookie("userhash=synthetic%2Fcookie%3D; other=value") == "synthetic%2Fcookie%3D");
    Assert(ContentText.NormalizeCookie("""{"cookie":"synthetic%25cookie","name":"test"}""") == "synthetic%25cookie");
    Throws<ArgumentException>(() => ContentText.NormalizeCookie("Abc1234"));
    Throws<ArgumentException>(() => ContentText.NormalizeCookie("synthetic\r\nCookie:bad"));
    Throws<ArgumentException>(() => ContentText.NormalizeCookie("synthetic;bad"));
});
Test("image URL cannot escape CDN and checks file signatures", () =>
{
    Assert(new Post { ImagePath = "2026-01-01/file", Extension = ".png" }.ImageUrl() == "https://image.nmb.best/image/2026-01-01/file.png");
    Assert(new Post { ImagePath = "../secret", Extension = ".png" }.ImageUrl() is null);
    Assert(new Post { ImagePath = "http://evil", Extension = ".jpg" }.ImageUrl() is null);
    Assert(XIslandApi.DetectImage("<script>hello"u8) is null);
    Assert(XIslandApi.DetectImage("GIF89a123"u8) == "image/gif");
});
// 只读接口：模拟正常目录与串页，以及权限错误、验证网页和限流响应。
AsyncTest("forum and timeline catalog from real API shape", async () =>
{
    using var api = new XIslandApi(new HttpClient(new FakeHandler((r, _) => Task.FromResult(Response(
        r.RequestUri!.AbsolutePath.EndsWith("getForumList")
        ? """[{"name":"综合","forums":[{"id":"-1","name":"时间线"},{"id":"30","name":"技术宅","showName":"技术宅(代码)","msg":"规则"}]}]"""
        : """[{"id":1,"name":"综合线","max_page":20}]""")))));
    var forums = await api.GetForumsAsync();
    Assert(forums.Count == 2 && forums[0].IsTimeline && forums[0].MaxPage == 20 && forums[1].Id == 30);
});
AsyncTest("server permission errors remain visible", async () =>
{
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("""{"success":false,"error":"必须登入领取饼干后才可以访问"}""")))));
    try { await api.GetThreadAsync(123, 1); throw new Exception("No failure"); }
    catch (IslandException ex) { Assert(ex.Message.Contains("饼干")); }
});
AsyncTest("HTML verification pages are not treated as forum data", async () =>
{
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("<html>verify</html>")))));
    await ThrowsAsync<IslandException>(() => api.GetThreadsAsync(new(1, "综合线", "时间线", "", true), 1));
});
AsyncTest("rate limit is not retried automatically", async () =>
{
    var count = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => { count++; return Task.FromResult(Response("slow down", HttpStatusCode.TooManyRequests)); })));
    await ThrowsAsync<IslandException>(() => api.GetThreadAsync(123, 1)); Assert(count == 1);
});
AsyncTest("thread page retains official tips at their positions without counting them", async () =>
{
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("""{"id":"123","ReplyCount":20,"Replies":[{"id":9999999,"user_hash":"Tips","now":"2099-01-01 00:00:01","content":"前提示"},{"id":"124","content":"reply"},{"id":"9999999","user_hash":"Tips","now":"2099-01-01 00:00:01","content":"后提示"}]}""")))));
    var page = await api.GetThreadAsync(123, 1); Assert(page.Replies.Count == 1 && page.Replies[0].Id == 124 && page.TotalPages == 2);
    Assert(page.Tips is { Count: 2 } && page.Tips.Select(t => t.BeforeReplyIndex).SequenceEqual(new[] { 0, 1 }), "Official tips disappeared or lost their original positions");
    Assert(page.Tips![0].Post.Content == "前提示" && page.Tips[1].Post.Content == "后提示");
});

AsyncTest("mutable Tips title and partial sentinel matches remain ordinary replies", async () =>
{
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("""{"id":123,"Replies":[{"id":124,"name":"Tips","title":"Tips","user_hash":"Tips","now":"2099-01-01 00:00:01"},{"id":9999999,"user_hash":"Other","now":"2099-01-01 00:00:01"},{"id":9999999,"user_hash":"Tips","now":"2026-10-26 00:00:00"}]}""")))));
    var page = await api.GetThreadAsync(123, 1);
    Assert(page.Replies.Count == 3 && page.Replies.All(p => !p.IsSiteTip) && (page.Tips?.Count ?? 0) == 0);
});

Test("thread cache retains tips and accepts older pages without tips", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N")); var store = new LocalStore(path);
    try
    {
        var original = new Post { Id = 123, ReplyCount = 19 }; var reply = new Post { Id = 124 };
        var tip = new Post { Id = 9999999, UserHash = "Tips", Time = "2099-01-01 00:00:01", Content = "提示" };
        store.SavePage(new(original, [reply], 1, Tips: [new(tip, 1)]), "guest");
        var saved = store.ReadPage(123, 1, "guest");
        Assert(saved?.Tips is { Count: 1 } && saved.Tips[0].BeforeReplyIndex == 1 && saved.Tips[0].Post.IsSiteTip);
        var old = JsonSerializer.Deserialize<ThreadPage>("""{"Original":{"Id":123},"Replies":[{"Id":124}],"Page":2}""")!;
        store.SavePage(old, "guest"); Assert(store.ReadPage(123, 2, "guest") is { Tips: null, Replies.Count: 1 });
    }
    finally { Cleanup(path); }
});
// 发言协议：明确成功才算送达；特别检查未知结果不重试、预检失败不提交。
AsyncTest("clamped high pages are rejected instead of mislabeled as requested pages", async () =>
{
    using var api = new XIslandApi(new HttpClient(new FakeHandler((request, _) =>
        Task.FromResult(Response("""{"id":51288554,"ReplyCount":8995,"Replies":[{"id":55499780},{"id":55525229}]}""")))));
    await ThrowsAsync<IslandException>(() => api.GetThreadAsync(51288554, 200));
});
AsyncTest("large valid pages preserve auth and reuse only same-identity baselines", async () =>
{
    var calls = new List<(int Page, string Cookie)>();
    using var api = new XIslandApi(new HttpClient(new FakeHandler((request, _) =>
    {
        var page = int.Parse(request.RequestUri!.Query.Split("page=")[1]);
        calls.Add((page, request.Headers.TryGetValues("Cookie", out var cookie) ? cookie.Single() : ""));
        return Task.FromResult(Response(JsonSerializer.Serialize(new { id = 123, ReplyCount = 100000,
            Replies = new[] { new { id = 100000 + page } } })));
    })));
    api.SetCookie("synthetic%2Bcookie");
    Assert((await api.GetThreadAsync(123, 1001)).Replies.Single().Id == 101001);
    Assert((await api.GetThreadAsync(123, 1002)).PaginationVerified);
    Assert(calls.Select(c => c.Page).SequenceEqual(new[] { 1001, 100, 1002 }));
    Assert(calls.All(c => c.Cookie == "userhash=synthetic%2Bcookie"));
    api.SetCookie(null); await api.GetThreadAsync(123, 1003);
    Assert(calls.Skip(3).Select(c => c.Page).SequenceEqual(new[] { 1003, 100 }) && calls.Skip(3).All(c => c.Cookie == ""));
});
Test("post success requires explicit server confirmation", () =>
{
    var result = XIslandApi.ParsePostResult("""<p class="success">回复成功</p><a href="/t/123">查看</a>""", HttpStatusCode.OK);
    Assert(result.ThreadId == 123);
    Throws<IslandException>(() => XIslandApi.ParsePostResult("""<p class="error">饼干已失效</p>""", HttpStatusCode.OK));
    Throws<DeliveryUnknownException>(() => XIslandApi.ParsePostResult("<html>unrecognized</html>", HttpStatusCode.OK));
    Throws<DeliveryUnknownException>(() => XIslandApi.ParsePostResult("", HttpStatusCode.Found));
});
AsyncTest("post payload cookie csrf and watermark omission", async () =>
{
    var calls = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler(async (request, ct) =>
    {
        calls++;
        if (request.Method == HttpMethod.Get)
        {
            var response = Response("""<meta name="__hash__" content="csrf-value"/>"""); response.Headers.Add("Set-Cookie", "PHPSESSID=test-session; Path=/; HttpOnly"); return response;
        }
        Assert(request.RequestUri!.AbsoluteUri == XIslandApi.Site + "/Home/Forum/doReplyThread.html");
        var cookies = string.Join(";", request.Headers.GetValues("Cookie"));
        Assert(cookies.Contains("userhash=synthetic%2Fcookie") && cookies.Contains("PHPSESSID=test-session"));
        var body = await request.Content!.ReadAsStringAsync(ct);
        Assert(body.Contains("name=\"content\"") && body.Contains("测试<&>"));
        var namePart = ((MultipartFormDataContent)request.Content).Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "name");
        Assert(await namePart.ReadAsStringAsync(ct) == "名称<&>", "Optional author name was lost or encoded as markup");
        Assert(body.Contains("name=\"resto\"") && body.Contains("csrf-value"));
        Assert(!body.Contains("name=\"water\""), "PHP treats the string false as true; false watermark field must be absent");
        return Response("""<p class="success">回复成功</p>""");
    })));
    await api.SendPostAsync(new(30, 123, "标题", "测试<&>", Name: "名称<&>"), "synthetic%2Fcookie"); Assert(calls == 2);
});
AsyncTest("uncertain delivery never retries the POST", async () =>
{
    var posts = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler((r, _) =>
    {
        if (r.Method == HttpMethod.Get) return Task.FromResult(Response("<html></html>"));
        posts++; throw new HttpRequestException("lost response");
    })));
    await ThrowsAsync<DeliveryUnknownException>(() => api.SendPostAsync(new(30, 123, "", "test"), "synthetic-cookie")); Assert(posts == 1);
});
AsyncTest("new thread sends optional title and name verbatim", async () =>
{
    foreach (var name in new[] { "", "名称<&>" })
    {
        using var api = new XIslandApi(new HttpClient(new FakeHandler(async (request, ct) =>
        {
            if (request.Method == HttpMethod.Get) return Response("<html></html>");
            Assert(request.RequestUri!.AbsolutePath == "/Home/Forum/doPostThread.html");
            var parts = (MultipartFormDataContent)request.Content!;
            async Task<string> Field(string key) => await parts.Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == key).ReadAsStringAsync(ct);
            Assert(await Field("name") == name && await Field("title") == "标题<&>" && await Field("fid") == "4");
            Assert(!parts.Any(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "resto"));
            return Response("""<p class="success">发串成功</p>""");
        })));
        await api.SendPostAsync(new(4, 0, "标题<&>", "正文", Name: name), "synthetic-cookie");
    }
});
AsyncTest("preflight failure sends no post", async () =>
{
    var posts = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler((r, _) =>
    { if (r.Method == HttpMethod.Post) posts++; return Task.FromResult(Response("offline", HttpStatusCode.ServiceUnavailable)); })));
    await ThrowsAsync<IslandException>(() => api.SendPostAsync(new(30, 123, "", "test"), "synthetic-cookie")); Assert(posts == 0);
});
AsyncTest("invalid form data never reaches network", async () =>
{
    var calls = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => { calls++; return Task.FromResult(Response("")); })));
    await ThrowsAsync<ArgumentException>(() => api.SendPostAsync(new(30, 0, "", "  "), "synthetic-cookie"));
    await ThrowsAsync<ArgumentException>(() => api.SendPostAsync(new(0, 0, "", "hello"), "synthetic-cookie")); Assert(calls == 0);
    await ThrowsAsync<ArgumentException>(() => api.SendPostAsync(new(4, 0, "", "hello", Name: new string('a', 101)), "synthetic-cookie")); Assert(calls == 0);
});
// 本机存储：使用独立临时目录，验证状态保存、缓存身份隔离以及损坏文件备份。
Test("atomic local state and identity-scoped cache", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N"));
    var store = new LocalStore(path); var state = new UserState { FontSize = 18 };
    state.History.Add(new(new() { Id = 123, Content = "test" }, 2, DateTimeOffset.UtcNow)); store.Save(state);
    Assert(store.Load().History.Single().Page == 2);
    store.SavePage(new(new() { Id = 123 }, [], 2), "identity-a");
    Assert(store.ReadPage(123, 2, "identity-a") is not null && store.ReadPage(123, 2, "identity-b") is null);
    store.SavePage(new(new() { Id = 123 }, [], 200), "identity-a");
    Assert(store.ReadPage(123, 200, "identity-a") is null, "Old unchecked high pages must not be used offline");
    store.SavePage(new(new() { Id = 123 }, [], 200, PaginationVerified: true), "identity-a");
    Assert(store.ReadPage(123, 200, "identity-a") is not null);
    // 只清理刚创建的唯一测试目录，具体路径边界检查在 Cleanup 中执行。
    Cleanup(path);
});
Test("spoiler protection defaults off and retains explicitly saved settings", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new LocalStore(path);
        File.WriteAllText(Path.Combine(path, "state.json"), "{}");
        var state = store.Load(); Assert(!state.HideSpoilers && !new UserState().HideSpoilers);
        state.HideSpoilers = true; store.Save(state); Assert(store.Load().HideSpoilers);
        state.HideSpoilers = false; store.Save(state); Assert(!store.Load().HideSpoilers);
        Assert(ContentText.Plain("[h]原文[/h]", false) == "[h]原文[/h]");
    }
    finally { Cleanup(path); }
});
Test("corrupt local state is backed up before reset", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N")); var store = new LocalStore(path);
    File.WriteAllText(Path.Combine(path, "state.json"), "invalid-json");
    var state = store.Load(); Assert(store.LoadWarning is not null && Directory.GetFiles(path, "state-unreadable-*.json").Length == 1);
    store.Save(state); Assert(File.ReadAllText(Directory.GetFiles(path, "state-unreadable-*.json").Single()) == "invalid-json");
    Cleanup(path);
});
Test("older drafts retain content when optional name is introduced", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N")); var store = new LocalStore(path);
    try
    {
        File.WriteAllText(Path.Combine(path, "state.json"), """{"Drafts":[{"Key":"new:4","ForumId":4,"ThreadId":0,"Title":"旧标题","Content":"旧正文","ImageFile":"","Watermark":false}]}""");
        var state = store.Load(); var draft = state.Drafts.Single();
        Assert(draft.Name == "" && draft.Title == "旧标题" && draft.Content == "旧正文" && state.RecentEmoticons.Count == 0);
        state.Drafts[0] = draft with { Name = "新名称" }; state.RecentEmoticons = ["　", "\u200b", "(`ᝫ´ )"];
        store.Save(state); var restored = store.Load();
        Assert(restored.Drafts[0].Name == "新名称" && restored.RecentEmoticons.SequenceEqual(state.RecentEmoticons));
    }
    finally { Cleanup(path); }
});
Test("reading position persists and older or invalid history remains readable", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N")); var store = new LocalStore(path);
    try
    {
        var position = new ReadingPosition(456, 7, 31.5, true);
        var state = new UserState { History = [new(new() { Id = 123 }, 1500, DateTimeOffset.UtcNow, position)] };
        store.Save(state); var restored = store.Load().History.Single();
        Assert(restored.Page == 1500 && restored.Position == position);
        File.WriteAllText(Path.Combine(path, "state.json"), """{"History":[{"Post":{"Id":123},"Page":27,"ReadAt":"2026-10-24T00:00:00Z"}]}""");
        restored = store.Load().History.Single(); Assert(restored.Page == 27 && restored.Position is null);
        state.History[0] = state.History[0] with { Page = -4, Position = new(456, 500, -10, false) }; store.Save(state);
        restored = store.Load().History.Single();
        Assert(restored.Page == 1 && restored.Position is { Index: 18, Offset: 0 });
    }
    finally { Cleanup(path); }
});

AsyncTest("site notice uses independent anonymous endpoint and preserves version", async () =>
{
    var calls = 0;
    using var client = new SiteNoticeClient(new HttpClient(new FakeHandler((request, _) =>
    {
        calls++;
        Assert(request.Method == HttpMethod.Get && request.RequestUri?.AbsoluteUri == SiteNoticeClient.Endpoint);
        Assert(!request.Headers.Contains("Cookie") && request.Headers.Authorization is null);
        return Task.FromResult(Response("""{"enable":true,"date":2026102100008,"content":"维护公告<br>稍后恢复 &amp; 感谢理解"}"""));
    })));
    var notice = await client.GetAsync();
    Assert(notice?.Version == "2026102100008" && notice.Content == "维护公告\n稍后恢复 & 感谢理解" && calls == 1);
});
Test("disabled empty and malformed notices do not become announcements", () =>
{
    foreach (var json in new[] { "{}", "{\"enable\":null}", "{\"enable\":false,\"content\":\"old\"}", "{\"enable\":true}", "{\"enable\":true,\"content\":null}", "{\"enable\":true,\"content\":\" <br> \"}" })
        Assert(SiteNoticeClient.Parse(json) is null);
    foreach (var json in new[] { "<html>maintenance</html>", "[]", "{\"enable\":\"true\",\"content\":\"bad\"}", "{\"enable\":true,\"content\":{}}" })
        Throws<JsonException>(() => SiteNoticeClient.Parse(json));
    Assert(SiteNoticeClient.Parse("""{"enable":true,"content":"<script>bad()</script>公告"}""")?.Content == "公告");
});
Test("notice suppression keys follow both version and content updates", () =>
{
    var original = SiteNoticeClient.Parse("""{"enable":true,"date":2026102100008,"content":"原公告"}""")!;
    var stringVersion = SiteNoticeClient.Parse("""{"enable":true,"date":"2026102100008","content":"原公告"}""")!;
    Assert(original.Key == stringVersion.Key && original.Key.Length == 64);
    Assert(original.Key != (original with { Content = "更新公告" }).Key);
    Assert(original.Key != (original with { Version = "2026102200001" }).Key);
});
AsyncTest("notice failures and redirects are rejected without retry", async () =>
{
    foreach (var status in new[] { HttpStatusCode.BadGateway, HttpStatusCode.TooManyRequests, HttpStatusCode.Redirect })
    {
        var calls = 0;
        using var client = new SiteNoticeClient(new HttpClient(new FakeHandler((_, _) => { calls++; return Task.FromResult(Response("unavailable", status)); })));
        await ThrowsAsync<IslandException>(() => client.GetAsync()); Assert(calls == 1);
    }
    using var invalid = new SiteNoticeClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response("<html>error</html>")))));
    await ThrowsAsync<JsonException>(() => invalid.GetAsync());
    using var large = new SiteNoticeClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response(new string('x', 65537))))));
    await ThrowsAsync<IslandException>(() => large.GetAsync());
});
AsyncTest("notice requests cancel when their owner closes", async () =>
{
    using var cancellation = new CancellationTokenSource();
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var client = new SiteNoticeClient(new HttpClient(new FakeHandler(async (_, ct) =>
    {
        started.SetResult(); await Task.Delay(Timeout.Infinite, ct); return Response("{}");
    })));
    var pending = client.GetAsync(cancellation.Token);
    await started.Task; cancellation.Cancel();
    await ThrowsAsync<OperationCanceledException>(() => pending);
});
Test("old notice state migration and suppression persistence", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "TideTests-" + Guid.NewGuid().ToString("N"));
    try
    {
        var store = new LocalStore(path);
        File.WriteAllText(Path.Combine(path, "state.json"), "{}");
        Assert(store.Load().SuppressedNoticeKeys.Count == 0);
        File.WriteAllText(Path.Combine(path, "state.json"), "{\"SuppressedNoticeKeys\":null}");
        Assert(store.Load().SuppressedNoticeKeys.Count == 0);
        var key = new SiteNotice("1", "公告").Key;
        var state = store.Load(); state.SuppressedNoticeKeys = [key, key.ToLowerInvariant(), "invalid", null!];
        store.Save(state); Assert(store.Load().SuppressedNoticeKeys.SequenceEqual(new[] { key }));
    }
    finally { Cleanup(path); }
});

AsyncTest("subscription pages use exact encoded ID and load until an empty page", async () =>
{
    var calls = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler((request, _) =>
    {
        calls++; var uri = request.RequestUri!;
        Assert(uri.AbsolutePath.EndsWith("/feed") && uri.Query.Contains("uuid=" + Uri.EscapeDataString(" 中文 AbC&x=1 ")));
        Assert(request.Headers.CacheControl is { NoCache: true, NoStore: true } && !request.Headers.Contains("Cookie"));
        return Task.FromResult(Response(calls switch { 1 => "[{\"id\":101,\"reply_count\":3}]", 2 => "[{\"id\":102}]", _ => "[]" }));
    })));
    var list = await api.GetSubscriptionsAsync(" 中文 AbC&x=1 ");
    Assert(list.Select(p => p.Id).SequenceEqual(new long[] { 101, 102 }) && list[0].ReplyCount == 3 && calls == 3);
});
AsyncTest("incomplete or repeating subscription pages never become a full snapshot", async () =>
{
    foreach (var second in new[] { "[{\"id\":1}]", "\"接口异常\"", "[null]", "[{\"id\":0}]" })
    {
        var calls = 0;
        using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => Task.FromResult(Response(++calls == 1 ? "[{\"id\":1}]" : second)))));
        await ThrowsAsync<IslandException>(() => api.GetSubscriptionsAsync("test-id")); Assert(calls == 2);
    }
});
AsyncTest("subscription writes use server success and never retry uncertain results", async () =>
{
    foreach (var response in new[] { "null", "\"失败\"", "<html>error</html>" })
    {
        var calls = 0;
        using var api = new XIslandApi(new HttpClient(new FakeHandler((request, _) =>
        {
            calls++; Assert(request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("addFeed"));
            return Task.FromResult(Response(response));
        })));
        await ThrowsAsync<SubscriptionUnknownException>(() => api.SetSubscriptionAsync("test-id", 101, true)); Assert(calls == 1);
    }
    using var success = new XIslandApi(new HttpClient(new FakeHandler((request, _) => Task.FromResult(Response(request.RequestUri!.AbsolutePath.EndsWith("addFeed") ? "\"订阅大成功→_→\"" : "\"取消订阅成功!\"")))));
    await success.SetSubscriptionAsync("test-id", 101, true); await success.SetSubscriptionAsync("test-id", 101, false);
});
AsyncTest("subscription transport errors hide identifiers and invalid input sends nothing", async () =>
{
    var calls = 0;
    using var api = new XIslandApi(new HttpClient(new FakeHandler((_, _) => { calls++; throw new HttpRequestException("https://api/?uuid=secret-id"); })));
    await ThrowsAsync<ArgumentException>(() => api.GetSubscriptionsAsync("\n"));
    await ThrowsAsync<ArgumentOutOfRangeException>(() => api.SetSubscriptionAsync("test", 0, true)); Assert(calls == 0);
    try { await api.GetSubscriptionsAsync("secret-id"); } catch (IslandException ex) { Assert(!ex.ToString().Contains("secret-id")); }
    await ThrowsAsync<SubscriptionUnknownException>(() => api.SetSubscriptionAsync("secret-id", 101, true)); Assert(calls == 2);
});

Test("subscription snapshots isolate number and cookie and reject expired or corrupt cache", () =>
{
    var path = Path.Combine(Path.GetTempPath(), "tide-sub-cache-" + Guid.NewGuid());
    var store = new LocalStore(path);
    var snapshot = new SubscriptionSnapshot([new Post { Id = 9 }], true, DateTimeOffset.UtcNow, [9]);
    store.SaveSubscriptions("中文 / ../ 号", "cookie-a", snapshot);
    Assert(store.ReadSubscriptions("中文 / ../ 号", "cookie-a") is { Complete: true, Posts.Count: 1, Uncertain.Count: 1 });
    Assert(store.ReadSubscriptions("中文 / ../ 号", "cookie-b") is null && store.ReadSubscriptions("other", "cookie-a") is null);
    Assert(Directory.GetFiles(Path.Combine(store.Cache.Root, "subscriptions")).Length == 1);
    var file = Directory.GetFiles(Path.Combine(store.Cache.Root, "subscriptions")).Single();
    store.SaveSubscriptions("expired", "", snapshot with { UpdatedAt = DateTimeOffset.UtcNow.AddDays(-31) });
    Assert(store.ReadSubscriptions("expired", "") is null);
    File.WriteAllText(file, "broken");
    Assert(store.ReadSubscriptions("中文 / ../ 号", "cookie-a") is null);
});

var failed = 0;
foreach (var check in checks)
{
    try { await check.Run(); Console.WriteLine("PASS " + check.Name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + check.Name + ": " + ex.Message); }
}
Console.WriteLine($"{checks.Count - failed}/{checks.Count} checks passed (mock transport; no live posts).");
return failed == 0 ? 0 : 1;

// 在 HttpMessageHandler 层拦截请求，既能断言表单和 Cookie，也能模拟断网或错误状态码。
sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}

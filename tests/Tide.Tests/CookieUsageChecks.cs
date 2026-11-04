using System.Text.Json;
using Tide.Core;

internal static class CookieUsageChecks
{
    internal static void Run()
    {
        var a = new CookieIdentity("a", "原名A", "opaque-a") { Remark = "常用" };
        var b = new CookieIdentity("b", "原名B", "opaque-b");
        var time = DateTimeOffset.Parse("2026-11-03T18:00:00+08:00");
        SentPost Reply(string id, string cookieId, string name, long thread, int minute) =>
            new(id, true, thread, 4, "综合版1", name, "", "", "正文", false, time.AddMinutes(minute)) { CookieId = cookieId };
        var posts = new[] { Reply("1", "a", a.Name, 101, 1), Reply("2", "a", a.Name, 101, 2),
            Reply("3", "b", b.Name, 101, 3), Reply("4", "a", a.Name, 202, 4),
            Reply("5", "a", a.Name, 101, 5) with { IsReply = false },
            Reply("6", "", a.Name, 101, 0), Reply("7", "removed-id", a.Name, 101, 9) };
        var result = CookieThreadUsage.ForThread([a, b], posts.Concat(posts.Take(1)), 101);
        Require(result["a"].ReplyCount == 3 && result["a"].LastUsed == time.AddMinutes(2) && !result["a"].IsLastUsed, "其他串/主题/重复/删除身份计入当前串");
        Require(result["b"].ReplyCount == 1 && result["b"].IsLastUsed, "上次使用不是当前串最新回应");
        Require(CookieThreadUsage.ForThread([a, b], posts, 0).Values.All(v => v.ReplyCount == 0 && !v.IsLastUsed), "发主题显示了串内回应统计");
        var renamed = CookieThreadUsage.ForThread([a with { Name = "更新原名", Remark = "新备注" }, b], posts, 101);
        Require(renamed["a"].ReplyCount == 2, "新记录没有按稳定 CookieId 关联");
        var duplicateName = CookieThreadUsage.ForThread([a, b with { Name = a.Name }], posts, 101);
        Require(duplicateName["a"].ReplyCount == 2 && duplicateName["b"].ReplyCount == 1, "歧义旧名称被错误关联");
        var old = JsonSerializer.Deserialize<SentPost>(JsonSerializer.Serialize(posts[0]).Replace("\"CookieId\":\"a\"", "\"Unused\":\"a\""))!;
        Require(old.CookieId == "" && CookieThreadUsage.ForThread([a], [old], 101)["a"].ReplyCount == 1, "旧发言记录不兼容");
        var oldCookie = JsonSerializer.Deserialize<CookieIdentity>("{\"Id\":\"x\",\"Name\":\"旧名称\",\"EncryptedValue\":\"opaque\"}")!;
        Require(oldCookie.Remark == "", "旧饼干资料的备注未初始化");
        var roundTrip = JsonSerializer.Deserialize<CookieIdentity>(JsonSerializer.Serialize(a))!;
        Require(roundTrip.Remark == "常用" && roundTrip.Name == a.Name && roundTrip.EncryptedValue == a.EncryptedValue, "备注改动覆盖原名称或凭据");
        Console.WriteLine("PASS: per-thread successful reply usage, stable cookie identity, unique-name legacy fallback, latest-in-thread marker, remark compatibility.");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
}

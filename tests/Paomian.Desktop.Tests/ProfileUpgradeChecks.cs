using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class ProfileUpgradeChecks
{
    // 固定 1.0.2 的 JSON 和缓存路径算法，不用当前模型序列化夹具，以免双方一起改错。
    internal static void Run()
    {
        var parent = Directory.CreateTempSubdirectory("Paomian-upgrade-");
        try
        {
            var root = Directory.CreateDirectory(Path.Combine(parent.FullName, "TideIsland")).FullName;
            var cacheParent = Path.Combine(parent.FullName, "separate cache");
            var attachment = Path.Combine(root, "draft attachment.bin"); File.WriteAllBytes(attachment, [1, 2, 3]);
            static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            static string Protect(string value) => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
            var cookie = Guid.NewGuid().ToString("N"); var number = Guid.NewGuid().ToString("N");
            var post = new { Id = 123, Content = "sample" };
            var state = JsonSerializer.Serialize(new {
                Theme = "dark", FontSize = 19, CacheParentPath = cacheParent,
                ActiveCookieId = "local-id", Cookies = new[] { new { Id = "local-id", Name = "test", EncryptedValue = Protect(cookie) } },
                ActiveSubscriptionId = "local-feed", SubscriptionAccounts = new[] { new { Id = "local-feed", Name = "test", EncryptedValue = Protect(number) } },
                History = new[] { new { Post = post, Page = 3, ReadAt = DateTimeOffset.UtcNow } },
                Drafts = new[] { new { Key = "reply", ForumId = 4, ThreadId = 123, Title = "", Content = "unsent", ImageFile = attachment, Watermark = false } }
            });
            var stateFile = Path.Combine(root, "state.json"); File.WriteAllText(stateFile, state);
            var sentFile = Path.Combine(root, "sent-posts.json");
            File.WriteAllText(sentFile, """[{"Id":"s1","IsReply":true,"ThreadId":123,"ForumId":4,"ForumName":"test","CookieName":"test","Title":"","Name":"","Content":"sent","HasImage":false,"SentAt":"2026-01-01T00:00:00Z"}]""");
            var key = Hash(root.ToUpperInvariant())[..16];
            var cache = Directory.CreateDirectory(Path.Combine(cacheParent, "TideCache-" + key)).FullName;
            File.WriteAllText(Path.Combine(cache, ".tide-cache"), key);
            var pages = Directory.CreateDirectory(Path.Combine(cache, Hash("local-id")[..20])).FullName;
            File.WriteAllText(Path.Combine(pages, "123-1.json"), JsonSerializer.Serialize(new { Original = post, Replies = Array.Empty<object>(), Page = 1 }));
            var feeds = Directory.CreateDirectory(Path.Combine(cache, "subscriptions")).FullName;
            File.WriteAllText(Path.Combine(feeds, Hash(JsonSerializer.Serialize(new[] { number, "local-id" })) + ".json"),
                JsonSerializer.Serialize(new { Posts = new[] { post }, Complete = true, UpdatedAt = DateTimeOffset.UtcNow, Uncertain = Array.Empty<long>() }));
            var before = Directory.EnumerateFiles(parent.FullName, "*", SearchOption.AllDirectories).ToDictionary(p => p, ReleaseUpdater.Hash);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var session = ProfileSession.TryOpen(parent.FullName);
                Require(session?.Root == root && !Directory.Exists(Path.Combine(parent.FullName, "PaomianIsland")), "legacy selection");
                var store = new LocalStore(session!.Root); var saved = store.Load(); store.ConfigureCache(saved);
                Require(store.LoadWarning is null && saved.Theme == "dark" && saved.FontSize == 19, "settings");
                Require(saved.ActiveCookieId == saved.Cookies.Single().Id && SecretProtection.Unprotect(saved.Cookies.Single().EncryptedValue) == cookie, "DPAPI cookie");
                Require(saved.ActiveSubscriptionId == saved.SubscriptionAccounts.Single().Id && SecretProtection.Unprotect(saved.SubscriptionAccounts.Single().EncryptedValue) == number, "DPAPI subscription");
                Require(saved.History.Single().Page == 3 && saved.Drafts.Single().Content == "unsent" && File.Exists(saved.Drafts.Single().ImageFile), "history/draft/attachment");
                Require(store.ReadSentPosts().Single().Content == "sent" && store.Cache.Root == cache && store.ReadPage(123, 1, "local-id")?.Original.Id == 123, "record/external cache");
                Require(store.ReadSubscriptions(number, "local-id")?.Posts.Single().Id == 123 && store.ReadSubscriptions(number, "") is null, "subscription isolation");
            }
            Require(Directory.GetFiles(parent.FullName, "*", SearchOption.AllDirectories).Length == before.Count && before.All(f => ReleaseUpdater.Hash(f.Key) == f.Value), "original bytes");
            Console.WriteLine("PASS 1.0.2 profile in-place, two launches, current-user DPAPI, external cache, records and original hashes");
        }
        finally { parent.Delete(true); }
    }
    private static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
}

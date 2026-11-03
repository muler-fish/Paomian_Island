using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tide.Core;

public sealed partial class LocalStore
{
    // 只保存可刷新的列表快照，30 天后丢弃；号码本身仍由 state.json 加密保存。
    public SubscriptionSnapshot? ReadSubscriptions(string number, string cookieId)
    {
        var key = SubscriptionPath(number, cookieId);
        try
        {
            var bytes = Cache.Read(key);
            if (bytes is null) return null;
            var snapshot = JsonSerializer.Deserialize<SubscriptionSnapshot>(bytes);
            if (snapshot is null || snapshot.Posts is null || snapshot.Uncertain is null ||
                snapshot.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-30) || snapshot.Posts.Any(p => p is null || p.Id <= 0)) return null;
            return snapshot;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void SaveSubscriptions(string number, string cookieId, SubscriptionSnapshot snapshot)
    {
        Cache.Write(SubscriptionPath(number, cookieId), JsonSerializer.SerializeToUtf8Bytes(snapshot));
    }


    private string SubscriptionPath(string number, string cookieId)
    {
        // 摘要防止任意号码成为文件路径；饼干身份也参与隔离，避免展示其他身份的受限内容。
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { number, cookieId }))));
        return "subscriptions/" + key + ".json";
    }
}

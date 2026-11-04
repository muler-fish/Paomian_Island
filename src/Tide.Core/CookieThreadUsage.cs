namespace Tide.Core;

/// <summary>仅统计当前串内、本机确认发送成功的回应；主题与其他串均不参与。</summary>
public sealed record CookieThreadUsage(int ReplyCount, DateTimeOffset? LastUsed, bool IsLastUsed)
{
    public static IReadOnlyDictionary<string, CookieThreadUsage> ForThread(
        IEnumerable<CookieIdentity> cookies, IEnumerable<SentPost> posts, long threadId)
    {
        var identities = cookies.ToArray();
        var validIds = identities.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var oldNames = identities.GroupBy(c => c.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single().Id, StringComparer.Ordinal);
        var replies = new Dictionary<string, List<SentPost>>(StringComparer.Ordinal);
        if (threadId > 0)
            foreach (var post in posts.Where(p => p.IsReply && p.ThreadId == threadId).DistinctBy(p => p.Id))
            {
                var id = post.CookieId;
                if (string.IsNullOrEmpty(id)) oldNames.TryGetValue(post.CookieName, out id);
                if (id is null || !validIds.Contains(id)) continue;
                if (!replies.TryGetValue(id, out var list)) replies[id] = list = [];
                list.Add(post);
            }
        var last = replies.SelectMany(pair => pair.Value.Select(p => (CookieId: pair.Key, Post: p)))
            .OrderByDescending(p => p.Post.SentAt).ThenBy(p => p.Post.Id, StringComparer.Ordinal).FirstOrDefault();
        return identities.ToDictionary(c => c.Id, c => replies.TryGetValue(c.Id, out var own)
            ? new CookieThreadUsage(own.Count, own.Max(p => p.SentAt), last.CookieId == c.Id)
            : new CookieThreadUsage(0, null, false), StringComparer.Ordinal);
    }
}

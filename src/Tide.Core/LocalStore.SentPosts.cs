using System.Text.Json;

namespace Tide.Core;

public sealed partial class LocalStore
{
    // 发言记录不是可丢弃的浏览缓存，独立落盘，避免每次调整设置都重写所有正文。
    public List<SentPost> ReadSentPosts()
    {
        lock (_gate)
        {
            var path = Path.Combine(_root, "sent-posts.json");
            if (!File.Exists(path)) return [];
            try
            {
                var records = JsonSerializer.Deserialize<List<SentPost>>(File.ReadAllText(path), Options);
                if (records is null || records.Any(p => p is null || string.IsNullOrWhiteSpace(p.Id) || p.Content is null ||
                    p.Name is null || p.Title is null || p.CookieName is null || p.ForumName is null))
                    throw new JsonException("发言记录缺少必要字段。");
                return records.DistinctBy(p => p.Id).OrderByDescending(p => p.SentAt).ToList();
            }
            catch (JsonException ex) { throw new IOException("本机发言记录无法读取，原文件已保留。", ex); }
        }
    }

    public void AddSentPost(SentPost post)
    {
        lock (_gate)
        {
            var posts = ReadSentPosts();
            if (posts.Any(p => p.Id == post.Id)) return;
            posts.Insert(0, post);
            var path = Path.Combine(_root, "sent-posts.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(posts, Options));
            File.Move(path + ".tmp", path, true);
        }
    }

    public void SetSentThreadId(string recordId, long threadId)
    {
        if (threadId <= 0) throw new ArgumentOutOfRangeException(nameof(threadId));
        lock (_gate)
        {
            var posts = ReadSentPosts();
            var index = posts.FindIndex(p => p.Id == recordId && !p.IsReply && p.ThreadId is not > 0);
            if (index < 0) return;
            posts[index] = posts[index] with { ThreadId = threadId };
            var path = Path.Combine(_root, "sent-posts.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(posts, Options));
            File.Move(path + ".tmp", path, true);
        }
    }
}

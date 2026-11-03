using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tide.Core;

public sealed partial class XIslandApi
{
    /// <summary>成功发串后的只读补查。始终使用发送时的身份，不能随当前浏览饼干切换。</summary>
    public async Task<long?> FindSentThreadAsync(SentPost sent, string cookie, CancellationToken ct = default)
    {
        if (sent.IsReply || sent.ThreadId is > 0) return sent.ThreadId;
        cookie = ContentText.NormalizeCookie(cookie);
        // getLastPost 在主站，返回最后一次发言（可能是回复），不是发串表单的返回值。
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Site + "/Api/getLastPost");
            request.Headers.Add("Cookie", "userhash=" + cookie);
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && int.TryParse(Str(root, "resto"), out var resto) && resto == 0)
                {
                    var last = ParsePost(root);
                    if (MatchesSentPost(last, sent))
                    {
                        // 最近发言接口不带 fid；读取主楼核对板块，避免认领另一板块的相同发言。
                        using var thread = await GetJsonAsync($"thread?id={last.Id}&page=1", ct, cookie);
                        var original = ParsePost(thread.RootElement);
                        if (original.Id == last.Id && original.Fid == sent.ForumId && original.HasImage == sent.HasImage && MatchesSentPost(original, sent)) return last.Id;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IslandException or JsonException) { }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }

        // 已在别处发言或最近发言接口不可用时，只检查原板块最近两页，拒绝模糊/多重匹配。
        var matches = new HashSet<long>();
        for (var page = 1; page <= 2; page++)
        {
            using var doc = await GetJsonAsync($"showf?id={sent.ForumId}&page={page}", ct, cookie);
            EnsureArray(doc.RootElement);
            if (doc.RootElement.GetArrayLength() == 0) break;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var post = ParsePost(item);
                if (post.Fid == sent.ForumId && post.HasImage == sent.HasImage && MatchesSentPost(post, sent)) matches.Add(post.Id);
            }
        }
        return matches.Count == 1 ? matches.Single() : null;
    }

    private static bool MatchesSentPost(Post post, SentPost sent)
    {
        if (post.Id <= 0 || post.IsSiteTip || post.UserHash != sent.CookieName || sent.CookieName.Length == 0) return false;
        var time = Regex.Replace(post.Time, @"\s*(?:\([^()\r\n]*\)|（[^（）\r\n]*）)\s*", " ");
        if (!DateTime.TryParseExact(time, ["yyyy-M-d H:mm:ss", "yyyy/M/d H:mm:ss"], CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var date)) return false;
        // X 岛时间按 UTC+8 解释，不依赖用户的 Windows 时区；仅允许发送前后两分钟。
        if ((new DateTimeOffset(date, TimeSpan.FromHours(8)) - sent.SentAt).Duration() > TimeSpan.FromMinutes(2)) return false;
        static string Optional(string value, string defaultValue) => value.Trim() == defaultValue ? "" : value.Trim();
        if (Optional(StoredText(post.Title), "无标题") != Optional(sent.Title, "无标题") ||
            Optional(StoredText(post.Name), "无名氏") != Optional(sent.Name, "无名氏")) return false;
        return SameSentContent(sent.Content, StoredText(post.Content));
    }

    // 服务端 HTML 只解码一层，保留用户原本写的 <b> 等字面文本。
    private static string StoredText(string html)
    {
        var text = Regex.Replace(html.Replace("\r\n", "\n"), @"<\s*br\s*/?\s*>[\t ]*\n?", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"</\s*(?:p|div)\s*>", "\n", RegexOptions.IgnoreCase);
        return WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]+>", "")).Trim();
    }

    private static bool SameSentContent(string input, string stored)
    {
        input = input.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (input == stored) return true;
        // 骰子由服务端在 [首项,尾项] 前加结果；只容许区间内数字，其他正文仍须逐字相符。
        var dice = Regex.Matches(input, @"\[(-?\d+),(-?\d+)\]");
        if (dice.Count == 0) return false;
        var pattern = new StringBuilder("\\A"); var offset = 0;
        foreach (Match token in dice)
        {
            pattern.Append(Regex.Escape(input[offset..token.Index])).Append(@"(?<roll>-?\d+)").Append(Regex.Escape(token.Value));
            offset = token.Index + token.Length;
        }
        pattern.Append(Regex.Escape(input[offset..])).Append("\\z");
        var match = Regex.Match(stored, pattern.ToString(), RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) return false;
        // 每个骰子按位置校验；混合原样/掷出结果的响应保守拒绝，稍后仍可补查。
        var rolls = match.Groups["roll"].Captures;
        if (rolls.Count != dice.Count) return false;
        for (var i = 0; i < dice.Count; i++)
        {
            if (!long.TryParse(rolls[i].Value, out var value) || !long.TryParse(dice[i].Groups[1].Value, out var first) ||
                !long.TryParse(dice[i].Groups[2].Value, out var last) || value < Math.Min(first, last) || value > Math.Max(first, last)) return false;
        }
        return true;
    }
}

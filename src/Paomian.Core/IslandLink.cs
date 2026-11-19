using System.Text.RegularExpressions;

namespace Paomian.Core;

public enum IslandLinkKind { Web, Thread, Forum, Timeline }

/// <summary>只识别明确的站点与只读页面路径；未知网页保留完整地址供浏览器打开。</summary>
public sealed record IslandLink(Uri Url, IslandLinkKind Kind = IslandLinkKind.Web, int Id = 0,
    string Name = "", int Page = 1, long? ReplyId = null)
{
    public static bool TryWebUri(string value, out Uri uri)
    {
        uri = null!;
        var text = value.Trim();
        if (text.Length == 0 || text.Any(char.IsControl) || text.Contains('\\')) return false;
        if (text.StartsWith("//")) text = "https:" + text;
        else if (text.StartsWith('/')) text = XIslandApi.Site + text;
        else if (Regex.IsMatch(text, @"^(?:t/|f/|(?:Home/)?Forum/)", RegexOptions.IgnoreCase)) text = XIslandApi.Site + "/" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out uri!) && uri.Scheme is "http" or "https" && uri.Host.Length > 0;
    }

    public static IslandLink? Parse(string value)
    {
        if (!TryWebUri(value, out var uri)) return null;
        var web = new IslandLink(uri);
        // 非默认端口或含用户信息的地址不接管，也不将相似域名或子域名当本站。
        if (!ContentText.IsIslandHost(uri.Host) || !uri.IsDefaultPort || uri.UserInfo.Length != 0) return web;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var path = uri.AbsolutePath.TrimEnd('/');
        var match = Regex.Match(path, @"^/(?:Home/)?Forum/(?<kind>thread|showf|timeline)/id/(?<id>[0-9]+)(?:/page/(?<page>[0-9]+))?(?:\.html)?$", RegexOptions.IgnoreCase);
        string kind, key; string? pathPage = null;
        if (match.Success)
        { kind = match.Groups["kind"].Value.ToLowerInvariant(); key = match.Groups["id"].Value; pathPage = match.Groups["page"].Success ? match.Groups["page"].Value : null; }
        else
        {
            match = Regex.Match(path, @"^/(?<kind>t|f)/(?<key>.+?)(?:/page/(?<page>[0-9]+))?(?:\.html)?$", RegexOptions.IgnoreCase);
            if (match.Success)
            { kind = match.Groups["kind"].Value.ToLowerInvariant(); key = Uri.UnescapeDataString(match.Groups["key"].Value); pathPage = match.Groups["page"].Success ? match.Groups["page"].Value : null; }
            else
            {
                match = Regex.Match(path, @"^/(?:Home/)?Forum/(?<kind>thread|showf|timeline)(?:\.html)?$", RegexOptions.IgnoreCase);
                if (!match.Success) return web;
                kind = match.Groups["kind"].Value.ToLowerInvariant(); key = query["id"] ?? "";
            }
        }
        var pageText = query["page"] ?? pathPage;
        var page = 1;
        if (pageText is not null && !Positive(pageText, out page)) return web;
        if (pathPage is not null && query["page"] is not null && pathPage != query["page"]) return web;
        long? reply = null;
        if (query["r"] is { } replyText)
        { if (!Positive(replyText, out var r)) return web; reply = r; }
        if (uri.Fragment.Length > 0)
        {
            var anchor = Regex.Match(uri.Fragment, @"^#(?:r|No\.)?([0-9]+)$", RegexOptions.IgnoreCase);
            if (!anchor.Success || !Positive(anchor.Groups[1].Value, out var r) || reply is not null && reply != r) return web;
            reply = r;
        }
        if (kind == "f" && !Positive(key, out _))
            return reply is null && key.Length > 0 && !key.Contains('%') ? new(uri, IslandLinkKind.Forum, Name: key, Page: page) : web;
        if (!Positive(key, out var id)) return web;
        if (query["id"] is { } queryId && queryId != key) return web;
        var type = kind switch { "t" or "thread" => IslandLinkKind.Thread, "timeline" => IslandLinkKind.Timeline, _ => IslandLinkKind.Forum };
        if (reply is not null && type != IslandLinkKind.Thread) return web;
        return new(uri, type, id, Page: page, ReplyId: reply);
    }

    public Forum? FindForum(IEnumerable<Forum> forums)
    {
        if (Kind is not (IslandLinkKind.Forum or IslandLinkKind.Timeline)) return null;
        // 优先用接口原名（网页 URL 使用 name，导航栏使用 showName），不用显示名称猜 ID。
        var candidates = forums.Where(f => f.IsTimeline == (Kind == IslandLinkKind.Timeline)).ToArray();
        if (Id > 0) return candidates.SingleOrDefault(f => f.Id == Id);
        var matches = candidates.Where(f => f.UrlName == Name || f.Name == Name).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool Positive(string value, out int number) => int.TryParse(value, System.Globalization.NumberStyles.None,
        System.Globalization.CultureInfo.InvariantCulture, out number) && number > 0;
}

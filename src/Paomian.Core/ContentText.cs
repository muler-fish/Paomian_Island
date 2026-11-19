using System.Net;
using System.Text.RegularExpressions;

namespace Paomian.Core;

/// <summary>与界面无关的文本处理：整理正文、生成摘要、识别串号和校验饼干输入。</summary>
public static partial class ContentText
{
    /// <summary>兼容原始 HTML 和整体转义过的 HTML；结果仍是数据，不能交给浏览器或 XAML 解析器执行。</summary>
    public static string Normalize(string? html)
    {
        var text = html ?? "";
        // 仅在检测到转义标签且没有真实标签时解码一层，避免把普通正文反复解码成标签。
        if (!Regex.IsMatch(text, @"<\s*(br|p|span|font|a|b|strong)\b", RegexOptions.IgnoreCase) &&
            Regex.IsMatch(text, @"&lt;\s*(br|p|span|font|a|b|strong)\b", RegexOptions.IgnoreCase))
            text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"<(script|style)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
        text = text.Replace("\r\n", "\n");
        // API 常用 <br /> + 换行符排版 HTML；两者表示同一次换行，不能在富文本中重复绘制。
        // 只移除紧随换行标签的一次源码换行，保留下一行缩进、连续 br 和纯文本空行。
        return Regex.Replace(text, @"(<\s*br\s*/?\s*>|</\s*(?:p|div)\s*>)[\t ]*\n", "$1", RegexOptions.IgnoreCase);
    }

    /// <summary>去掉标签并保留基本换行；列表和草稿摘要传入 hideSpoilers，替换隐藏段落。</summary>
    public static string Plain(string? html, bool hideSpoilers = false)
    {
        var text = Normalize(html);
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>|</\s*(p|div)\s*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        if (hideSpoilers) text = Regex.Replace(text, @"\[h\][\s\S]*?\[/h\]", "[隐藏内容]", RegexOptions.IgnoreCase);
        return Regex.Replace(text, @"\n[\t ]*\n(?:[\t ]*\n)+", "\n\n").Trim();
    }

    /// <summary>识别纯串号、No. 引用及本站的新旧串链接；无法识别时返回 null。</summary>
    public static long? ParseThreadId(string input)
    {
        var s = input.Trim();
        if (long.TryParse(s, out var value) && value > 0 && value <= int.MaxValue) return value;
        var match = Regex.Match(s, @"^(?:>>\s*)?(?:No\.)?(\d{1,10})$", RegexOptions.IgnoreCase);
        if (match.Success && long.TryParse(match.Groups[1].Value, out value) && value > 0 && value <= int.MaxValue) return value;
        if (IslandLink.Parse(s) is { Kind: IslandLinkKind.Thread } link) return link.Id;
        return null;
    }

    public static bool IsIslandHost(string host) => new[] { "nmbxd.com", "www.nmbxd.com", "nmbxd1.com", "www.nmbxd1.com" }.Contains(host.ToLowerInvariant());

    /// <summary>从原始值、Cookie 头或 JSON 中提取完整 userhash；只校验输入格式，不验证服务端权限。</summary>
    public static string NormalizeCookie(string input)
    {
        var raw = input.Trim();
        if (raw.StartsWith('{'))
        {
            using var doc = System.Text.Json.JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("cookie", out var cookie) && !doc.RootElement.TryGetProperty("userhash", out cookie))
                throw new ArgumentException("JSON 中需要包含 cookie 或 userhash 字段。");
            raw = cookie.GetString() ?? "";
        }
        var match = Regex.Match(raw, @"(?:^|;\s*)userhash=([^;]+)", RegexOptions.IgnoreCase);
        if (match.Success) raw = match.Groups[1].Value;
        raw = raw.Trim();
        // 拒绝换行、空白和 Cookie 分隔符，防止输入被拼接成额外请求头或其他 Cookie。
        if (raw.Length < 8 || raw.Length > 4096 || raw.Any(c => c <= 32 || c >= 127 || c is ';' or ',' or '"' or '\\'))
            throw new ArgumentException("请输入完整的 userhash 值，不能是显示在串里的 7 位饼干 ID。请去除空格、换行和多余字段。");
        // 站点发放的值已经 URL 编码；保留 % 转义，解码或重复编码都会改变认证值。
        return raw;
    }
}

using System.Globalization;
using System.Text.RegularExpressions;

namespace Paomian.Core;

/// <summary>统一帖子时间的显示格式；只整理文本，不转换时区或修改服务端原值。</summary>
public static class PostTime
{
    private static readonly string[] SecondsFormats = ["yyyy-M-d H:mm:ss", "yyyy/M/d H:mm:ss"];
    private static readonly string[] MinutesFormats = ["yyyy-M-d H:mm", "yyyy/M/d H:mm"];
    private static readonly string[] DateFormats = ["yyyy-M-d", "yyyy/M/d"];

    public static string Format(string? value, bool includeSeconds = false, int? currentYear = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        // 站点常用 2026-10-27(二)20:38:07；括号中的星期不影响实际日期。
        var text = Regex.Replace(value.Trim(), @"\s*(?:\([^()\r\n]*\)|（[^（）\r\n]*）)\s*", " ");
        var hasSeconds = DateTime.TryParseExact(text, SecondsFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date);
        var hasTime = hasSeconds || DateTime.TryParseExact(text, MinutesFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date);
        if (!hasTime && !DateTime.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)) return value;
        var format = date.Year == (currentYear ?? DateTime.Now.Year) ? "MM'/'dd" : "yyyy'/'MM'/'dd";
        if (hasTime) format += includeSeconds && hasSeconds ? " HH':'mm':'ss" : " HH':'mm";
        // 使用公历、固定斜杠和 24 小时制，不随系统区域设置改成别的日期分隔符。
        return date.ToString(format, CultureInfo.InvariantCulture);
    }
}

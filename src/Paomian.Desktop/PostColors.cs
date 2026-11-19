using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;
using Paomian.Core;

namespace Paomian.Desktop;

internal static class PostColors
{
    internal static void Apply(Span span, string token)
    {
        if (!token.Contains("color", StringComparison.OrdinalIgnoreCase)) return;
        var css = Attribute(token, "style");
        var declarations = Regex.Matches(css, @"(?:^|;)\s*color\s*:\s*(?<value>[^;]+)", RegexOptions.IgnoreCase);
        var color = declarations.Select(m => Parse(m.Groups["value"].Value)).LastOrDefault(c => c.HasValue)
            ?? Parse(Attribute(token, "color"));
        if (color is not { } source) return;
        var brush = new SolidColorBrush(source);
        brush.Freeze();
        span.Foreground = brush;
    }

    private static string Attribute(string token, string name)
    {
        // 先解析属性边界，避免将 title、background-color 等字段误认为正文前景色。
        var attributes = Regex.Matches(token, @"\s(?<name>[\w:-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))");
        var attribute = attributes.FirstOrDefault(a => a.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase));
        return attribute is null ? "" : WebUtility.HtmlDecode(attribute.Groups["value"].Value);
    }

    private static Color? Parse(string value)
    {
        value = Regex.Replace(value, @"\s*!important\s*$", "", RegexOptions.IgnoreCase).Trim();
        if (Regex.IsMatch(value, @"^#[0-9a-f]{3}$", RegexOptions.IgnoreCase))
            value = "#" + string.Concat(value.Skip(1).Select(c => new string(c, 2)));
        if (value.StartsWith('#') && LocalStore.HexColor(value) is { Length: > 0 } hex)
            return (Color)ColorConverter.ConvertFromString(hex);
        var rgb = Regex.Match(value, @"^rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)$", RegexOptions.IgnoreCase);
        if (rgb.Success && byte.TryParse(rgb.Groups[1].Value, out var r) && byte.TryParse(rgb.Groups[2].Value, out var g) &&
            byte.TryParse(rgb.Groups[3].Value, out var b)) return Color.FromRgb(r, g, b);
        if (!Regex.IsMatch(value, @"^[a-z]{3,22}$", RegexOptions.IgnoreCase)) return null;
        // CSS 的 green / lime 分别对应 #008000 / #00FF00。
        try { return ColorConverter.ConvertFromString(value) is Color { A: 255 } color ? color : null; }
        catch (FormatException) { return null; }
    }
}

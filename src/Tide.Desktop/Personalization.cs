using System.Windows;
using System.Windows.Media;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>主题强调色与身份标识颜色：由用户选的一个主色按浅色/深色主题生成整组语义画刷，覆盖在应用资源上。未自定义时不覆盖，保持主题文件原值。</summary>
internal static class Personalization
{
    internal sealed record Preset(string Name, string Light, string Dark);
    internal enum Identity { Cookie, Admin, Sage, Po }

    // 第一项是默认绿；预设同时给出深色主题的取值，自定义颜色的深色取值由程序生成。
    internal static readonly Preset[] Presets =
    [
        new("默认绿", "#286D59", "#94BBA9"),
        new("湖蓝", "#2F6690", "#9DBBD6"),
        new("靛紫", "#5B57A2", "#B2B0E0"),
        new("玫红", "#A3456B", "#DCA7BD"),
        new("赭石", "#94571F", "#D9B08C"),
        new("石墨", "#4B5560", "#B5BEC7"),
    ];
    internal const string DefaultAccent = "#286D59";
    // 各标识的默认主色（0.3.12 按用户当时的设置定为默认）；主题文件里的默认标签颜色就是由它们按浅色/深色生成的。
    internal static string DefaultIdentity(Identity identity) => identity switch
    {
        Identity.Cookie => "#286D59", Identity.Admin => "#FF0000", Identity.Sage => "#FF0000", _ => "#0084FF"
    };
    internal static string Label(Identity identity) => identity switch
    {
        Identity.Cookie => "饼干 ID", Identity.Admin => "Admin（红名）", Identity.Sage => "SAGE", _ => "PO"
    };

    private static readonly string[] AccentKeys = ["Accent", "OnAccent", "AccentHover", "AccentPressed", "AccentSoft", "Selected", "SelectionLine"];
    private static readonly Color LightSurface = Rgb("#FFFFFF"), DarkSurface = Rgb("#1D1F22");
    private static string _accent = "";
    private static IdentityColors _identity = new();

    /// <summary>从用户状态读取当前颜色；随后调用 <see cref="App.ApplyTheme"/> 或 <see cref="App.ApplyPersonalization"/> 生效。</summary>
    internal static void Load(UserState state) { _accent = state.AccentColor; _identity = state.IdentityColors ?? new(); }

    /// <summary>WPF UI 控件使用的强调色（与 Accent 画刷一致）。</summary>
    internal static Color AccentFor(bool dark) => AccentPalette(_accent, dark)[0];

    internal static void Apply(ResourceDictionary resources, bool dark)
    {
        foreach (var key in AccentKeys.Concat(Enum.GetValues<Identity>().SelectMany(IdentityKeys))) resources.Remove(key);
        if (_accent.Length > 0)
        {
            var palette = AccentPalette(_accent, dark);
            for (var i = 0; i < AccentKeys.Length; i++) resources[AccentKeys[i]] = Brush(palette[i]);
        }
        foreach (var identity in Enum.GetValues<Identity>())
        {
            var hex = Get(_identity, identity);
            if (hex.Length == 0) continue;
            var (foreground, background) = IdentityPalette(hex, dark);
            var keys = IdentityKeys(identity);
            resources[keys[0]] = Brush(foreground); resources[keys[1]] = Brush(background);
        }
    }

    internal static string Get(IdentityColors colors, Identity identity) => identity switch
    {
        Identity.Cookie => colors.Cookie, Identity.Admin => colors.Admin, Identity.Sage => colors.Sage, _ => colors.Po
    };
    internal static IdentityColors With(IdentityColors colors, Identity identity, string hex) => identity switch
    {
        Identity.Cookie => colors with { Cookie = hex }, Identity.Admin => colors with { Admin = hex }, Identity.Sage => colors with { Sage = hex }, _ => colors with { Po = hex }
    };
    // 文字色、底色对应的资源键，与 PostIdentity.xaml 和串列表 SAGE 标签一致。
    private static string[] IdentityKeys(Identity identity) => identity switch
    {
        Identity.Cookie => ["CookieForeground", "CookieBackground"],
        Identity.Admin => ["AdminCookieForeground", "AdminCookieBackground"],
        Identity.Sage => ["OnSage", "SageBackground"],
        _ => ["PoForeground", "PoBackground"]
    };

    /// <summary>顺序与 AccentKeys 一致：强调色、其上文字、悬停、按下、浅底、选中行、选中线。</summary>
    internal static Color[] AccentPalette(string hex, bool dark)
    {
        var light = Rgb(hex.Length > 0 ? hex : DefaultAccent);
        var preset = Presets.FirstOrDefault(p => p.Light.Equals(hex.Length > 0 ? hex : DefaultAccent, StringComparison.OrdinalIgnoreCase));
        if (dark)
        {
            // 深色主题用较亮、较柔和的同色相，并保证在面板底色上至少 4.5:1。
            var accent = EnsureContrast(preset is null ? DarkVariant(light) : Rgb(preset.Dark), DarkSurface, 4.5, lighten: true);
            var on = Mix(accent, Colors.Black, .82);
            return [accent, on, Mix(accent, Colors.White, .15), Mix(accent, Colors.Black, .13), Mix(DarkSurface, accent, .13), Mix(DarkSurface, accent, .1), Mix(accent, Colors.Black, .12)];
        }
        else
        {
            var accent = EnsureContrast(light, LightSurface, 4.5, lighten: false);
            var on = Contrast(Colors.White, accent) >= 4.5 ? Colors.White : Rgb("#14181A");
            return [accent, on, Mix(accent, Colors.White, .1), Mix(accent, Colors.Black, .15), Mix(LightSurface, accent, .12), Mix(LightSurface, accent, .08), Mix(accent, Colors.White, .1)];
        }
    }

    /// <summary>标签文字色与底色：底色是主色的浅（深）色调，文字色在底色上至少 4.5:1。</summary>
    internal static (Color Foreground, Color Background) IdentityPalette(string hex, bool dark)
    {
        var color = Rgb(hex);
        if (!dark)
        {
            var background = Mix(LightSurface, color, .11);
            return (EnsureContrast(color, background, 4.5, lighten: false), background);
        }
        // 深色主题：保留用户选的饱和度（只去掉最刺眼的荧光感），亮度只抬到深色底上能读清的程度；
        // 底色混入 26% 的主色，标签能看出颜色。最后按对比度再微调文字亮度。
        var (h, s, l) = ToHsl(color);
        var foreground = FromHsl(h, Math.Min(s, .9), Math.Max(l, .62));
        var darkBackground = Mix(DarkSurface, color, .26);
        return (EnsureContrast(foreground, darkBackground, 4.5, lighten: true), darkBackground);
    }

    internal static Color Rgb(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    internal static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    private static SolidColorBrush Brush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    internal static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount), (byte)Math.Round(from.G + (to.G - from.G) * amount), (byte)Math.Round(from.B + (to.B - from.B) * amount));
    internal static double Contrast(Color a, Color b)
    {
        double L(Color c)
        {
            static double Channel(byte v) { var x = v / 255d; return x <= .03928 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
            return .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        }
        var (la, lb) = (L(a), L(b));
        return (Math.Max(la, lb) + .05) / (Math.Min(la, lb) + .05);
    }
    // 按 HSL 亮度逐步调整，直到达到对比度；色相不变。
    private static Color EnsureContrast(Color color, Color background, double ratio, bool lighten)
    {
        var (h, s, l) = ToHsl(color);
        for (var i = 0; i < 60 && Contrast(color, background) < ratio; i++)
        {
            l = Math.Clamp(l + (lighten ? .015 : -.015), 0, 1);
            color = FromHsl(h, s, l);
        }
        return color;
    }
    private static Color DarkVariant(Color color)
    {
        var (h, s, _) = ToHsl(color);
        return FromHsl(h, Math.Min(s * .6, .45), .68);
    }
    /// <summary>HSV：H 为 0–360，S、V 为 0–1；调色板横向是饱和度、纵向是明度。</summary>
    internal static (double H, double S, double V) ToHsv(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        var h = d < 1e-9 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
        return ((h + 360) % 360, max <= 0 ? 0 : d / max, max);
    }
    internal static Color FromHsv(double h, double s, double v)
    {
        h = (h % 360 + 360) % 360; s = Math.Clamp(s, 0, 1); v = Math.Clamp(v, 0, 1);
        var c = v * s; var x = c * (1 - Math.Abs(h / 60 % 2 - 1)); var m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    internal static (double H, double S, double L) ToHsl(Color color)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        var s = l > .5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }
    internal static Color FromHsl(double h, double s, double l)
    {
        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            return t < 1d / 6 ? p + (q - p) * 6 * t : t < .5 ? q : t < 2d / 3 ? p + (q - p) * (2d / 3 - t) * 6 : p;
        }
        h = (h % 360 + 360) % 360 / 360;
        if (s <= 0) { var v = (byte)Math.Round(l * 255); return Color.FromRgb(v, v, v); }
        var q = l < .5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - q;
        return Color.FromRgb((byte)Math.Round(Hue(p, q, h + 1d / 3) * 255), (byte)Math.Round(Hue(p, q, h) * 255), (byte)Math.Round(Hue(p, q, h - 1d / 3) * 255));
    }
}

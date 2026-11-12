using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Tide.Core;

namespace Tide.Desktop;

internal sealed class PostColors : INotifyPropertyChanged, IValueConverter
{
    internal const string RedSource = "#FF0000", GreenSource = "#789922";
    private static readonly PostColors Palette = new();
    private readonly Dictionary<Color, SolidColorBrush> _brushes = new();
    private PostColorOptions _options = new();
    private bool _dark;
    public int Revision { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    internal static void Load(PostColorOptions options)
    {
        if (Palette._options == options) return;
        Palette._options = options;
        Palette.Refresh();
    }

    internal static void ApplyTheme(bool dark)
    {
        Palette._dark = dark;
        Palette.Refresh();
    }

    internal static void Apply(Span span, string token)
    {
        if (!token.Contains("color", StringComparison.OrdinalIgnoreCase)) return;
        var css = Attribute(token, "style");
        var declarations = Regex.Matches(css, @"(?:^|;)\s*color\s*:\s*(?<value>[^;]+)", RegexOptions.IgnoreCase);
        var color = declarations.Select(m => Parse(m.Groups["value"].Value)).LastOrDefault(c => c.HasValue)
            ?? Parse(Attribute(token, "color"));
        if (color is not { } source) return;
        BindingOperations.SetBinding(span, TextElement.ForegroundProperty, new Binding(nameof(Revision))
        {
            Source = Palette, Converter = Palette, ConverterParameter = source, Mode = BindingMode.OneWay
        });
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
        if (LocalStore.HexColor(value) is { Length: > 0 } hex && value.StartsWith('#')) return Personalization.Rgb(hex);
        var rgb = Regex.Match(value, @"^rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)$", RegexOptions.IgnoreCase);
        if (rgb.Success && byte.TryParse(rgb.Groups[1].Value, out var r) && byte.TryParse(rgb.Groups[2].Value, out var g) &&
            byte.TryParse(rgb.Groups[3].Value, out var b)) return Color.FromRgb(r, g, b);
        if (!Regex.IsMatch(value, @"^[a-z]{3,22}$", RegexOptions.IgnoreCase)) return null;
        // CSS 的 green / lime 分别对应 #008000 / #00FF00。
        try { return ColorConverter.ConvertFromString(value) is Color { A: 255 } color ? color : null; }
        catch (FormatException) { return null; }
    }

    internal static Color Display(Color source) => Palette.Resolve(source);

    private Color Resolve(Color source)
    {
        if (SystemParameters.HighContrast) return SystemColors.WindowTextColor;
        var key = Personalization.Hex(source) switch
        {
            "#FF0000" or "#CC0000" or "#C00000" => "PostRed",
            "#789922" or "#008000" or "#00FF00" or "#00CC00" or "#009900" => "PostGreen",
            _ => null
        };
        var custom = key switch { "PostRed" => _options.Red, "PostGreen" => _options.Green, _ => "" };
        var original = custom.Length > 0 ? Personalization.Rgb(custom) : source;
        if (custom.Length == 0 && key is not null && _options.Mode == PostColorOptions.Default &&
            Application.Current?.TryFindResource(key) is SolidColorBrush themed) return themed.Color;
        var (h, s, l) = Personalization.ToHsl(original);
        if (_dark && _options.Mode == PostColorOptions.Vivid && s > .12)
            original = Personalization.FromHsl(h, Math.Max(s, .74), Math.Clamp(l, .60, .79));
        // 选中的列表行比阅读底色更接近文字亮度，按较严格的背景保证两个区域都可读。
        var background = Personalization.Rgb(_dark ? "#333638" : "#E7EBE8");
        return Personalization.EnsureContrast(original, background, _options.Mode == PostColorOptions.Vivid && _dark ? 5.5 : 4.5, _dark);
    }

    private void Refresh()
    {
        _brushes.Clear();
        Revision++;
        PropertyChanged?.Invoke(this, new(nameof(Revision)));
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var source = (Color)parameter;
        if (_brushes.TryGetValue(source, out var cached)) return cached;
        var brush = new SolidColorBrush(Resolve(source));
        brush.Freeze();
        if (_brushes.Count < 256) _brushes.Add(source, brush);
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

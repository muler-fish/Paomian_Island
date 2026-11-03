using System.Windows;

namespace Tide.Desktop;

internal static class ReaderTheme
{
    public static void Apply(bool dark)
    {
        var app = Application.Current;
        var dictionaries = app.Resources.MergedDictionaries;
        var old = dictionaries.FirstOrDefault(d => d.Source?.OriginalString.EndsWith("Themes/Light.xaml", StringComparison.Ordinal) == true || d.Source?.OriginalString.EndsWith("Themes/Dark.xaml", StringComparison.Ordinal) == true);
        var theme = new ResourceDictionary { Source = new Uri($"/Tide;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        if (old is not null) dictionaries[dictionaries.IndexOf(old)] = theme;
        else dictionaries.Add(theme);
        // Keep the WPF UI control state templates and the reader palette in sync.
        foreach (var (key, source) in new[] {
            ("AccentButtonBackground", "Accent"), ("AccentButtonBackgroundPointerOver", "AccentHover"), ("AccentButtonBackgroundPressed", "AccentPressed"),
            ("AccentButtonForeground", "OnAccent"), ("AccentButtonForegroundPointerOver", "OnAccent"), ("AccentButtonForegroundPressed", "OnAccent"),
            ("ControlFillColorDefaultBrush", "Surface"), ("ControlFillColorSecondaryBrush", "Hover"), ("ControlFillColorTertiaryBrush", "Pressed"),
            ("ControlStrokeColorDefaultBrush", "Line"), ("TextFillColorPrimaryBrush", "Text"), ("TextFillColorSecondaryBrush", "Muted") })
            app.Resources[key] = app.FindResource(source);
    }
}

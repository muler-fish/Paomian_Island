using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tide.Desktop;

/// <summary>Toolbar, document and pagination share one centered reading measure.</summary>
public sealed class ReadingMeasureConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double width && double.IsFinite(width) ? Math.Max(1, Math.Min(UiMetrics.ReadingWidth, width - (width < 400 ? 32 : 64))) : 1d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class ReadingTypeScaleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double size ? Math.Max(13, size + double.Parse(parameter?.ToString() ?? "0", CultureInfo.InvariantCulture)) : 16d;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

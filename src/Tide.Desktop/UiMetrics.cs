using System.Windows;

namespace Tide.Desktop;

// Refined Reader round 2: shared WPF DIP geometry, including all custom dialogs.
public static class UiMetrics
{
    public const double MainTitleHeight = 52;
    public const double ChildTitleHeight = 76;
    public const double ControlHeight = 32;
    public const double BodyLineHeightRatio = 1.85;
    public const double ReadingWidth = 860;
    public const double DialogWidth = 640;
    public const double DialogInset = 24;
    public const int PreviewLines = 7;
    public static GridLength MainTitleRow => new(MainTitleHeight);
    public static Thickness ButtonPadding => new(12, 4, 12, 4);
    public static Thickness IconButtonPadding => new(6);
    public static Thickness InputPadding => new(10, 6, 10, 6);
    public static CornerRadius ControlCornerRadius => new(5);
    public static CornerRadius DialogCornerRadius => new(10);
    public static Thickness DialogBodyMargin => new(DialogInset, 0, DialogInset, 12);
    public static Thickness DialogActionsMargin => new(DialogInset, 16, DialogInset, DialogInset);
}

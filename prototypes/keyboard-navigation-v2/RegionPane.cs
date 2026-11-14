using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using Tide.Desktop;

namespace Paomian.KeyboardPrototype;

internal sealed class RegionPane
{
    private readonly Border _activeLine = new() { Height = 2, Opacity = 0, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
    internal Grid Root { get; } = new() { ClipToBounds = true };
    internal TextBlock Heading { get; } = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    internal TextBlock Status { get; } = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
    internal StackPanel Tools { get; } = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    internal bool ShowsFocus => _activeLine.Opacity == 1;

    internal RegionPane(string name, string background)
    {
        Root.SetResourceReference(Panel.BackgroundProperty, background);
        Root.RowDefinitions.Add(new() { Height = UiMetrics.MainTitleRow });
        Root.RowDefinitions.Add(new());

        Heading.Text = name;
        Status.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        AutomationProperties.SetLiveSetting(Status, System.Windows.Automation.AutomationLiveSetting.Polite);
        var headings = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
        headings.Children.Add(Heading);
        headings.Children.Add(Status);

        var bar = new DockPanel { Margin = new(0, 0, 12, 0) };
        DockPanel.SetDock(Tools, Dock.Right);
        bar.Children.Add(Tools);
        bar.Children.Add(headings);
        var header = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        header.SetResourceReference(Border.BorderBrushProperty, "Line");
        Root.Children.Add(header);
        _activeLine.SetResourceReference(Border.BackgroundProperty, "Accent");
        Panel.SetZIndex(_activeLine, 1);
        Root.Children.Add(_activeLine);
        Root.IsKeyboardFocusWithinChanged += (_, _) =>
        {
            var active = Root.IsKeyboardFocusWithin;
            _activeLine.Opacity = active ? 1 : 0;
            Heading.SetResourceReference(TextBlock.ForegroundProperty, active ? "Accent" : "Text");
        };
        Heading.SetResourceReference(TextBlock.ForegroundProperty, "Text");
    }
}

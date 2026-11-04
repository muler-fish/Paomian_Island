using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Tide.Desktop;

/// <summary>One compact row rhythm for all categories, using the existing Refined Reader tokens.</summary>
internal static class SettingsLayout
{
    internal const double Width = 880, Height = 690, NavigationWidth = 144;
    internal static TextBlock Text(string text, bool secondary = false)
    {
        var block = new TextBlock { Text = text, FontSize = secondary ? 12 : 13, TextWrapping = TextWrapping.Wrap, LineHeight = secondary ? 19 : 21 };
        block.SetResourceReference(TextBlock.ForegroundProperty, secondary ? "Muted" : "Text"); return block;
    }
    internal static StackPanel Page(string title)
    {
        var page = new StackPanel { Visibility = Visibility.Collapsed, Margin = new(24, 0, 24, 8) };
        page.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 12) }); return page;
    }
    internal static void Section(StackPanel page, string title)
    {
        var heading = Text(title); heading.FontWeight = FontWeights.SemiBold;
        var border = new Border { BorderThickness = new(0, 1, 0, 0), Padding = new(0, 20, 0, 4), Margin = new(0, 12, 0, 0), Child = heading };
        border.SetResourceReference(Border.BorderBrushProperty, "Line"); page.Children.Add(border);
    }
    internal static void Block(StackPanel page, UIElement content, bool separator = true)
    {
        var border = new Border { Child = content, Padding = new(0, 12, 0, 12), BorderThickness = new(0, 0, 0, separator ? 1 : 0) };
        border.SetResourceReference(Border.BorderBrushProperty, "Line"); page.Children.Add(border);
    }
    internal static void Row(StackPanel page, string title, string? hint, FrameworkElement control)
    {
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 20, 0) }; labels.Children.Add(Text(title));
        if (!string.IsNullOrEmpty(hint)) { var help = Text(hint, true); help.Margin = new(0, 4, 0, 0); labels.Children.Add(help); }
        control.VerticalAlignment = VerticalAlignment.Center;
        if (control is not Button) AutomationProperties.SetName(control, title);
        if (control is CheckBox box)
        {
            // Preserve the original checkbox label's click target, while aligning the mark on the right.
            labels.Background = System.Windows.Media.Brushes.Transparent;
            labels.MouseLeftButtonUp += (_, e) => { if (box.IsEnabled) { box.Focus(); box.IsChecked = box.IsChecked != true; e.Handled = true; } };
            AutomationProperties.SetLabeledBy(box, (TextBlock)labels.Children[0]);
        }
        Grid.SetColumn(control, 1); grid.Children.Add(labels); grid.Children.Add(control); Block(page, grid);
    }
    internal static void Slider(StackPanel page, string title, string? hint, double value, double min, double max, double step, string suffix, Action<double> save)
    {
        var group = new StackPanel(); var header = new Grid(); header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(Text(title)); var current = Text($"{value:0.##}{suffix}", true); Grid.SetColumn(current, 1); header.Children.Add(current); group.Children.Add(header);
        if (hint is not null) { var help = Text(hint, true); help.Margin = new(0, 2, 0, 0); group.Children.Add(help); }
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Value = value, Margin = new(0, 8, 0, 0) };
        AutomationProperties.SetName(slider, title); slider.ValueChanged += (_, _) => { current.Text = $"{slider.Value:0.##}{suffix}"; save(slider.Value); };
        group.Children.Add(slider); Block(page, group);
    }
    internal static TextBox Path(string value, string name)
    {
        var box = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 84, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 10) };
        AutomationProperties.SetName(box, name); box.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(TextBox.Text)) { Source = box }); return box;
    }
}

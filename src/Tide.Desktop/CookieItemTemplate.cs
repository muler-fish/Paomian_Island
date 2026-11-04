using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>管理列表与发言选择器使用同一份真实饼干对象，显示备注、原名及本串记录。</summary>
internal static class CookieItemTemplate
{
    // The same CookieIdentity remains selected; only the display differs between the popup and selection box.
    internal static DataTemplate CreateChoice(string defaultId, IReadOnlyDictionary<string, CookieThreadUsage>? usage,
        bool usageUnavailable = false)
    {
        var converter = new CookieTextConverter(defaultId, usage, usageUnavailable);
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetBinding(FrameworkElement.ToolTipProperty, new Binding { Converter = converter, ConverterParameter = "choiceTip" });

        var count = Text("ChoiceCount", "choiceCount", 12, "Muted");
        count.SetValue(DockPanel.DockProperty, Dock.Right);
        count.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
        row.AppendChild(count);

        var last = Text("ChoiceLast", "choiceLast", 11, "Muted");
        last.SetValue(FrameworkElement.ToolTipProperty, "本串上次使用");
        last.SetValue(DockPanel.DockProperty, Dock.Right);
        last.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
        row.AppendChild(last);
        var defaultState = Text("ChoiceDefault", "choiceDefault", 11, "Muted");
        defaultState.SetValue(DockPanel.DockProperty, Dock.Right);
        defaultState.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 0, 0));
        row.AppendChild(defaultState);

        var check = new FrameworkElementFactory(typeof(Path), "ChoiceCheck");
        check.SetValue(Path.DataProperty, Geometry.Parse("M1,6 L4,9 L11,2"));
        check.SetResourceReference(Shape.StrokeProperty, "Text");
        check.SetValue(Shape.StrokeThicknessProperty, 1.5d);
        check.SetValue(Shape.StretchProperty, Stretch.Uniform);
        check.SetValue(FrameworkElement.WidthProperty, 12d);
        check.SetValue(FrameworkElement.HeightProperty, 12d);
        check.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        check.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        check.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
        row.AppendChild(check);

        row.AppendChild(NameWithRemark(converter));

        var template = new DataTemplate(typeof(CookieIdentity)) { VisualTree = row };
        var selected = new DataTrigger { Binding = new Binding(nameof(ComboBoxItem.IsSelected)) { RelativeSource = new(RelativeSourceMode.FindAncestor, typeof(ComboBoxItem), 1) }, Value = true };
        selected.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "ChoiceCheck"));
        template.Triggers.Add(selected);
        // A selection-box presenter has no ComboBoxItem ancestor: omit popup-only state and check columns.
        var collapsed = new DataTrigger { Binding = new Binding { RelativeSource = new(RelativeSourceMode.FindAncestor, typeof(ComboBoxItem), 1) }, Value = null };
        collapsed.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "ChoiceCheck"));
        collapsed.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "ChoiceDefault"));
        template.Triggers.Add(collapsed);
        return template;

        FrameworkElementFactory Text(string name, string field, double size, string brush)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock), name);
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = converter, ConverterParameter = field });
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            text.SetValue(TextBlock.FontSizeProperty, size);
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var style = new Style(typeof(TextBlock));
            var empty = new Trigger { Property = TextBlock.TextProperty, Value = "" };
            empty.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
            style.Triggers.Add(empty); text.SetValue(FrameworkElement.StyleProperty, style);
            return text;
        }
    }

    internal static DataTemplate Create(string defaultId, string? currentId = null)
    {
        var converter = new CookieTextConverter(defaultId, null, false, currentId);
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
        var badges = Text("managementState", 11, "Muted");
        badges.SetValue(DockPanel.DockProperty, Dock.Right);
        badges.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));
        panel.AppendChild(badges);
        var name = NameWithRemark(converter);
        name.SetBinding(FrameworkElement.ToolTipProperty, new Binding { Converter = converter, ConverterParameter = "choiceTip" });
        panel.AppendChild(name);
        return new DataTemplate(typeof(CookieIdentity)) { VisualTree = panel };

        FrameworkElementFactory Text(string field, double size, string brush)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding { Converter = converter, ConverterParameter = field });
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            text.SetValue(TextBlock.FontSizeProperty, size);
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
            text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            return text;
        }
    }

    private static FrameworkElementFactory NameWithRemark(CookieTextConverter converter)
    {
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetValue(TextBlock.FontSizeProperty, 13d);
        name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        var id = new FrameworkElementFactory(typeof(System.Windows.Documents.Run));
        id.SetBinding(System.Windows.Documents.Run.TextProperty, new Binding(nameof(CookieIdentity.Name)) { Mode = BindingMode.OneWay });
        name.AppendChild(id);
        var remark = new FrameworkElementFactory(typeof(System.Windows.Documents.Run));
        remark.SetBinding(System.Windows.Documents.Run.TextProperty, new Binding { Mode = BindingMode.OneWay, Converter = converter, ConverterParameter = "choiceRemark" });
        remark.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Muted");
        name.AppendChild(remark);
        return name;
    }

    private sealed class CookieTextConverter(string defaultId, IReadOnlyDictionary<string, CookieThreadUsage>? usage,
        bool unavailable, string? currentId = null) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not CookieIdentity cookie) return "";
            CookieThreadUsage? record = null;
            usage?.TryGetValue(cookie.Id, out record);
            return (parameter as string) switch
            {
                "choiceRemark" => string.IsNullOrWhiteSpace(cookie.Remark) ? "" : $"（{cookie.Remark}）",
                "choiceDefault" => cookie.Id == defaultId ? "默认" : "",
                "choiceLast" => record?.IsLastUsed == true ? "上次" : "",
                "choiceCount" when unavailable => "—",
                "choiceCount" => record is { ReplyCount: > 0 } ? $"{record.ReplyCount} 次" : "",
                "choiceTip" => (string.IsNullOrWhiteSpace(cookie.Remark) ? cookie.Name : $"{cookie.Remark} · {cookie.Name}") + (unavailable ? " · 本串回应记录暂不可用" : ""),
                "managementState" => string.Join(" · ", new[] { cookie.Id == defaultId ? "默认" : "", cookie.Id == currentId ? "当前" : "" }.Where(s => s.Length > 0)),
                _ => ""
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}

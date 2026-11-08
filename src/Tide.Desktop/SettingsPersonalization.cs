using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>「外观」里的主题强调色与标识颜色；改动立即生效并保存。</summary>
internal static class SettingsPersonalization
{
    internal static void Accent(StackPanel page, MainViewModel vm, Func<string, Action, Button> button)
    {
        // 上一行已有分隔线，这里只用加粗小标题，避免出现两条线。
        var heading = SettingsLayout.Text("主题强调色"); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new(0, 16, 0, 0); page.Children.Add(heading);
        var hint = SettingsLayout.Text("用于主要按钮、选中状态、滑块、开关和标志。深色主题会自动换成同色相的较亮颜色；饼干、PO、SAGE 等标识颜色不受影响。", true);
        hint.Margin = new(0, 4, 0, 0); page.Children.Add(hint);
        var swatches = new WrapPanel { Margin = new(-4, 10, 0, 0) };
        AutomationProperties.SetName(swatches, "预设强调色");
        // 「实际显示」按当前主题生成：浅色主题下太浅的颜色会被加深，保存的仍是用户选的原始颜色。
        var custom = new ColorField("自定义强调色", vm.State.AccentColor, Personalization.DefaultAccent, button, hex => Personalization.AccentPalette(hex, vm.State.Theme == "dark")[0]);
        var dots = new List<(Personalization.Preset Preset, Border Dot)>();
        foreach (var preset in Personalization.Presets)
        {
            var dot = new Border { Width = 24, Height = 24, CornerRadius = new(12), BorderThickness = new(2) };
            var choose = new Button { Content = dot, Width = 34, Height = 34, MinHeight = 34, Padding = new(0), Margin = new(0, 0, 2, 0), ToolTip = preset.Name };
            choose.SetResourceReference(FrameworkElement.StyleProperty, "Quiet");
            AutomationProperties.SetName(choose, "强调色：" + preset.Name);
            choose.Click += (_, _) => custom.Set(preset.Light, raise: true);
            dots.Add((preset, dot)); swatches.Children.Add(choose);
        }
        // 圆点显示当前主题下的实际颜色；选中的预设加一圈正文色描边。
        void Refresh()
        {
            var dark = vm.State.Theme == "dark";
            var current = custom.Value.Length > 0 ? custom.Value : Personalization.DefaultAccent;
            custom.Refresh();
            foreach (var (preset, dot) in dots)
            {
                dot.Background = new SolidColorBrush(Personalization.AccentPalette(preset.Light, dark)[0]);
                if (preset.Light.Equals(current, StringComparison.OrdinalIgnoreCase)) dot.SetResourceReference(Border.BorderBrushProperty, "Text");
                else dot.BorderBrush = Brushes.Transparent;
            }
        }
        custom.Changed += hex =>
        {
            vm.State.AccentColor = hex; Personalization.Load(vm.State); App.ApplyPersonalization(vm.State.Theme); vm.Save(); Refresh();
        };
        ThemeChanged += Refresh; page.Unloaded += (_, _) => ThemeChanged -= Refresh;
        Refresh();
        page.Children.Add(swatches);
        var customRow = new StackPanel { Margin = new(0, 10, 0, 0) };
        customRow.Children.Add(SettingsLayout.Text("自定义颜色")); customRow.Children.Add(custom);
        page.Children.Add(customRow);
        page.Children.Add(Preview());
        var separator = new Border { BorderThickness = new(0, 0, 0, 1), Margin = new(0, 16, 0, 0) }; separator.SetResourceReference(Border.BorderBrushProperty, "Line");
        page.Children.Add(separator);
    }

    // 效果预览：浅底细边框的独立区域，分组展示选中与未选中状态。整块不接收鼠标和焦点，只用于查看，不是设置项。
    private static Border Preview()
    {
        var box = new Border { BorderThickness = new(1), CornerRadius = UiMetrics.ControlCornerRadius, Padding = new(14, 10, 14, 12), Margin = new(0, 14, 0, 0), IsHitTestVisible = false };
        box.SetResourceReference(Border.BackgroundProperty, "Canvas"); box.SetResourceReference(Border.BorderBrushProperty, "Line");
        AutomationProperties.SetName(box, "效果预览");
        var content = new StackPanel();
        var header = new DockPanel();
        var note = SettingsLayout.Text("仅展示效果，不会改动设置", true); DockPanel.SetDock(note, Dock.Right); header.Children.Add(note);
        var title = SettingsLayout.Text("效果预览", true); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
        content.Children.Add(header);
        var groups = new WrapPanel();
        StackPanel Group(string caption, params UIElement[] samples)
        {
            var group = new StackPanel { Margin = new(0, 10, 28, 0) };
            var label = SettingsLayout.Text(caption, true); label.Margin = new(0, 0, 0, 6); group.Children.Add(label);
            var row = new WrapPanel(); foreach (var sample in samples) { if (sample is FrameworkElement element) { element.Focusable = false; element.Margin = new(0, 0, 8, 4); } row.Children.Add(sample); }
            group.Children.Add(row); groups.Children.Add(group); return group;
        }
        var primary = new Wpf.Ui.Controls.Button { Content = "主要按钮" }; primary.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        Group("按钮", primary, new Wpf.Ui.Controls.Button { Content = "普通按钮" });
        ToggleButton Toggle(string text, bool on) { var toggle = new ToggleButton { Content = text, IsChecked = on }; toggle.SetResourceReference(FrameworkElement.StyleProperty, "ReaderToggle"); return toggle; }
        Group("分类切换", Toggle("已选中", true), Toggle("未选中", false));
        Group("勾选与滑块", new CheckBox { Content = "已勾选", IsChecked = true, VerticalAlignment = VerticalAlignment.Center }, new CheckBox { Content = "未勾选", VerticalAlignment = VerticalAlignment.Center },
            new Slider { Width = 110, Minimum = 0, Maximum = 100, Value = 60, VerticalAlignment = VerticalAlignment.Center });
        Border Row(string text, bool selected)
        {
            var row = new Border { BorderThickness = new(2, 0, 0, 0), Padding = new(10, 5, 12, 5), Width = 128, Child = SettingsLayout.Text(text) };
            if (selected) { row.SetResourceReference(Border.BackgroundProperty, "Selected"); row.SetResourceReference(Border.BorderBrushProperty, "SelectionLine"); }
            else { row.Background = Brushes.Transparent; row.BorderBrush = Brushes.Transparent; }
            return row;
        }
        var rows = new StackPanel(); rows.Children.Add(Row("选中的串", true)); var other = Row("其他串", false); other.Margin = new(0, 2, 0, 0); rows.Children.Add(other);
        Group("串列表", rows);
        content.Children.Add(groups);
        box.Child = content;
        return box;
    }

    internal static void Identity(StackPanel page, MainViewModel vm, Func<string, Action, Button> button)
    {
        var content = new StackPanel { Margin = new(10, 4, 0, 0) };
        var expander = new Expander { Header = "标识颜色", Content = content, IsExpanded = false, Margin = new(-10, 12, 0, 0) };
        content.Children.Add(SettingsLayout.Text("只需选择主色，标签文字色和底色会按浅色、深色主题自动生成，保证足够的对比度。只改变颜色，不影响 PO、红名、SAGE 的判断。", true));
        // 预览用与串列表、阅读区相同的身份标签控件。
        var preview = new WrapPanel { Margin = new(0, 10, 0, 4) };
        AutomationProperties.SetName(preview, "标识颜色预览");
        preview.Children.Add(new PostIdentity { Post = new Post { UserHash = "a1B2c3D", IsSage = true }, IsPo = true, Margin = new(0, 0, 16, 6) });
        preview.Children.Add(new PostIdentity { Post = new Post { UserHash = "Admin", IsAdmin = true }, Margin = new(0, 0, 16, 6) });
        content.Children.Add(preview);
        var fields = new List<ColorField>();
        foreach (var identity in Enum.GetValues<Personalization.Identity>())
        {
            // 「实际显示」是当前主题下标签文字的颜色。
            var field = new ColorField(Personalization.Label(identity) + "颜色", Personalization.Get(vm.State.IdentityColors, identity), Personalization.DefaultIdentity(identity), button,
                hex => Personalization.IdentityPalette(hex, vm.State.Theme == "dark").Foreground);
            field.Changed += hex =>
            {
                vm.State.IdentityColors = Personalization.With(vm.State.IdentityColors, identity, hex);
                Personalization.Load(vm.State); App.ApplyPersonalization(vm.State.Theme); vm.Save();
            };
            var row = new StackPanel { Margin = new(0, 12, 0, 0) };
            row.Children.Add(SettingsLayout.Text(Personalization.Label(identity))); row.Children.Add(field);
            content.Children.Add(row); fields.Add(field);
        }
        void Refresh() { foreach (var field in fields) field.Refresh(); }
        ThemeChanged += Refresh; page.Unloaded += (_, _) => ThemeChanged -= Refresh;
        var reset = button("全部恢复默认", () => { foreach (var field in fields) field.Set("", raise: true); });
        reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new(0, 16, 0, 4);
        content.Children.Add(reset);
        SettingsLayout.Block(page, expander, false);
    }

    /// <summary>设置窗口切换浅色/深色后通知强调色圆点刷新。</summary>
    internal static event Action? ThemeChanged;
    internal static void NotifyThemeChanged() => ThemeChanged?.Invoke();
}

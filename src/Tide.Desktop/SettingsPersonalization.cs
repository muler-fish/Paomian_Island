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
        var custom = new ColorField("自定义强调色", vm.State.AccentColor, Personalization.DefaultAccent, button);
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
        // 实时预览：直接使用共用样式，与程序中实际控件一致。
        var preview = new WrapPanel { Margin = new(0, 14, 0, 0) };
        AutomationProperties.SetName(preview, "强调色预览");
        var primary = new Wpf.Ui.Controls.Button { Content = "主要按钮", Margin = new(0, 0, 10, 8) }; primary.SetResourceReference(FrameworkElement.StyleProperty, "Primary");
        var toggle = new RadioButton { Content = "选中", IsChecked = true, GroupName = "AccentPreview" + vm.GetHashCode(), Margin = new(0, 0, 10, 8) }; toggle.SetResourceReference(FrameworkElement.StyleProperty, "ReaderToggle");
        var check = new CheckBox { Content = "已勾选", IsChecked = true, Margin = new(0, 0, 14, 8), VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Width = 120, Minimum = 0, Maximum = 100, Value = 60, Margin = new(0, 0, 14, 8), VerticalAlignment = VerticalAlignment.Center, Focusable = false };
        AutomationProperties.SetName(slider, "预览滑块");
        var row = new Border { BorderThickness = new(2, 0, 0, 0), Padding = new(10, 6, 12, 6), Margin = new(0, 0, 0, 8), Child = SettingsLayout.Text("选中的串") };
        row.SetResourceReference(Border.BackgroundProperty, "Selected"); row.SetResourceReference(Border.BorderBrushProperty, "SelectionLine");
        foreach (var element in new UIElement[] { primary, toggle, check, slider, row }) preview.Children.Add(element);
        SettingsLayout.Block(page, preview);
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
            var field = new ColorField(Personalization.Label(identity) + "颜色", Personalization.Get(vm.State.IdentityColors, identity), Personalization.DefaultIdentity(identity), button);
            field.Changed += hex =>
            {
                vm.State.IdentityColors = Personalization.With(vm.State.IdentityColors, identity, hex);
                Personalization.Load(vm.State); App.ApplyPersonalization(vm.State.Theme); vm.Save();
            };
            var row = new StackPanel { Margin = new(0, 12, 0, 0) };
            row.Children.Add(SettingsLayout.Text(Personalization.Label(identity))); row.Children.Add(field);
            content.Children.Add(row); fields.Add(field);
        }
        var reset = button("全部恢复默认", () => { foreach (var field in fields) field.Set("", raise: true); });
        reset.HorizontalAlignment = HorizontalAlignment.Left; reset.Margin = new(0, 16, 0, 4);
        content.Children.Add(reset);
        SettingsLayout.Block(page, expander, false);
    }

    /// <summary>设置窗口切换浅色/深色后通知强调色圆点刷新。</summary>
    internal static event Action? ThemeChanged;
    internal static void NotifyThemeChanged() => ThemeChanged?.Invoke();
}

/// <summary>一个颜色设置：色块、HEX 输入、可展开的色相/饱和度/亮度调节和恢复默认。值为空字符串表示默认色。</summary>
internal sealed class ColorField : StackPanel
{
    private readonly string _default, _name;
    private readonly Border _swatch = new() { Width = 28, Height = 28, CornerRadius = UiMetrics.ControlCornerRadius, BorderThickness = new(1), Margin = new(0, 0, 10, 0) };
    private readonly TextBox _hex = new() { Width = 100, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _adjust = new() { Content = "调色", Margin = new(8, 0, 0, 0) };
    private readonly Button _reset;
    private readonly TextBlock _error = SettingsLayout.Text("", true);
    private StackPanel? _mixer;
    private Slider? _hue, _saturation, _lightness;
    private bool _updating;
    public string Value { get; private set; }
    public event Action<string>? Changed;

    public ColorField(string name, string value, string defaultHex, Func<string, Action, Button> button)
    {
        _name = name; _default = defaultHex; Value = value;
        Margin = new(0, 8, 0, 0);
        var row = new WrapPanel();
        _swatch.SetResourceReference(Border.BorderBrushProperty, "Line"); AutomationProperties.SetName(_swatch, name + "色块");
        row.Children.Add(_swatch);
        AutomationProperties.SetName(_hex, name + " HEX");
        _hex.TextChanged += (_, _) => { if (!_updating && LocalStore.HexColor(_hex.Text) is { Length: > 0 } hex && _hex.Text.Trim().Length >= 6) Set(hex, raise: true, fromText: true); };
        _hex.LostKeyboardFocus += (_, _) => Commit();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        row.Children.Add(_hex);
        _adjust.SetResourceReference(FrameworkElement.StyleProperty, "ReaderToggle");
        AutomationProperties.SetName(_adjust, name + "调色");
        _adjust.Checked += (_, _) => { EnsureMixer().Visibility = Visibility.Visible; SyncSliders(); };
        _adjust.Unchecked += (_, _) => { if (_mixer is not null) _mixer.Visibility = Visibility.Collapsed; };
        row.Children.Add(_adjust);
        _reset = button("恢复默认", () => Set("", raise: true)); _reset.Margin = new(8, 0, 0, 0);
        AutomationProperties.SetName(_reset, name + "恢复默认");
        row.Children.Add(_reset);
        Children.Add(row);
        _error.Visibility = Visibility.Collapsed; _error.Margin = new(0, 4, 0, 0); _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        Children.Add(_error);
        Show();
    }

    private string Effective => Value.Length > 0 ? Value : _default;

    /// <summary>设置颜色；与默认色相同时记为默认（空字符串），便于以后跟随默认值。</summary>
    public void Set(string hex, bool raise, bool fromText = false, bool fromSliders = false)
    {
        hex = LocalStore.HexColor(hex);
        if (hex.Equals(_default, StringComparison.OrdinalIgnoreCase)) hex = "";
        var changed = hex != Value;
        Value = hex; _error.Visibility = Visibility.Collapsed;
        Show(updateText: !fromText); if (!fromSliders) SyncSliders();
        if (changed && raise) Changed?.Invoke(hex);
    }

    private void Commit()
    {
        if (LocalStore.HexColor(_hex.Text) is { Length: > 0 } hex) { Set(hex, raise: true); return; }
        _error.Text = "请输入 #RRGGBB 格式的颜色，例如 #286D59。"; _error.Visibility = Visibility.Visible;
        _updating = true; _hex.Text = Effective; _updating = false;
    }

    private void Show(bool updateText = true)
    {
        _swatch.Background = new SolidColorBrush(Personalization.Rgb(Effective));
        if (updateText) { _updating = true; _hex.Text = Effective; _updating = false; }
        _reset.IsEnabled = Value.Length > 0;
    }

    private StackPanel EnsureMixer()
    {
        if (_mixer is not null) return _mixer;
        _mixer = new StackPanel { Margin = new(0, 8, 0, 0), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
        Slider Channel(string label, double max)
        {
            var grid = new Grid { Margin = new(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new() { Width = new(64) }); grid.ColumnDefinitions.Add(new());
            var text = SettingsLayout.Text(label, true); text.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(text);
            var slider = new Slider { Minimum = 0, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(slider, _name + label);
            slider.ValueChanged += (_, _) => FromSliders();
            Grid.SetColumn(slider, 1); grid.Children.Add(slider); _mixer!.Children.Add(grid);
            return slider;
        }
        _hue = Channel("色相", 359); _saturation = Channel("饱和度", 100); _lightness = Channel("亮度", 100);
        Children.Insert(1, _mixer);
        return _mixer;
    }

    private void SyncSliders()
    {
        if (_hue is null || _saturation is null || _lightness is null) return;
        var (h, s, l) = Personalization.ToHsl(Personalization.Rgb(Effective));
        _updating = true; _hue.Value = Math.Round(h); _saturation.Value = Math.Round(s * 100); _lightness.Value = Math.Round(l * 100); _updating = false;
    }

    private void FromSliders()
    {
        if (_updating || _hue is null || _saturation is null || _lightness is null) return;
        var color = Personalization.FromHsl(_hue.Value, _saturation.Value / 100, _lightness.Value / 100);
        Set(Personalization.Hex(color), raise: true, fromSliders: true);
    }
}

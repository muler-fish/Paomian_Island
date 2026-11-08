using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>
/// 二维 HSV 调色板：色域横向为饱和度、纵向为明度，下方色相条，底部显示所选颜色和实际显示颜色。
/// 内部保存 H/S/V，外部写入的颜色与当前位置一致时不重算，避免灰色、黑色时色相跳回红色。
/// </summary>
internal sealed class ColorPicker : Border
{
    internal const double AreaWidth = 240, AreaHeight = 150, HueHeight = 14;
    private double _h, _s, _v;
    private readonly Border _hueLayer = new() { IsHitTestVisible = false };
    private readonly Surface _area = new() { Width = AreaWidth, Height = AreaHeight, Background = Brushes.Transparent, Cursor = Cursors.Cross, Focusable = true };
    private readonly Canvas _areaMarks = new() { IsHitTestVisible = false };
    private readonly Ellipse _thumb = new() { Width = 14, Height = 14, StrokeThickness = 2, Stroke = Brushes.White };
    private readonly Ellipse _thumbOuter = new() { Width = 16, Height = 16, StrokeThickness = 1, Stroke = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
    private readonly Surface _hue = new() { Width = AreaWidth, Height = HueHeight + 6, Background = Brushes.Transparent, Cursor = Cursors.Hand, Focusable = true };
    private readonly Border _hueThumb = new() { Width = 6, Height = HueHeight + 6, CornerRadius = new(3), BorderThickness = new(1.5), BorderBrush = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _selected = Swatch(), _effective = Swatch();
    private readonly TextBlock _selectedText = SettingsLayout.Text("", true), _effectiveText = SettingsLayout.Text("", true);
    private readonly DispatcherTimer _throttle = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private bool _dirty;
    private Func<string, Color>? _effectiveColor;
    /// <summary>用户在调色板上改动后触发（拖动时约每 40 ms 一次，松开时一定触发一次）。</summary>
    public event Action<string>? ColorChanged;
    public string Hex => Personalization.Hex(Personalization.FromHsv(_h, _s, _v));
    internal (double H, double S, double V) Hsv => (_h, _s, _v);
    internal FrameworkElement Area => _area;
    internal FrameworkElement HueBar => _hue;

    public ColorPicker(string name)
    {
        Padding = new(12); BorderThickness = new(1); CornerRadius = UiMetrics.ControlCornerRadius;
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(BorderBrushProperty, "Line");
        AutomationProperties.SetName(this, name + "调色板");
        var stack = new StackPanel();
        // 色域：底色为纯色相，叠加「白→透明」横向渐变和「透明→黑」纵向渐变。圆角裁切只作用于渐变，指示器不被裁掉。
        var gradients = new Grid { Clip = new RectangleGeometry(new Rect(0, 0, AreaWidth, AreaHeight), 4, 4), IsHitTestVisible = false };
        gradients.Children.Add(_hueLayer);
        gradients.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Colors.White, Color.FromArgb(0, 255, 255, 255), 0) });
        gradients.Children.Add(new Rectangle { Fill = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Colors.Black, 90) });
        _area.Children.Add(gradients);
        _areaMarks.Children.Add(_thumbOuter); _areaMarks.Children.Add(_thumb); _area.Children.Add(_areaMarks);
        AutomationProperties.SetName(_area, name + "色域");
        AutomationProperties.SetHelpText(_area, "横向调整饱和度，纵向调整明度；可点击或拖动，方向键微调。");
        Track(_area, p => PickArea(p));
        _area.KeyDown += (_, e) => Nudge(e, dx => PickArea(new(_s * AreaWidth + dx, (1 - _v) * AreaHeight)), dy => PickArea(new(_s * AreaWidth, (1 - _v) * AreaHeight + dy)));
        stack.Children.Add(_area);
        // 色相条：七段渐变，指示器是带白边的竖条。
        var hueTrack = new Border { Height = HueHeight, CornerRadius = new(HueHeight / 2), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var spectrum = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 0) };
        for (var i = 0; i <= 6; i++) spectrum.GradientStops.Add(new(Personalization.FromHsv(i * 60, 1, 1), i / 6d));
        hueTrack.Background = spectrum;
        _hueThumb.Effect = null; _hueThumb.IsHitTestVisible = false;
        _hue.Children.Add(hueTrack); _hue.Children.Add(_hueThumb); _hue.Margin = new(0, 10, 0, 0);
        AutomationProperties.SetName(_hue, name + "色相条");
        Track(_hue, p => PickHue(p.X));
        _hue.KeyDown += (_, e) => Nudge(e, dx => PickHue(_h / 360 * AreaWidth + dx), _ => { });
        stack.Children.Add(_hue);
        // 所选颜色（保存的原始值）与实际显示颜色（为保证对比度可能被调整）分开展示。
        var footer = new WrapPanel { Margin = new(0, 10, 0, 0), MaxWidth = AreaWidth };
        footer.Children.Add(Labeled("所选", _selected, _selectedText, name + "所选颜色"));
        footer.Children.Add(Labeled("实际显示", _effective, _effectiveText, name + "实际颜色"));
        stack.Children.Add(footer);
        Child = stack;
        _throttle.Tick += (_, _) => Flush();
    }

    /// <summary>实际显示颜色的计算方式（例如按当前主题保证对比度）；为空时与所选颜色相同。</summary>
    internal void SetEffective(Func<string, Color> effective) { _effectiveColor = effective; Render(); }

    /// <summary>外部（HEX 输入、预设、恢复默认）写入颜色；与当前位置相同时保持原有色相和位置。</summary>
    public void SetColor(string hex)
    {
        hex = LocalStore.HexColor(hex);
        if (hex.Length == 0) return;
        if (!hex.Equals(Hex, StringComparison.OrdinalIgnoreCase))
        {
            var (h, s, v) = Personalization.ToHsv(Personalization.Rgb(hex));
            // 灰色没有色相、黑色没有饱和度：保留原值，色域不会突然跳回红色。
            if (s > 0 && v > 0) _h = h;
            if (v > 0) _s = s;
            _v = v;
        }
        Render();
    }

    /// <summary>按色域内的位置（DIP）选色；超出范围时取边缘。</summary>
    internal void PickArea(Point point)
    {
        _s = Math.Clamp(point.X / AreaWidth, 0, 1); _v = 1 - Math.Clamp(point.Y / AreaHeight, 0, 1);
        Render(); _dirty = true;
    }

    internal void PickHue(double x)
    {
        _h = Math.Clamp(x / AreaWidth, 0, 1) * 359.9;
        Render(); _dirty = true;
    }

    internal void Refresh() => Render();

    private void Render()
    {
        _hueLayer.Background = new SolidColorBrush(Personalization.FromHsv(_h, 1, 1));
        var x = _s * AreaWidth; var y = (1 - _v) * AreaHeight;
        Canvas.SetLeft(_thumb, x - 7); Canvas.SetTop(_thumb, y - 7); Canvas.SetLeft(_thumbOuter, x - 8); Canvas.SetTop(_thumbOuter, y - 8);
        _hueThumb.Margin = new(Math.Clamp(_h / 360 * AreaWidth - 3, 0, AreaWidth - 6), 0, 0, 0);
        _hueThumb.Background = new SolidColorBrush(Personalization.FromHsv(_h, 1, 1));
        var hex = Hex;
        _selected.Background = new SolidColorBrush(Personalization.Rgb(hex)); _selectedText.Text = hex;
        var effective = _effectiveColor?.Invoke(hex) ?? Personalization.Rgb(hex);
        _effective.Background = new SolidColorBrush(effective); _effectiveText.Text = Personalization.Hex(effective);
    }

    /// <summary>立即提交尚未通知的改动（鼠标松开、键盘调整时调用）。</summary>
    internal void Flush()
    {
        if (!_dirty) return;
        _dirty = false; ColorChanged?.Invoke(Hex);
    }

    // 按下即选色并捕获鼠标，拖动时连续更新，松开时立即提交最后一次结果。
    private void Track(FrameworkElement element, Action<Point> pick)
    {
        element.MouseLeftButtonDown += (_, e) => { element.Focus(); element.CaptureMouse(); pick(e.GetPosition(element)); _throttle.Start(); e.Handled = true; };
        element.MouseMove += (_, e) => { if (element.IsMouseCaptured) pick(e.GetPosition(element)); };
        element.MouseLeftButtonUp += (_, e) => { if (!element.IsMouseCaptured) return; element.ReleaseMouseCapture(); e.Handled = true; };
        element.LostMouseCapture += (_, _) => { _throttle.Stop(); Flush(); };
    }

    private void Nudge(KeyEventArgs e, Action<double> horizontal, Action<double> vertical)
    {
        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 2;
        switch (e.Key)
        {
            case Key.Left: horizontal(-step); break;
            case Key.Right: horizontal(step); break;
            case Key.Up: vertical(-step); break;
            case Key.Down: vertical(step); break;
            default: return;
        }
        e.Handled = true; Flush();
    }

    private static Border Swatch()
    {
        var swatch = new Border { Width = 18, Height = 18, CornerRadius = new(3), BorderThickness = new(1), Margin = new(0, 0, 6, 0) };
        swatch.SetResourceReference(BorderBrushProperty, "Line"); return swatch;
    }
    private static StackPanel Labeled(string label, Border swatch, TextBlock value, string name)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new(0, 0, 16, 0) };
        var caption = SettingsLayout.Text(label, true); caption.Margin = new(0, 0, 6, 0); caption.VerticalAlignment = VerticalAlignment.Center;
        value.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetName(value, name);
        panel.Children.Add(caption); panel.Children.Add(swatch); panel.Children.Add(value);
        return panel;
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    // 色域和色相条要出现在自动化树里（读屏和 UI 自动化可以定位），普通 Grid 没有自动化节点。
    private sealed class Surface : Grid
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
    }
}

/// <summary>一个颜色设置：色块、HEX 输入、「调色」弹出调色板、恢复默认。值为空字符串表示默认色；保存的始终是用户选的原始颜色。</summary>
internal sealed class ColorField : StackPanel
{
    private readonly string _default;
    private readonly Border _swatch = new() { Width = 28, Height = 28, CornerRadius = UiMetrics.ControlCornerRadius, BorderThickness = new(1), Margin = new(0, 0, 10, 0) };
    private readonly TextBox _hex = new() { Width = 100, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _adjust, _reset;
    private readonly TextBlock _error = SettingsLayout.Text("", true);
    private readonly WrapPanel _row = new();
    private bool _updating;
    internal ColorPicker Picker { get; }
    internal Popup Popup { get; } = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Custom, PopupAnimation = PopupAnimation.None };
    public string Value { get; private set; }
    public event Action<string>? Changed;

    public ColorField(string name, string value, string defaultHex, Func<string, Action, Button> button, Func<string, Color>? effective = null)
    {
        _default = defaultHex; Value = value;
        Margin = new(0, 8, 0, 0);
        _swatch.SetResourceReference(Border.BorderBrushProperty, "Line"); AutomationProperties.SetName(_swatch, name + "色块");
        _row.Children.Add(_swatch);
        AutomationProperties.SetName(_hex, name + " HEX");
        // 输入完整的 6 位颜色立即生效；不完整或非法的内容在失焦或回车时提示，并恢复为当前颜色。
        _hex.TextChanged += (_, _) => { if (!_updating && _hex.Text.Trim().TrimStart('#').Length == 6 && LocalStore.HexColor(_hex.Text) is { Length: > 0 } hex) Set(hex, raise: true, fromText: true); };
        _hex.LostKeyboardFocus += (_, _) => Commit();
        _hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        _row.Children.Add(_hex);
        Picker = new ColorPicker(name);
        if (effective is not null) Picker.SetEffective(effective);
        Picker.ColorChanged += hex => Set(hex, raise: true, fromPicker: true);
        Popup.Child = Picker; Popup.PlacementTarget = _row; Popup.CustomPopupPlacementCallback = Place;
        _adjust = button("调色", () => { Popup.IsOpen = !Popup.IsOpen; }); _adjust.Margin = new(8, 0, 0, 0);
        AutomationProperties.SetName(_adjust, name + "调色");
        // 弹出层打开时按钮不接收点击：点按钮会先关闭弹出层，避免随即又被重新打开。
        Popup.Opened += (_, _) => { _adjust.IsHitTestVisible = false; Picker.SetColor(Effective); Picker.Refresh(); };
        Popup.Closed += (_, _) => _adjust.IsHitTestVisible = true;
        _row.Children.Add(_adjust);
        _reset = button("恢复默认", () => Set("", raise: true)); _reset.Margin = new(8, 0, 0, 0);
        AutomationProperties.SetName(_reset, name + "恢复默认");
        _row.Children.Add(_reset);
        _row.Children.Add(Popup);
        Children.Add(_row);
        _error.Visibility = Visibility.Collapsed; _error.Margin = new(0, 4, 0, 0); _error.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        Children.Add(_error);
        Show(updateText: true); Picker.SetColor(Effective);
    }

    private string Effective => Value.Length > 0 ? Value : _default;

    /// <summary>设置颜色；与默认色相同时记为默认（空字符串）。来自调色板的改动不回写调色板位置，来自输入框的改动不改写正在输入的文字。</summary>
    public void Set(string hex, bool raise, bool fromText = false, bool fromPicker = false)
    {
        hex = LocalStore.HexColor(hex);
        if (hex.Equals(_default, StringComparison.OrdinalIgnoreCase)) hex = "";
        var changed = hex != Value;
        Value = hex; _error.Visibility = Visibility.Collapsed;
        Show(updateText: !fromText);
        if (!fromPicker) Picker.SetColor(Effective);
        if (changed && raise) Changed?.Invoke(hex);
    }

    /// <summary>主题切换后刷新「实际显示」颜色。</summary>
    internal void Refresh() => Picker.Refresh();

    private void Commit()
    {
        if (LocalStore.HexColor(_hex.Text) is { Length: > 0 } hex) { Set(hex, raise: true); return; }
        _error.Text = "请输入 #RRGGBB 格式的颜色，例如 #286D59。"; _error.Visibility = Visibility.Visible;
        _updating = true; _hex.Text = Effective; _updating = false;
    }

    private void Show(bool updateText)
    {
        _swatch.Background = new SolidColorBrush(Personalization.Rgb(Effective));
        if (updateText) { _updating = true; _hex.Text = Effective; _updating = false; }
        _reset.IsEnabled = Value.Length > 0;
    }

    // 优先放在这一行下方；下方到设置窗口底部的空间不够时放到上方，避免盖住底部的「完成」或超出窗口。
    private CustomPopupPlacement[] Place(Size popup, Size target, Point offset)
    {
        var below = new CustomPopupPlacement(new Point(0, target.Height + 6), PopupPrimaryAxis.Horizontal);
        var above = new CustomPopupPlacement(new Point(0, -popup.Height - 6), PopupPrimaryAxis.Horizontal);
        if (Window.GetWindow(this) is not { } window || PresentationSource.FromVisual(this) is null) return [below, above];
        var rowBottom = _row.PointToScreen(new Point(0, _row.ActualHeight)).Y;
        var rowTop = _row.PointToScreen(new Point(0, 0)).Y;
        var windowTop = window.PointToScreen(new Point(0, 0)).Y;
        var windowBottom = window.PointToScreen(new Point(0, window.ActualHeight)).Y;
        // 窗口底部操作区约 72 DIP，换算为屏幕像素。
        var scale = VisualTreeHelper.GetDpi(window).DpiScaleY;
        var fitsBelow = rowBottom + popup.Height + 6 <= windowBottom - 72 * scale;
        var fitsAbove = rowTop - popup.Height - 6 >= windowTop;
        return fitsBelow || !fitsAbove ? [below, above] : [above, below];
    }
}

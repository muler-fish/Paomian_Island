using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Paomian.Desktop;

/// <summary>非模态验收工具，调节时仍能滚动主窗中的同一段内容。</summary>
internal sealed class WheelTuningWindow : ChildWindow
{
    internal static readonly (string Name, WheelTuning Value)[] Presets =
    [
        ("当前 B", WheelTuning.Baseline), ("精细短滑", WheelTuning.Fine),
        ("均衡短滑", WheelTuning.Balanced), ("快速短滑", WheelTuning.Fast),
        ("浏览器轨迹对照", WheelTuning.BrowserComparison)
    ];
    private readonly ComboBox _presets = new(), _curve = new();
    private readonly Slider _distance, _duration, _immediate;
    private readonly CheckBox _system = new() { Content = "按 Windows 行数换算（三行为基准）" };
    private readonly TextBlock _effective = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Action<WheelTuning> _changed;
    private bool _updating;

    internal WheelTuningWindow(MainWindow owner, WheelTuning current, Action<WheelTuning> changed)
        : base("滚轮调校", dialogChrome: false)
    {
        Owner = owner;
        ShowInTaskbar = false;
        ShowActivated = false;
        Width = 344; Height = 414; MinWidth = 290; MinHeight = 260;
        _changed = changed;
        SourceInitialized += (_, _) =>
        {
            var work = WindowPlacement.WorkArea(owner);
            var origin = owner.PointToScreen(new Point(10, 58));
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(owner);
            Left = Math.Clamp(origin.X / dpi.DpiScaleX, work.Left, Math.Max(work.Left, work.Right - Width));
            Top = Math.Clamp(origin.Y / dpi.DpiScaleY, work.Top, Math.Max(work.Top, work.Bottom - Height));
        };
        var form = new StackPanel { Margin = new Thickness(18, 12, 18, 12) };
        SetClientContent(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        foreach (var (name, _) in Presets) _presets.Items.Add(name);
        _presets.Items.Add(new ComboBoxItem { Content = "自定义", IsEnabled = false });
        AddChoice(form, "预设", _presets);
        _distance = AddSlider(form, "每格距离", "DIP", 8, 160);
        _duration = AddSlider(form, "过渡时长", "ms", 16, 220);
        _immediate = AddSlider(form, "立即位移", "%", 0, 100);
        foreach (var name in new[] { "线性", "二次缓出", "三次缓出", "五次缓入缓出" }) _curve.Items.Add(name);
        AddChoice(form, "曲线", _curve);
        _system.Margin = new Thickness(0, 8, 0, 6);
        form.Children.Add(_system);
        _effective.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        form.Children.Add(_effective);

        _presets.SelectionChanged += (_, _) =>
        {
            if (_updating || _presets.SelectedIndex < 0 || _presets.SelectedIndex >= Presets.Length) return;
            var selected = Presets[_presets.SelectedIndex].Value;
            Display(selected);
            _changed(selected);
        };
        _distance.ValueChanged += (_, _) => Edited();
        _duration.ValueChanged += (_, _) => Edited();
        _immediate.ValueChanged += (_, _) => Edited();
        _curve.SelectionChanged += (_, _) => Edited();
        _system.Checked += (_, _) => Edited();
        _system.Unchecked += (_, _) => Edited();
        Display(current);
    }

    private static void AddChoice(Panel form, string caption, ComboBox field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = caption, Width = 84, VerticalAlignment = VerticalAlignment.Center });
        AutomationProperties.SetName(field, caption);
        row.Children.Add(field);
        form.Children.Add(row);
    }

    private static Slider AddSlider(Panel form, string caption, string unit, double min, double max)
    {
        var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(new TextBlock { Text = caption });
        var value = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(value);
        var field = new Slider { Minimum = min, Maximum = max, TickFrequency = 1, IsSnapToTickEnabled = true,
            SmallChange = 1, LargeChange = 8, IsMoveToPointEnabled = true, MinHeight = 24 };
        AutomationProperties.SetName(field, caption);
        value.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Slider.Value))
            { Source = field, StringFormat = "{0:0} " + unit });
        form.Children.Add(row); form.Children.Add(field);
        return field;
    }

    private void Edited()
    {
        if (_updating) return;
        var value = new WheelTuning(_distance.Value, _duration.Value, _immediate.Value / 100, (WheelCurve)_curve.SelectedIndex)
            { ScaleWithSystemLines = _system.IsChecked == true };
        Display(value);
        _changed(value);
    }

    internal void Display(WheelTuning value)
    {
        _updating = true;
        try
        {
            var index = Array.FindIndex(Presets, p => p.Value == value);
            _presets.SelectedIndex = index < 0 ? Presets.Length : index;
            _distance.Value = value.Distance;
            _duration.Value = value.DurationMs;
            _immediate.Value = value.ImmediateFraction * 100;
            _curve.SelectedIndex = (int)value.Curve;
            _system.IsChecked = value.ScaleWithSystemLines;
            var lines = SystemParameters.WheelScrollLines;
            _effective.Text = lines switch
            {
                < 0 => "Windows 已设为每格翻一页。",
                0 => "Windows 已关闭滚轮滚动。",
                _ => $"Windows 每格 {lines} 行 · 当前每格 {value.NotchDistance(lines, double.MaxValue):0.##} DIP（最多一屏）"
            };
        }
        finally { _updating = false; }
    }
}

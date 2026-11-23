using System.Windows.Input;

namespace Paomian.Desktop;

public partial class MainWindow
{
    internal void EnableWheelTuning()
    {
        var current = WheelTuning.Baseline;
        WheelTuningWindow? panel = null;
        void Apply(WheelTuning value)
        {
            current = value;
            InertialScroll.SetTuning(ThreadList, value);
            InertialScroll.SetTuning(ReaderList, value);
            InertialScroll.SetTuning(ForumScroll, value);
        }
        void OpenPanel()
        {
            panel = new WheelTuningWindow(this, current, Apply);
            panel.Closed += (_, _) => panel = null;
            panel.Show();
        }
        ContentRendered += (_, _) => { if (panel is null) OpenPanel(); };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.None || e.Key is not (Key.F6 or Key.F7)) return;
            e.Handled = true;
            if (e.IsRepeat) return;
            if (e.Key == Key.F7)
            {
                if (panel is null) OpenPanel(); else panel.Close();
                return;
            }
            var presets = WheelTuningWindow.Presets;
            var next = presets[(Array.FindIndex(presets, p => p.Value == current) + 1) % presets.Length];
            Apply(next.Value);
            panel?.Display(next.Value);
            ShowFeedback($"{next.Name} · F7 调校", FeedbackArea.General);
        };
    }

    // 只在开发对照入口注册。本次切换不写入资料目录。
    internal void EnableWheelComparison()
    {
        var mode = WheelMode.Short;
        ContentRendered += (_, _) => Describe();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F6 || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            if (e.IsRepeat) return;
            mode = mode switch { WheelMode.Native => WheelMode.Short, WheelMode.Short => WheelMode.Legacy, _ => WheelMode.Native };
            InertialScroll.SetMode(ThreadList, mode);
            InertialScroll.SetMode(ReaderList, mode);
            InertialScroll.SetMode(ForumScroll, mode);
            Describe();
        };
        void Describe() => ShowFeedback(mode switch
        {
            WheelMode.Native => "A · WPF 原生滚动　F6 切换",
            WheelMode.Short => "B · 短时平滑滚动　F6 切换",
            _ => "C · 原有惯性滚动　F6 切换"
        }, FeedbackArea.General);
    }
}

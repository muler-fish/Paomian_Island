using System.Windows.Input;

namespace Paomian.Desktop;

public partial class MainWindow
{
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

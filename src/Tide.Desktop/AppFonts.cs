using System.Windows;
using System.Windows.Media;

namespace Tide.Desktop;

/// <summary>全应用共用的文字字体；图标仍使用 WPF UI 自己的图标字体。</summary>
public static class AppFonts
{
    // 优先使用 Windows 自带的微软雅黑，缺失字符由随包字体补足；不安装或修改系统字体。
    public static FontFamily Family { get; } = new(
        new Uri("pack://application:,,,/"),
        "/Tide;component/Assets/Fonts/#Tide Reading");

    // 正文使用微软雅黑常规字重；标题与原文强调按各自样式请求字重。
    internal static FontWeight ReadingWeight { get; } = FontWeights.Normal;

    // 保留自然字距，由 WPF 自动选择抗锯齿算法，并采用适合运动的字形提示。
    // 字体观感需在实际显示器上验收；保持现有渲染方式，不影响独立绘制的彩色 emoji。
    internal static void ApplyReadingRendering(DependencyObject target)
    {
        TextOptions.SetTextFormattingMode(target, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(target, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(target, TextHintingMode.Animated);
    }
}

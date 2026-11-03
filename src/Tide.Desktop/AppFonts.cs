using System.Windows;
using System.Windows.Media;

namespace Tide.Desktop;

/// <summary>全应用共用的文字字体；图标仍使用 WPF UI 自己的图标字体。</summary>
public static class AppFonts
{
    // 由复合字体优先选用随包 MiSans，缺失字符再回退；不安装或修改系统字体。
    public static FontFamily Family { get; } = new(
        new Uri("pack://application:,,,/"),
        "/Tide;component/Assets/Fonts/#Tide Reading");

    // MiSans Light 4.009 的内部字重为 250，并非 WPF 的 Light(300)。
    // 显式请求该真实字重，只减轻阅读正文；界面 Regular 与标题 Semibold 继续沿用原值。
    internal static FontWeight ReadingWeight { get; } = FontWeight.FromOpenTypeWeight(250);

    // 试验版保留自然字距，由 WPF 自动选择抗锯齿算法，并采用适合运动的字形提示。
    // Animated 可能降低静态锐度，需结合 OTF 在实际显示器上验收；不影响独立绘制的彩色 emoji。
    internal static void ApplyReadingRendering(DependencyObject target)
    {
        TextOptions.SetTextFormattingMode(target, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(target, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(target, TextHintingMode.Animated);
    }
}

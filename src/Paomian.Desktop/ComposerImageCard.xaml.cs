using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Paomian.Desktop;

public partial class ComposerImageCard : UserControl
{
    internal event Action? RemoveRequested;
    public ComposerImageCard()
    {
        InitializeComponent();
        MouseEnter += (_, _) => Reveal(); MouseLeave += (_, _) => Reveal();
        IsKeyboardFocusWithinChanged += (_, _) => Reveal();
        Remove.Click += (_, e) => { e.Handled = true; RemoveRequested?.Invoke(); };
    }

    internal void ShowImage(ComposerImage? image)
    {
        Photo.Source = image?.Thumbnail;
        Placeholder.Visibility = Photo.Source is null ? Visibility.Visible : Visibility.Collapsed;
        ToolTip = image?.Name;
    }

    private void Reveal()
    {
        var show = IsMouseOver || IsKeyboardFocusWithin;
        // 保留 Tab 焦点。鼠标或键盘进入后再开放点击，避免点中隐藏的按钮。
        Remove.IsHitTestVisible = show;
        foreach (var element in new UIElement[] { Shade, Remove })
        {
            var from = element.Opacity; var to = show ? 1d : 0d;
            element.BeginAnimation(OpacityProperty, null); element.Opacity = to;
            if (SystemParameters.ClientAreaAnimation) element.BeginAnimation(OpacityProperty,
                new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(100)) { FillBehavior = FillBehavior.Stop });
        }
    }
}

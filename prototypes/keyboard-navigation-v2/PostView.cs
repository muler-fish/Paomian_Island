using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tide.Desktop;

namespace Paomian.KeyboardPrototype;

internal static class PostView
{
    internal static Border Build(ReadingRegion region, NavigationInput navigation, DemoPost post, bool mainPost = false)
    {
        var content = new StackPanel { Margin = new Thickness(0, 12, 0, 14) };
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
        var cookie = new Border { Padding = new Thickness(5, 2, 5, 2), CornerRadius = new CornerRadius(3) };
        cookie.SetResourceReference(Border.BackgroundProperty, "CookieBackground");
        var name = new TextBlock { Text = post.Cookie, FontSize = 12 };
        name.SetResourceReference(TextBlock.ForegroundProperty, "CookieForeground");
        cookie.Child = name;
        identity.Children.Add(cookie);
        identity.Children.Add(Muted($"{post.Time}   No.{post.Number}" + (mainPost ? "   主楼" : ""), new Thickness(8, 0, 0, 0)));
        content.Children.Add(identity);

        for (var i = 0; i < post.Paragraphs.Length; i++)
        {
            content.Children.Add(Paragraph(post.Paragraphs[i]));
            if (i + 1 == post.InsertActionsAfter && post.Actions.Length != 0)
            {
                var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 6) };
                foreach (var action in post.Actions)
                {
                    var button = region.ActionButton(action.Name, () => Open(navigation, action), "InlineAction");
                    button.Margin = new Thickness(0, 0, 6, 14);
                    button.VerticalAlignment = VerticalAlignment.Top;
                    if (action.Kind == ActionKind.Image)
                    {
                        var thumbnail = new StackPanel();
                        thumbnail.Children.Add(new Image { Source = Map(action.Reference != 0), Width = 116, Height = 62 });
                        thumbnail.Children.Add(new TextBlock { Text = action.Name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
                        button.Content = thumbnail;
                    }
                    actions.Children.Add(button);
                }
                content.Children.Add(actions);
            }
        }

        var postBorder = new Border { Child = content, BorderThickness = new Thickness(0, 0, 0, 1), Tag = post.Number };
        postBorder.SetResourceReference(Border.BorderBrushProperty, "Hairline");
        return postBorder;
    }

    internal static TextBlock Paragraph(string text)
    {
        var paragraph = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 16,
            LineHeight = 16 * UiMetrics.BodyLineHeightRatio, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Margin = new Thickness(0, 0, 0, 9)
        };
        paragraph.SetResourceReference(TextBlock.ForegroundProperty, "BodyText");
        TextOptions.SetTextFormattingMode(paragraph, TextFormattingMode.Ideal);
        TextOptions.SetTextHintingMode(paragraph, TextHintingMode.Animated);
        return paragraph;
    }

    internal static TextBlock Muted(string text, Thickness margin = default)
    {
        var block = new TextBlock { Text = text, FontSize = 12, Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return block;
    }

    private static void Open(NavigationInput navigation, PostAction action)
    {
        navigation.ShowModal(action.Kind switch
        {
            ActionKind.Quote => new PreviewDialog($"引用 · No.{action.Reference}", post: DemoContent.Quote(action.Reference)),
            ActionKind.Link => new PreviewDialog(action.Name, text: "这是模拟链接。可以用 Esc 回到原楼层。"),
            _ => new PreviewDialog(action.Name, image: Map(action.Reference != 0))
        });
    }

    private static DrawingImage Map(bool upstream)
    {
        Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
        var map = new DrawingGroup();
        using (var drawing = map.Open())
        {
            drawing.DrawRectangle(Brush("AccentSoft"), null, new Rect(0, 0, 480, 240));
            var river = Geometry.Parse(upstream
                ? "M0,50 C144,91 241,18 480,62 L480,155 C282,116 139,192 0,143 Z"
                : "M0,115 C181,142 188,17 480,43 L480,136 C250,113 194,231 0,199 Z");
            drawing.DrawGeometry(Brush("PoBackground"), new Pen(Brush("Line"), 2), river);
            var path = Geometry.Parse(upstream
                ? "M0,36 C137,80 260,4 480,47 M148,235 L195,145 L188,67"
                : "M0,99 C167,129 199,3 480,29 M265,240 L299,120 L295,41");
            drawing.DrawGeometry(null, new Pen(Brush("Accent"), 4), path);
            drawing.DrawEllipse(Brush("Accent"), new Pen(Brush("Surface"), 4), upstream ? new Point(188, 67) : new Point(295, 41), 8, 8);
        }
        var image = new DrawingImage(map);
        image.Freeze();
        return image;
    }
}

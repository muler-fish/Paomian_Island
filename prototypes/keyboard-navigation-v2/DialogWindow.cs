using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Tide.Desktop;

namespace Paomian.KeyboardPrototype;

internal abstract class DialogWindow : Window
{
    protected DialogWindow(string title, UIElement content, IInputElement initialFocus, double height)
    {
        Title = title;
        Width = UiMetrics.DialogWidth;
        Height = height;
        MinWidth = 420;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.FitOnStartup(this);
        var frame = new Border { Child = content, CornerRadius = UiMetrics.DialogCornerRadius };
        frame.SetResourceReference(Border.BackgroundProperty, "Surface");
        Content = frame;
        KeyboardNavigation.SetTabNavigation(frame, KeyboardNavigationMode.Cycle);
        Loaded += (_, _) => Keyboard.Focus(initialFocus);
    }
}

internal sealed class PreviewDialog : DialogWindow
{
    internal ReadingRegion Region { get; }
    internal NavigationInput Navigation { get; }

    internal PreviewDialog(string title, Scheme scheme, DemoPost? post = null, string? text = null, ImageSource? image = null)
        : this(title, scheme, post, text, image, new ReadingRegion("预览正文", "Surface", footer: true)) { }

    private PreviewDialog(string title, Scheme scheme, DemoPost? post, string? text, ImageSource? image, ReadingRegion region)
        : base(title, region.Root, region.View, post is not null && post.Paragraphs.Length > 2 ? 520 : 350)
    {
        Region = region;
        Region.Heading.Text = scheme == Scheme.Letters ? "F 显示目标" : "Enter 选择目标";
        Navigation = new NavigationInput(this, [Region], scheme, dialog: true);
        var body = new StackPanel { Margin = UiMetrics.DialogBodyMargin };
        if (post is not null) body.Children.Add(PostView.Build(Region, Navigation, post));
        if (text is not null) body.Children.Add(PostView.Paragraph(text));
        if (image is not null) body.Children.Add(new Image { Source = image, MaxWidth = 480, Stretch = Stretch.Uniform });
        Region.View.Content = body;
        Region.ToolbarButton("关闭", Close, bottom: true);
    }
}

internal sealed class ComposeDialog : DialogWindow
{
    internal TextBox Editor { get; }

    internal ComposeDialog(string action) : this(action, new TextBox
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16
    }) { }

    private ComposeDialog(string action, TextBox editor) : this(action, editor, new Button { Content = "关闭", ToolTip = "Tab 到关闭按钮，或按 Alt+F4" }) { }

    private ComposeDialog(string action, TextBox editor, Button close) : base(action, BuildContent(editor, close), editor, 380)
    {
        Editor = editor;
        close.Click += (_, _) => Close();
        // 编辑框里的 Esc 留给输入法和原生编辑控件。焦点移到按钮后可用 Esc。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || editor.IsKeyboardFocusWithin || e.IsRepeat || e.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            Close();
        };
    }

    private static Grid BuildContent(TextBox editor, Button close)
    {
        var body = new Grid { Margin = new Thickness(UiMetrics.DialogInset) };
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        body.RowDefinitions.Add(new());
        body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var label = new TextBlock { Text = "正文 · 模拟输入", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        body.Children.Add(label);
        AutomationProperties.SetLabeledBy(editor, label);
        Grid.SetRow(editor, 1);
        body.Children.Add(editor);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        footer.Children.Add(close);
        Grid.SetRow(footer, 2);
        body.Children.Add(footer);
        return body;
    }
}

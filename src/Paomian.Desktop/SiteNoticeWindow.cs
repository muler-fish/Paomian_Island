using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Paomian.Core;

namespace Paomian.Desktop;

/// <summary>启动公告：正文可滚动和复制，确认按钮及单条免打扰选项始终可见。</summary>
public sealed class SiteNoticeWindow : DialogWindow
{
    public bool SuppressThisNotice { get; private set; }

    public SiteNoticeWindow(SiteNotice notice) : base("来自 X 岛的通知")
    {
        Width = 560; Height = 570; MinWidth = 420; MinHeight = 360;
        ShowInTaskbar = false;
        // 公告是服务端文本，直接放入 Run；不作为 HTML / XAML 或可执行链接解释。
        var paragraph = new Paragraph(new Run(notice.Content)) { Margin = new(0) };
        var document = new FlowDocument(paragraph) { PagePadding = new(0), FontSize = 16, LineHeight = 26, FontFamily = (FontFamily)FindResource("ReaderInterfaceFont") };
        document.SetResourceReference(TextElement.ForegroundProperty, "Text");
        var content = new RichTextBox
        {
            Document = document, IsReadOnly = true, BorderThickness = new(0), Padding = new(0),
            Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, AcceptsTab = false
        };
        ColorEmoji.Render(paragraph.Inlines);
        DataObject.AddCopyingHandler(content, (_, e) =>
        {
            var text = EmojiText.Read(content.Selection.Start, content.Selection.End);
            e.DataObject.SetData(DataFormats.UnicodeText, text); e.DataObject.SetData(DataFormats.Text, text);
        });
        content.SetResourceReference(RichTextBox.SelectionBrushProperty, "AccentSoft");
        AutomationProperties.SetName(content, "X 岛通知正文");
        Body.Children.Add(content);

        Actions.Orientation = Orientation.Vertical;
        Actions.HorizontalAlignment = HorizontalAlignment.Stretch;
        var suppress = new CheckBox { Content = "不再显示该条通知", Margin = new(0, 0, 0, 12) };
        AutomationProperties.SetName(suppress, "不再显示该条通知");
        Actions.Children.Add(suppress);
        var confirm = Button("确定", () => { SuppressThisNotice = suppress.IsChecked == true; Close(); }, primary: true);
        confirm.IsDefault = true;
        confirm.HorizontalAlignment = HorizontalAlignment.Right;
        confirm.MinWidth = 80;
        AutomationProperties.SetName(confirm, "确定通知");
        Actions.Children.Add(confirm);
        Loaded += (_, _) => confirm.Focus();
        // 标题栏关闭 / Esc 只关闭本次；只有勾选后按“确定”才保存“不再显示”。
    }
}

using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Tide.Desktop;

/// <summary>带彩色 emoji 的纯文本编辑器，对调用方仍提供 UTF-16 选区及原文。</summary>
public sealed class EmojiEditor : RichTextBox
{
    private bool _rendering, _composing;
    private System.Windows.Documents.TextSelection NativeSelection => ((RichTextBox)this).Selection;
    public int MaxLength { get; set; } = 10000;
    public string Text
    {
        get => EmojiText.Read(Document);
        set
        {
            // 每行一个段落，与 Enter、粘贴产生的结构一致；段落之间读回为 \r\n。
            BeginChange();
            try
            {
                Document.Blocks.Clear();
                foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
                    Document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new(0) });
            }
            finally { EndChange(); }
        }
    }
    public string SelectedText { get => EmojiText.Read(NativeSelection.Start, NativeSelection.End); set => NativeSelection.Text = value; }
    public int SelectionStart => EmojiText.Read(Document.ContentStart, NativeSelection.Start).Length;
    public int SelectionLength => SelectedText.Length;
    public int CaretIndex { get => SelectionStart; set => Select(value, 0); }
    public void Select(int start, int length) => NativeSelection.Select(EmojiText.At(Document, start), EmojiText.At(Document, start + length));

    public EmojiEditor()
    {
        FontFamily = AppFonts.Family;
        Padding = new(8, 6, 8, 6); BorderThickness = new(1);
        SetResourceReference(BackgroundProperty, "Surface"); SetResourceReference(ForegroundProperty, "Text");
        SetResourceReference(BorderBrushProperty, "Line"); SetResourceReference(SelectionBrushProperty, "AccentSoft");
        AcceptsReturn = true; AcceptsTab = false; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Document.PagePadding = new(0); Document.LineHeight = 24;
        Document.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        PreviewTextInput += (_, e) =>
        {
            _composing = false;
            if (Text.Length - SelectionLength + e.Text.Length > MaxLength) e.Handled = true;
        };
        // 粘贴仅接受纯文本，防止 RTF/XAML 将外部格式或图像混入发言正文。
        DataObject.AddPastingHandler(this, (_, e) =>
        {
            if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText)) { e.CancelCommand(); return; }
            var text = (string)e.DataObject.GetData(DataFormats.UnicodeText);
            if (Text.Length - SelectionLength + text.Length > MaxLength) { e.CancelCommand(); return; }
            e.DataObject = new DataObject(DataFormats.UnicodeText, text); e.FormatToApply = DataFormats.UnicodeText;
        });
        TextCompositionManager.AddPreviewTextInputStartHandler(this, (_, _) => _composing = true);
        TextCompositionManager.AddPreviewTextInputUpdateHandler(this, (_, _) => _composing = true);
        LostKeyboardFocus += (_, _) => { _composing = false; RenderEmoji(); };
        // Enter 插入新段落：段落末尾若留下后面没有文字的 LineBreak，微软拼音等 TSF 输入法会把组合文字
        // 按字符偏移插到 LineBreak 之前，即上一行末尾。原生选区和撤销仍由 WPF 管理。
        PreviewKeyDown += (_, e) =>
        {
            // 输入法组合期间按键以 ImeProcessed 送达，候选确认键不会到这里。收到真实按键说明没有组合在进行：
            // 组合被取消（Esc、删光拼音）时不会有 PreviewTextInput，需在此复位，否则 Enter 会落到默认的分段处理。
            if (e.Key == Key.ImeProcessed) return;
            _composing = false;
            if (e.Key == Key.Enter) { if (Text.Length - SelectionLength + 2 <= MaxLength) EditingCommands.EnterParagraphBreak.Execute(null, this); e.Handled = true; }
        };
        CommandManager.AddPreviewExecutedHandler(this, CopyCommand);
        Loaded += (_, _) => RenderEmoji();
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        if (!_rendering && !_composing) RenderEmoji();
        if (!_rendering) base.OnTextChanged(e);
    }

    private void RenderEmoji()
    {
        if (_rendering || Document is null) return;
        var start = SelectionStart; var length = SelectionLength; var rebuilt = false;
        _rendering = true;
        BeginChange();
        try
        {
            foreach (var paragraph in Document.Blocks.OfType<Paragraph>().ToArray())
            {
                var text = EmojiText.Read(paragraph.ContentStart, paragraph.ContentEnd);
                var matches = EmojiSequences.Matches(text);
                var existing = new List<(int Offset, string Text)>(); var offset = 0;
                foreach (var segment in EmojiText.Segments(paragraph.ContentStart, paragraph.ContentEnd))
                {
                    if (segment.Start.GetAdjacentElement(LogicalDirection.Forward) is Emoji.Wpf.EmojiInline emoji) existing.Add((offset, emoji.Text));
                    offset += segment.Text.Length;
                }
                if (existing.SequenceEqual(matches.Select(m => (m.Index, m.Value)))) continue;
                // 仅重建 emoji 序列有变化的段落；普通输入不重排，也不逐字调用 TextPointer 扫描。
                paragraph.Inlines.Clear(); var position = 0; rebuilt = true;
                foreach (var match in matches)
                {
                    if (match.Index > position) paragraph.Inlines.Add(new Run(text[position..match.Index]));
                    WindowsEmojiDrawing.Prepare(match.Value);
                    paragraph.Inlines.Add(new Emoji.Wpf.EmojiInline { Text = match.Value, FontSize = FontSize, Foreground = Brushes.Black });
                    position = match.Index + match.Length;
                }
                if (position < text.Length) paragraph.Inlines.Add(new Run(text[position..]));
            }
            // 只有重建过段落才需要恢复选区；文档没变时保留 WPF 自己的光标，避免往返换算移动光标。
            if (rebuilt) Select(start, length);
        }
        finally { EndChange(); _rendering = false; }
    }

    private void CopyCommand(object sender, ExecutedRoutedEventArgs e)
    {
        // 原生 RichTextBox 会把图形当作对象；复制时恢复原始 Unicode 和换行。
        if (e.OriginalSource is not DependencyObject source || (source != this && !IsAncestorOf(source))) return;
        if (e.Command != ApplicationCommands.Copy && e.Command != ApplicationCommands.Cut) return;
        e.Handled = true;
        try { Clipboard.SetText(SelectedText); }
        catch (System.Runtime.InteropServices.ExternalException) { return; }
        if (e.Command == ApplicationCommands.Cut && !IsReadOnly) NativeSelection.Text = "";
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorPeer(this);
    private sealed class EditorPeer(EmojiEditor owner) : RichTextBoxAutomationPeer(owner), IValueProvider
    {
        public override object GetPattern(PatternInterface patternInterface) => patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
        bool IValueProvider.IsReadOnly => owner.IsReadOnly;
        string IValueProvider.Value => owner.Text;
        void IValueProvider.SetValue(string value) { if (owner.IsReadOnly) throw new InvalidOperationException(); owner.Text = value; }
    }
}

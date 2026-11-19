using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Paomian.Desktop;

/// <summary>列表和菜单共用与正文相同的识别规则，避免把颜文字中的普通箭头彩色化。</summary>
public sealed class EmojiLabel : TextBlock
{
    // 独立保存绑定原文；基类 Text 会随着 Inlines 图形替换而变化。
    public new static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmojiLabel),
        new PropertyMetadata("", (d, _) => ((EmojiLabel)d).Render()));
    public new string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    private void Render()
    {
        Inlines.Clear(); Inlines.Add(new Run(Text ?? "")); ColorEmoji.Render(Inlines);
    }
}

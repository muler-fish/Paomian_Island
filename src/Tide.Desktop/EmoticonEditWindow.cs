using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>新增或修改自定义颜文字。内容原样保存：不 Trim、不自动折行，多行 ASCII Art 保持字符相对位置。</summary>
internal sealed class EmoticonEditWindow : DialogWindow
{
    private readonly TextBox _text = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, MinHeight = 160, MaxHeight = 300,
        VerticalContentAlignment = VerticalAlignment.Top, MaxLength = EmoticonCatalog.MaxLength, FontSize = 15,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };
    private readonly TextBox _name = new() { MaxLength = 80 };
    public CustomEmoticon? Saved { get; private set; }

    public EmoticonEditWindow(UserState state, Action save, EmoticonEntry? editing = null) : base(editing is null ? "新增颜文字" : "编辑颜文字")
    {
        Width = UiMetrics.DialogWidth; Height = 560; MinHeight = 420;
        AutomationProperties.SetName(_text, "颜文字内容"); AutomationProperties.SetName(_name, "简称或备注");
        _text.FontFamily = AppFonts.Family;
        Label("颜文字内容"); Body.Children.Add(_text);
        var monospace = new CheckBox { Content = "用等宽字体查看", Margin = new(0, 8, 0, 0) };
        // 只改变这里的显示字体，便于核对 ASCII Art；保存的内容不变。
        monospace.Click += (_, _) => _text.FontFamily = monospace.IsChecked == true ? new FontFamily("Consolas, " + AppFonts.Family.Source) : AppFonts.Family;
        Body.Children.Add(monospace);
        Body.Children.Add(Hint("空格和换行会原样保留。"));
        Label("简称或备注（可选）"); Body.Children.Add(_name);
        Body.Children.Add(Hint("较长或多行的颜文字在列表中显示简称。"));
        Body.Children.Add(Feedback); Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        if (editing is not null)
        {
            _text.Text = editing.Text.Replace("\n", Environment.NewLine); _name.Text = editing.Name;
            monospace.IsChecked = editing.IsAsciiArt; if (editing.IsAsciiArt) _text.FontFamily = new FontFamily("Consolas, " + AppFonts.Family.Source);
        }
        AddAction("取消", Close);
        AddAction("保存", () =>
        {
            var previousCustom = state.CustomEmoticons.ToList(); var previousRecent = state.RecentEmoticons.ToList();
            try { Saved = EmoticonCatalog.Save(state, editing?.CustomId, _text.Text, _name.Text); save(); Close(); }
            catch (ArgumentException ex) { Feedback.Text = ex.Message; }
            // 写入失败时撤回内存修改，界面与磁盘保持一致。
            catch (Exception) { state.CustomEmoticons = previousCustom; state.RecentEmoticons = previousRecent; Saved = null; Feedback.Text = "无法保存颜文字，请检查资料目录权限。"; }
        }, true);
        Loaded += (_, _) => _text.Focus();
    }
}

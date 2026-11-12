using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tide.Core;

namespace Tide.Desktop;

internal static class SettingsPostColors
{
    internal static void Add(StackPanel page, Window owner, MainViewModel vm, Func<string, Action, Button> button)
    {
        var content = new StackPanel { Margin = new(10, 4, 0, 0) };
        page.Children.Add(new Expander { Header = "正文彩色文字", Content = content, Margin = new(-10, 12, 0, 0) });
        string[] modes = [PostColorOptions.Default, PostColorOptions.Vivid, PostColorOptions.Original];
        var mode = new ComboBox
        {
            ItemsSource = new[] { "默认适配", "鲜明显示", "尽量保留原色" },
            SelectedIndex = Math.Max(0, Array.IndexOf(modes, vm.State.PostColors.Mode)),
            Width = 144
        };
        SettingsLayout.Row(content, "显示方式", null, mode);

        var preview = new PostBody
        {
            Html = "普通正文　<font color='#cc0000'>红色强调</font>　<span style='color:#789922'>绿色公告</span><br><span style='color:#526ed6'>其他彩色文字</span>　<a href='https://www.nmbxd1.com/'>链接</a>",
            FontSize = 15, Focusable = false, IsHitTestVisible = false, IsTabStop = false
        };
        AutomationProperties.SetName(preview, "正文彩色文字预览");
        SettingsLayout.Block(content, preview);

        var red = Field("正文红色", vm.State.PostColors.Red, PostColors.RedSource);
        var green = Field("正文绿色", vm.State.PostColors.Green, PostColors.GreenSource);
        ColorField Field(string label, string value, string source)
        {
            var field = new ColorField(label, value, source, button, _ => PostColors.Display(Personalization.Rgb(source)));
            var group = new StackPanel { Margin = new(0, 10, 0, 0) };
            group.Children.Add(SettingsLayout.Text(label)); group.Children.Add(field); content.Children.Add(group);
            return field;
        }

        void Refresh() { red.Refresh(); green.Refresh(); }
        SettingsPersonalization.ThemeChanged += Refresh;
        content.Loaded += (_, _) => Refresh();
        owner.Closed += (_, _) => SettingsPersonalization.ThemeChanged -= Refresh;
        void Save()
        {
            if (mode.SelectedIndex < 0) return;
            vm.State.PostColors = new() { Mode = modes[mode.SelectedIndex], Red = red.Value, Green = green.Value };
            PostColors.Load(vm.State.PostColors);
            Refresh();
            vm.Save();
        }
        mode.SelectionChanged += (_, _) => Save();
        red.Changed += _ => Save();
        green.Changed += _ => Save();
    }
}

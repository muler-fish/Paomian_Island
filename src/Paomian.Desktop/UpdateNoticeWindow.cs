using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Paomian.Core;

namespace Paomian.Desktop;

internal sealed class UpdateNoticeWindow : DialogWindow
{
    private readonly ReleaseNoteList _versions;
    private readonly TextBlock _status = SettingsLayout.Text("", true);

    internal UpdateNoticeWindow(Version version, Action<string> open) : base("更新内容")
    {
        Width = 560; Height = 460; MinHeight = 300;
        ShowInTaskbar = false;

        var product = SettingsLayout.Text($"{AppIdentity.DisplayName} {version}");
        product.FontSize = 22; product.LineHeight = 30; product.FontWeight = FontWeights.SemiBold;
        Body.Children.Add(product);
        var subtitle = SettingsLayout.Text("已更新至新版本", true);
        subtitle.Margin = new(0, 2, 0, 6); Body.Children.Add(subtitle);
        _status.Margin = new(0, 8, 0, 0);
        _status.Visibility = Visibility.Collapsed;
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        Body.Children.Add(_status);
        _versions = new(open);
        Body.Children.Add(_versions);

        var history = SettingsLayout.Text("");
        history.Inlines.Add(ReleaseMarkdown.Link("查看完整更新日志", () => open(ReleaseUpdater.Repository + "/releases")));
        SetFooterLeadingContent(history);
        var acknowledge = Button("知道了", Dismiss, primary: true);
        acknowledge.IsDefault = true;
        Actions.Children.Add(acknowledge);
        Loaded += (_, _) => acknowledge.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.System || e.SystemKey != Key.F4 || Keyboard.Modifiers != ModifierKeys.Alt) return;
            e.Handled = true;
            Dismiss();
        };
    }

    internal void Display(IReadOnlyList<ReleaseNote> entries, string? status = null)
    {
        _status.Text = status ?? "";
        _status.Visibility = string.IsNullOrEmpty(status) ? Visibility.Collapsed : Visibility.Visible;
        _versions.Display(entries);
    }

    // 只有用户自己的关闭操作才算确认。Owner 关闭或应用退出时沿用 WPF 的默认结果。
    protected override void Dismiss() => DialogResult = true;
}

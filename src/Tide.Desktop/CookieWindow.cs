using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>二维码与文本共用校验、加密保存入口；成功后立即切换请求身份。</summary>
public sealed class CookieWindow : DialogWindow
{
    private readonly MainViewModel _vm;
    private readonly ListBox _list = new() { Height = 96, DisplayMemberPath = "Name" };
    private readonly TextBlock _name = new() { Text = "—", TextWrapping = TextWrapping.Wrap };
    private readonly PasswordBox _value = new();
    private readonly TextBlock _active = new();
    private readonly TextBlock _empty = Hint("尚未导入饼干。可以继续以游客身份浏览。 ");
    private readonly Button _import;
    private bool _readingQr, _closed;

    public CookieWindow(MainViewModel vm) : base("饼干管理")
    {
        Width = UiMetrics.DialogWidth; Height = 760; _vm = vm;
        var rowStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem)));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
        rowStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        _list.ItemContainerStyle = rowStyle;
        Closed += (_, _) => { _closed = true; _value.Clear(); };
        AutomationProperties.SetName(_name, "饼干名称");
        AutomationProperties.SetName(_value, "完整饼干内容");
        AutomationProperties.SetName(_active, "当前饼干");
        _value.PasswordChanged += (_, _) => UpdateNamePreview();
        Body.Children.Add(_active);
        var sources = new WrapPanel { Margin = new(-8, 12, 0, 4) };
        sources.Children.Add(Button("选择二维码图片", ChooseQr));
        sources.Children.Add(Button("粘贴二维码", Paste));
        sources.Children.Add(Button("官方用户系统", () => (Owner as MainWindow ?? Application.Current.MainWindow as MainWindow)?.OpenExternal(XIslandApi.Site + "/Member")));
        Body.Children.Add(sources);
        Label("饼干名称"); Body.Children.Add(_name);
        Label("饼干内容"); Body.Children.Add(_value);
        Body.Children.Add(Hint("可粘贴完整 userhash、Cookie 或二维码 JSON。饼干在本机加密保存。"));
        Body.Children.Add(Feedback); AutomationProperties.SetName(Feedback, "饼干导入结果"); AutomationProperties.SetLiveSetting(Feedback, AutomationLiveSetting.Polite);
        Label("已保存的饼干"); Body.Children.Add(_empty); Body.Children.Add(_list);
        var management = new WrapPanel { Margin = new(-8, 10, 0, 0) };
        management.Children.Add(Button("使用选中饼干", () => { if (_list.SelectedItem is CookieIdentity item) Switch(item.Id); }));
        management.Children.Add(Button("切换为游客", () => Switch("")));
        management.Children.Add(Button("移除选中项", Remove)); Body.Children.Add(management);
        AddAction("完成", Close);
        _import = Button("导入并使用", Import, true); Actions.Children.Add(_import);
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); Refresh();
    }

    private void Refresh()
    {
        _list.ItemsSource = _vm.State.Cookies.ToList();
        _list.Height = Math.Min(180, _vm.State.Cookies.Count * 36);
        _empty.Visibility = _vm.State.Cookies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.SelectedItem = _vm.State.Cookies.Find(c => c.Id == _vm.State.ActiveCookieId);
        _active.Text = "当前：" + (_vm.State.Cookies.Find(c => c.Id == _vm.State.ActiveCookieId)?.Name ?? "游客");
        UpdateNamePreview();
    }
    private async void ChooseQr()
    {
        var picker = new OpenFileDialog { Title = "选择饼干二维码图片", Filter = "二维码图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*", Multiselect = false };
        if (picker.ShowDialog(this) == true) await ReadQrAsync(() => CookieImport.ReadFileAsync(picker.FileName));
    }
    private async void Paste()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image) await ReadQrAsync(() => CookieImport.ReadImageAsync(image));
            else if (Clipboard.ContainsText()) Fill(CookieImport.Parse(Clipboard.GetText()));
            else ShowFailure(new ArgumentException("剪贴板中没有二维码图片或饼干文本。"));
        }
        catch (Exception ex) { ShowFailure(ex); }
    }
    // UI 与隔离测试都经过相同异步识别路径；关闭后不再回填或保存。
    internal async Task ReadQrAsync(Func<Task<CookieImport.Value>> read)
    {
        if (_readingQr || _closed) return;
        _readingQr = true; _import.IsEnabled = false; Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Feedback.Text = "正在识别二维码…";
        try { var value = await read(); if (!_closed) Fill(value); }
        catch (Exception ex) { if (!_closed) ShowFailure(ex); }
        finally { _readingQr = false; if (!_closed) _import.IsEnabled = true; }
    }
    private void Fill(CookieImport.Value value)
    {
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        // 名称与凭据一起保留在导入内容里；更换内容时重新解析，避免套用上一张二维码的名字。
        _value.Password = JsonSerializer.Serialize(new { cookie = value.Cookie, name = value.Name });
        Feedback.Text = "已识别，点击“导入并使用”保存。";
    }
    private string ImportedName(CookieImport.Value value, CookieIdentity? existing) =>
        value.Name.Length > 0 ? value.Name : existing?.Name ?? $"饼干 {_vm.State.Cookies.Count + 1}";

    private void UpdateNamePreview()
    {
        try
        {
            var value = CookieImport.Parse(_value.Password);
            _name.Text = ImportedName(value, _vm.State.Cookies.Find(c => Matches(c, value.Cookie)));
        }
        // 输入尚未完整时只清空名称；具体格式错误在点击导入时提示。
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException) { _name.Text = "—"; }
    }
    private void Import()
    {
        try
        {
            var value = CookieImport.Parse(_value.Password);
            var existing = _vm.State.Cookies.Find(c => Matches(c, value.Cookie));
            var name = ImportedName(value, existing);
            var entry = existing is null
                ? new CookieIdentity(Guid.NewGuid().ToString("N"), name, SecretProtection.Protect(value.Cookie))
                : existing with { Name = name };
            // 重复导入仍复用身份；有源名称时更新为源名称，无名称的文本导入保留已保存的名称。
            var cookies = _vm.State.Cookies.ToList();
            if (existing is null) cookies.Add(entry); else cookies[cookies.IndexOf(existing)] = entry;
            Commit(cookies, entry.Id);
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            _value.Clear(); Feedback.Text = $"已保存并使用「{entry.Name}」。";
        }
        catch (Exception ex) { ShowFailure(ex); }
    }
    private static bool Matches(CookieIdentity entry, string cookie)
    {
        try { return SecretProtection.Unprotect(entry.EncryptedValue) == cookie; }
        catch (System.Security.Cryptography.CryptographicException) { return false; }
    }
    private void Switch(string id)
    {
        try { Commit(_vm.State.Cookies.ToList(), id); Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); Feedback.Text = id.Length == 0 ? "已切换为游客。" : "已切换饼干。"; }
        catch (Exception ex) { ShowFailure(ex); }
    }
    private void Remove()
    {
        if (_list.SelectedItem is not CookieIdentity entry || !ConfirmWindow.Ask(this, "移除饼干", $"从本机移除「{entry.Name}」？")) return;
        try { Commit(_vm.State.Cookies.Where(c => c.Id != entry.Id).ToList(), _vm.State.ActiveCookieId == entry.Id ? "" : _vm.State.ActiveCookieId); Feedback.Text = "已从本机移除。"; }
        catch (Exception ex) { ShowFailure(ex); }
    }
    private void Commit(List<CookieIdentity> cookies, string active)
    {
        var previous = _vm.State.Cookies; var previousActive = _vm.State.ActiveCookieId;
        _vm.State.Cookies = cookies; _vm.State.ActiveCookieId = active;
        try { _vm.Store.Save(_vm.State); }
        catch { _vm.State.Cookies = previous; _vm.State.ActiveCookieId = previousActive; throw; }
        _vm.ApplyCookie(); Refresh();
    }
    private void ShowFailure(Exception ex)
    {
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        Feedback.Text = ex switch {
        JsonException or InvalidOperationException => "饼干 JSON 格式不正确，需要 cookie 或 userhash 文本字段。",
        ArgumentException => ex.Message,
        IOException or UnauthorizedAccessException => "无法读取图片或保存饼干，请检查文件及目录权限。",
        _ => "无法识别这张二维码图片，请换用清晰的 PNG 或 JPG，或粘贴饼干文本。"
        };
    }
}

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
    private readonly ListBox _list = new();
    private readonly TextBox _remark = new() { MaxLength = 80, IsEnabled = false };
    private readonly TextBlock _name = new() { Text = "—", TextWrapping = TextWrapping.Wrap };
    private readonly PasswordBox _value = new();
    private readonly TextBlock _active = new();
    private readonly TextBlock _empty = Hint("尚未导入饼干。可以继续以游客身份浏览。 ");
    private readonly Button _import;
    private readonly Button _setDefault, _remove, _saveRemark, _guest;
    private readonly Grid _management = new(), _importPage = new();
    private readonly StackPanel _details = new();
    private readonly TextBlock _detailName = new() { FontSize = 18, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detailState = Hint("");
    private readonly TextBlock _detailEmpty = Hint("在左侧选择一份饼干，查看信息或编辑备注。");
    private readonly Button _back;
    private readonly bool _fromComposer;
    private string? _currentCookieId;
    private int _viewGeneration;
    private bool _readingQr, _closed;
    public string? RequestedCookieId { get; private set; }
    private string CurrentCookieId => _currentCookieId ?? _vm.State.ActiveCookieId;

    public CookieWindow(MainViewModel vm, string? currentCookieId = null) : base("饼干管理")
    {
        Width = 760; Height = 540; MinWidth = 540; MinHeight = 400;
        _vm = vm; _currentCookieId = currentCookieId; _fromComposer = currentCookieId is not null;
        var rowStyle = new Style(typeof(ListBoxItem), (Style)FindResource(typeof(ListBoxItem)));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 6, 10, 6)));
        rowStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 36d));
        _list.ItemContainerStyle = rowStyle;
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        UseReadingScroll(_list);
        Closed += (_, _) => { _closed = true; _value.Clear(); };
        AutomationProperties.SetName(_name, "饼干名称");
        AutomationProperties.SetName(_value, "完整饼干内容");
        AutomationProperties.SetName(_active, "当前饼干");
        AutomationProperties.SetName(_remark, "饼干备注");
        AutomationProperties.SetName(_list, "已保存的饼干");
        AutomationProperties.SetName(_detailName, "所选饼干标识");
        AutomationProperties.SetName(_detailState, "所选饼干状态");
        _list.SelectionChanged += (_, _) => UpdateDetails();
        _remark.TextChanged += (_, _) => UpdateActions();
        _value.PasswordChanged += (_, _) => { if (!_readingQr) Feedback.Text = ""; UpdateNamePreview(); };

        var root = new Grid { Margin = new(UiMetrics.DialogInset, 0, UiMetrics.DialogInset, 0) };
        root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(_management); root.Children.Add(_importPage);
        Grid.SetRow(Feedback, 1); root.Children.Add(Feedback);
        SetBodyContent(root);
        AutomationProperties.SetName(Feedback, "饼干操作结果"); AutomationProperties.SetLiveSetting(Feedback, AutomationLiveSetting.Polite);

        _management.RowDefinitions.Add(new() { Height = GridLength.Auto }); _management.RowDefinitions.Add(new());
        var header = new DockPanel { Margin = new(0, 0, 0, 20) };
        var importEntry = Button("导入饼干", () => ShowImport(true), true);
        DockPanel.SetDock(importEntry, Dock.Right); header.Children.Add(importEntry);
        _guest = Button("切换为游客", () => Switch("")); _guest.SetResourceReference(StyleProperty, "Quiet");
        DockPanel.SetDock(_guest, Dock.Right); header.Children.Add(_guest);
        _active.TextWrapping = TextWrapping.Wrap; _active.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_active); _management.Children.Add(header);

        var columns = new Grid(); Grid.SetRow(columns, 1); _management.Children.Add(columns);
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1) });
        columns.ColumnDefinitions.Add(new() { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 220 });
        var left = new Grid { Margin = new(0, 0, 16, 0) };
        left.RowDefinitions.Add(new() { Height = GridLength.Auto }); left.RowDefinitions.Add(new());
        left.Children.Add(Hint("已保存的饼干")); Grid.SetRow(_list, 1); left.Children.Add(_list);
        Grid.SetRow(_empty, 1); _empty.VerticalAlignment = VerticalAlignment.Center; left.Children.Add(_empty); columns.Children.Add(left);
        var divider = new System.Windows.Shapes.Rectangle(); divider.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Line");
        Grid.SetColumn(divider, 1); columns.Children.Add(divider);
        var right = new Grid { Margin = new(24, 0, 0, 0) }; Grid.SetColumn(right, 2); columns.Children.Add(right);
        var scroll = new ScrollViewer { Content = _details, Padding = new(0, 0, 12, 0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        UseReadingScroll(scroll); right.Children.Add(scroll); right.Children.Add(_detailEmpty);
        _details.Children.Add(_detailName); _details.Children.Add(_detailState);
        _details.Children.Add(Hint("本机备注")); _details.Children.Add(_remark);
        _saveRemark = Button("保存备注", SaveRemark); _saveRemark.Margin = new(0, 10, 0, 0); _saveRemark.HorizontalAlignment = HorizontalAlignment.Left;
        _details.Children.Add(_saveRemark);
        _details.Children.Add(Hint("备注只保存在本机，不修改饼干原标识。"));
        _setDefault = Button("设置为默认饼干", () => { if (_list.SelectedItem is CookieIdentity item) Switch(item.Id); });
        _setDefault.Margin = new(0, 12, 0, 0); _setDefault.HorizontalAlignment = HorizontalAlignment.Left; _details.Children.Add(_setDefault);
        _remove = Button("移除饼干", Remove); _remove.Margin = new(0, 16, 0, 0); _remove.HorizontalAlignment = HorizontalAlignment.Left;
        _remove.SetResourceReference(StyleProperty, "Quiet"); _remove.SetResourceReference(ForegroundProperty, "Danger"); _details.Children.Add(_remove);

        _importPage.RowDefinitions.Add(new() { Height = GridLength.Auto }); _importPage.RowDefinitions.Add(new());
        _back = Button("‹ 返回管理", () => ShowImport(false)); _back.Margin = new(0, 0, 0, 12); _back.HorizontalAlignment = HorizontalAlignment.Left;
        _back.SetResourceReference(StyleProperty, "Quiet"); _importPage.Children.Add(_back);
        var form = new StackPanel(); var formScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(formScroll, 1); _importPage.Children.Add(formScroll); UseReadingScroll(formScroll);
        var sources = new WrapPanel { Margin = new(-8, 12, 0, 4) };
        sources.Children.Add(Button("选择二维码图片", ChooseQr));
        sources.Children.Add(Button("粘贴二维码", Paste));
        sources.Children.Add(Button("官方用户系统", () => (Owner as MainWindow ?? Application.Current.MainWindow as MainWindow)?.OpenExternal(XIslandApi.Site + "/Member")));
        form.Children.Add(sources);
        form.Children.Add(Hint("饼干内容")); form.Children.Add(_value);
        form.Children.Add(Hint("可粘贴完整 userhash、Cookie 或二维码 JSON。内容隐藏输入，并在本机加密保存。"));
        form.Children.Add(Hint("识别到的饼干名称")); form.Children.Add(_name);
        AddAction("完成", Close);
        _import = Button("导入并使用", Import, true); Actions.Children.Add(_import);
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); ShowImport(false); Refresh();
    }

    private void ShowImport(bool show)
    {
        ++_viewGeneration; _readingQr = false;
        Title = show ? "导入饼干" : "饼干管理";
        _management.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        _importPage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _import.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _value.Clear(); Feedback.Text = ""; UpdateNamePreview();
        if (show) _value.Focus(); else _list.Focus();
    }

    private void Refresh(string? selectedId = null)
    {
        selectedId ??= (_list.SelectedItem as CookieIdentity)?.Id ?? _vm.State.ActiveCookieId;
        _list.ItemTemplate = CookieItemTemplate.Create(_vm.State.ActiveCookieId, CurrentCookieId);
        _list.ItemsSource = _vm.State.Cookies.ToList();
        _empty.Visibility = _vm.State.Cookies.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.SelectedItem = _vm.State.Cookies.Find(c => c.Id == selectedId) ?? _vm.State.Cookies.FirstOrDefault();
        _active.Text = (_fromComposer ? "当前发言：" : "当前浏览：") + (_vm.State.Cookies.Find(c => c.Id == CurrentCookieId)?.Name ?? "游客");
        _guest.IsEnabled = CurrentCookieId.Length > 0 || _vm.State.ActiveCookieId.Length > 0;
        UpdateDetails();
        UpdateNamePreview();
    }
    private void UpdateDetails()
    {
        var item = _list.SelectedItem as CookieIdentity;
        _details.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        _detailEmpty.Visibility = item is null ? Visibility.Visible : Visibility.Collapsed;
        _detailName.Text = item?.Name ?? "";
        _detailState.Text = item is null ? "" : string.Join(" · ", new[] { item.Id == _vm.State.ActiveCookieId ? "默认饼干" : "", item.Id == CurrentCookieId ? (_fromComposer ? "当前发言使用" : "当前浏览使用") : "" }.Where(s => s.Length > 0));
        _remark.IsEnabled = item is not null; _remark.Text = item?.Remark ?? "";
        UpdateActions();
    }
    private void UpdateActions()
    {
        var item = _list.SelectedItem as CookieIdentity;
        _setDefault.IsEnabled = item is not null && item.Id != _vm.State.ActiveCookieId;
        _setDefault.Content = item is not null && item.Id == _vm.State.ActiveCookieId ? "已是默认饼干" : "设置为默认饼干";
        _remove.IsEnabled = item is not null;
        _saveRemark.IsEnabled = item is not null && _remark.Text.Trim() != item.Remark;
    }
    private void SaveRemark()
    {
        if (_list.SelectedItem is not CookieIdentity selected) { Feedback.Text = "请先选择一份饼干。"; return; }
        var previous = _vm.State.Cookies;
        try
        {
            _vm.State.Cookies = previous.Select(c => c.Id == selected.Id ? c with { Remark = _remark.Text.Trim() } : c).ToList();
            _vm.Store.Save(_vm.State);
            Refresh(selected.Id);
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); Feedback.Text = "备注已保存。";
        }
        catch (Exception ex) { _vm.State.Cookies = previous; ShowFailure(ex); }
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
        if (_readingQr || _closed || _importPage.Visibility != Visibility.Visible) return;
        var generation = _viewGeneration;
        _readingQr = true; _import.IsEnabled = false; Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Feedback.Text = "正在识别二维码…";
        try { var value = await read(); if (!_closed && generation == _viewGeneration) Fill(value); }
        catch (Exception ex) { if (!_closed && generation == _viewGeneration) ShowFailure(ex); }
        finally { if (!_closed && generation == _viewGeneration) { _readingQr = false; UpdateNamePreview(); } }
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
        _import.IsEnabled = false;
        try
        {
            var value = CookieImport.Parse(_value.Password);
            _name.Text = ImportedName(value, _vm.State.Cookies.Find(c => Matches(c, value.Cookie)));
            _name.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            _import.IsEnabled = !_readingQr && _importPage.Visibility == Visibility.Visible;
        }
        // 输入未通过现有解析规则时，直接显示校验反馈并保持导入按钮禁用。
        catch (Exception ex) when (ex is ArgumentException or JsonException or InvalidOperationException)
        {
            _name.Text = _value.Password.Length == 0 ? "—" : ex is ArgumentException ? ex.Message : "JSON 格式不正确，需要 cookie 或 userhash 文本字段。";
            _name.SetResourceReference(TextBlock.ForegroundProperty, _value.Password.Length == 0 ? "Muted" : "Danger");
        }
    }
    private void Import()
    {
        if (!_import.IsEnabled || _readingQr || _importPage.Visibility != Visibility.Visible) return;
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
            RequestedCookieId = entry.Id; if (_fromComposer) _currentCookieId = entry.Id;
            ShowImport(false); Refresh(entry.Id);
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
        try { Commit(_vm.State.Cookies.ToList(), id); if (id.Length == 0) { RequestedCookieId = ""; if (_fromComposer) _currentCookieId = ""; Refresh(); } Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); Feedback.Text = id.Length == 0 ? "已切换为游客。" : "已设置为默认饼干。"; }
        catch (Exception ex) { ShowFailure(ex); }
    }
    private void Remove()
    {
        if (_list.SelectedItem is not CookieIdentity entry || !ConfirmWindow.Ask(this, "移除饼干", $"从本机移除「{entry.Name}」？")) return;
        try
        {
            Commit(_vm.State.Cookies.Where(c => c.Id != entry.Id).ToList(), _vm.State.ActiveCookieId == entry.Id ? "" : _vm.State.ActiveCookieId);
            if (_fromComposer && _currentCookieId == entry.Id)
            {
                _currentCookieId = _vm.State.ActiveCookieId;
                RequestedCookieId = _currentCookieId;
                Refresh();
            }
            Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); Feedback.Text = "已从本机移除。";
        }
        catch (Exception ex) { ShowFailure(ex); }
    }
    private void Commit(List<CookieIdentity> cookies, string active)
    {
        var previous = _vm.State.Cookies; var previousActive = _vm.State.ActiveCookieId;
        _vm.State.Cookies = cookies; _vm.State.ActiveCookieId = active;
        try { _vm.Store.Save(_vm.State); }
        catch { _vm.State.Cookies = previous; _vm.State.ActiveCookieId = previousActive; throw; }
        _vm.ApplyCookie(); Refresh(active);
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

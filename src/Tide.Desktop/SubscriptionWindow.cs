using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Tide.Desktop;

/// <summary>可见、支持 IME 的多订阅号编辑器；编辑期间不切换后台账号，保存后才应用。</summary>
public sealed class SubscriptionWindow : DialogWindow
{
    private sealed record Row(string Key, TextBox Name, TextBox Number, RadioButton Active, Border Container);
    private readonly List<Row> _rows = [];
    private readonly StackPanel _accounts = new();
    public SubscriptionWindow(MainViewModel vm) : base("订阅号管理")
    {
        Width = UiMetrics.DialogWidth; Height = 590;
        Body.Children.Add(_accounts);
        foreach (var account in vm.SubscriptionAccounts) AddRow(account.Id, account.Name, account.Value, account.Id == vm.ActiveSubscriptionKey);
        var add = Button("添加订阅号", () => AddRow(Guid.NewGuid().ToString(), "", "", _rows.Count == 0));
        add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new(0, 12, 0, 0); Body.Children.Add(add);
        Body.Children.Add(Feedback);
        AddAction("取消", Close);
        AddAction("保存", () =>
        {
            try
            {
                var selected = _rows.Find(r => r.Active.IsChecked == true) ?? throw new ArgumentException("请选择当前订阅号。");
                vm.SaveSubscriptionAccounts(_rows.Select(r => new SubscriptionProfile(r.Key, r.Name.Text, r.Number.Text)), selected.Key);
                Close();
            }
            catch (ArgumentException ex) { Feedback.Text = ex.Message; }
            catch { Feedback.Text = "无法保存订阅号，请检查数据目录权限。"; }
        }, true);
    }

    private void AddRow(string key, string name, string number, bool active)
    {
        var container = new Border { BorderThickness = new(1), CornerRadius = new(4), Padding = new(12), Margin = new(0, 12, 0, 0) };
        container.SetResourceReference(Border.BorderBrushProperty, "Line");
        var panel = new StackPanel(); container.Child = panel;
        var top = new DockPanel(); panel.Children.Add(top);
        var select = new RadioButton { Content = "当前使用", GroupName = "SubscriptionAccount", IsChecked = active, VerticalAlignment = VerticalAlignment.Center };
        var remove = Button("移除", () =>
        {
            var row = _rows.Single(r => r.Key == key);
            _rows.Remove(row); _accounts.Children.Remove(container);
            if (row.Active.IsChecked == true && _rows.Count > 0) _rows[0].Active.IsChecked = true;
        });
        remove.ToolTip = "仅移除本机号码记录，不删除云端订阅";
        DockPanel.SetDock(remove, Dock.Right); top.Children.Add(remove); top.Children.Add(select);
        var grid = new Grid { Margin = new(0, 10, 0, 0) };
        grid.ColumnDefinitions.Add(new() { Width = new(145) }); grid.ColumnDefinitions.Add(new());
        var namePanel = new StackPanel { Margin = new(0, 0, 10, 0) };
        namePanel.Children.Add(new TextBlock { Text = "备注", Margin = new(0, 0, 0, 6) });
        var nameInput = new TextBox { Text = name }; namePanel.Children.Add(nameInput); grid.Children.Add(namePanel);
        var numberPanel = new StackPanel(); Grid.SetColumn(numberPanel, 1); grid.Children.Add(numberPanel);
        numberPanel.Children.Add(new TextBlock { Text = "订阅号", Margin = new(0, 0, 0, 6) });
        var numberInput = new TextBox { Text = number, MaxLength = 4096, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        InputMethod.SetIsInputMethodEnabled(numberInput, true);
        System.Windows.Automation.AutomationProperties.SetName(numberInput, "订阅号");
        System.Windows.Automation.AutomationProperties.SetName(nameInput, "订阅号备注");
        numberPanel.Children.Add(numberInput); panel.Children.Add(grid);
        var actions = new WrapPanel { Margin = new(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Button("随机生成", () => { numberInput.Text = Guid.NewGuid().ToString(); numberInput.Focus(); numberInput.SelectAll(); }));
        actions.Children.Add(Button("复制", () =>
        {
            try { if (numberInput.Text.Length > 0) { Clipboard.SetText(numberInput.Text); Feedback.Text = "已复制订阅号。"; } }
            catch (System.Runtime.InteropServices.ExternalException) { Feedback.Text = "剪贴板暂时不可用，请重试。"; }
        }));
        panel.Children.Add(actions);
        _rows.Add(new(key, nameInput, numberInput, select, container)); _accounts.Children.Add(container);
    }
}

using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

/// <summary>订阅号选择器：备注作主名称、号码作次级文字；无备注时只显示号码。只用合成号码。</summary>
internal static class SubscriptionDisplayChecks
{
    internal static async Task RunAsync(MainWindow window, string output, Action<Window, string> capture)
    {
        const string withRemark = "synthetic-subscription-0001", bare = "synthetic-subscription-0002-no-remark";
        var vm = window.Model;
        // 预置本机快照：fixture 不提供 feed，同步失败时保留并显示这份列表。
        vm.Store.SaveSubscriptions(withRemark, vm.State.ActiveCookieId, new([new Post { Id = 70000001, Fid = 4, ReplyCount = 0, Content = "订阅串一" }, new Post { Id = 70000002, Fid = 4, Content = "订阅串二" }], true, DateTimeOffset.UtcNow, []));
        vm.SaveSubscriptionAccounts([new("a", "默认", withRemark), new("b", "", bare)], "a");
        _ = vm.ShowSubscriptionsAsync(); await Task.Delay(400);
        var selector = (ComboBox)window.FindName("SubscriptionSelector");
        Require(selector.IsVisible, "订阅号选择器不可见");
        // feed 接口不返回回复数：订阅列表不显示「0 回复」，其他列表的卡片仍默认显示。
        var threadList = (ListBox)window.FindName("ThreadList");
        Require(vm.Posts.Count == 2 && vm.Posts.All(c => !c.ShowReplies) && new PostCard(new Post { Id = 1, ReplyCount = 5 }, "综合版1").ShowReplies, "订阅卡片仍显示回复数或普通卡片被隐藏");
        Require(UiRefinementChecks.Descendants<TextBlock>(threadList).Where(t => t.Name == "CardReplies").All(t => !t.IsVisible)
            && UiRefinementChecks.Descendants<TextBlock>(threadList).Any(t => t.Name == "CardReplies"), "订阅列表的回复数文字仍然可见");
        var a = vm.SubscriptionAccounts.Single(x => x.Id == "a"); var b = vm.SubscriptionAccounts.Single(x => x.Id == "b");
        Require(a.DisplayName == "默认" && a.Detail == withRemark, "有备注时主名称或次级号码错误");
        Require(b.DisplayName == bare && b.Detail == "", "无备注时未以号码为主名称或重复显示号码");
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme);
            selector.SelectedValue = "a"; await Until(() => Shown(selector, "默认") is { } shown && Text(shown).Length > 2);
            Require(Shown(selector, "默认") is { } remark && Text(remark) == "默认  " + withRemark && Equals(remark.ToolTip, withRemark), $"有备注时选择框显示或悬停号码错误: {string.Join(" | ", UiRefinementChecks.Descendants<TextBlock>(selector).Select(t => AutomationProperties.GetName(t) + "=" + Text(t) + "/" + t.ToolTip))}; key={vm.ActiveSubscriptionKey}; item={(selector.SelectedItem as SubscriptionProfile)?.Id ?? "null"}; value={selector.SelectedValue}; count={selector.Items.Count}");
            capture(window, Path.Combine(output, $"subscription-remark-{theme}.png"));
            selector.SelectedValue = "b"; await Until(() => Shown(selector, bare) is not null);
            Require(vm.ActiveSubscriptionKey == "b" && Shown(selector, bare) is { } number && Text(number).Trim() == bare && Equals(number.ToolTip, bare), "无备注时选择框显示错误或未切换订阅号");
            capture(window, Path.Combine(output, $"subscription-bare-{theme}.png"));
        }
        App.ApplyTheme("light"); selector.SelectedValue = "a"; await Task.Delay(100);
        // 改备注后选择器即时更新；清空备注退回号码显示。
        vm.SaveSubscriptionAccounts([new("a", "", withRemark), new("b", "", bare)], "a"); await Until(() => Shown(selector, withRemark) is not null);
        Require(Shown(selector, withRemark) is { } cleared && Text(cleared).Trim() == withRemark, "清空备注后选择器未改为显示号码");
        // 替换当前订阅号对象（删掉当前号、换成新号）后，选择框仍要选中并显示新的当前号。
        vm.SaveSubscriptionAccounts([new("c", "新号", "synthetic-subscription-0003"), new("b", "", bare)], "c");
        await Until(() => selector.SelectedItem is not null);
        Require(selector.SelectedItem is SubscriptionProfile { Id: "c" } && Shown(selector, "新号") is not null, $"替换当前订阅号后选择框为空: item={(selector.SelectedItem as SubscriptionProfile)?.Id ?? "null"}; value={selector.SelectedValue}");
        Console.WriteLine("PASS: replacing the active subscription keeps the selector populated; subscription cards hide reply count (feed has none); subscription selector uses remark as title with muted full ID, ID-only without remark, tooltip shows full ID, light/dark.");
    }

    // 选择框模板在下一轮排版后才生成，按条件轮询而不是固定等待。
    private static async Task Until(Func<bool> done, int timeoutMs = 3000)
    {
        for (var waited = 0; waited < timeoutMs && !done(); waited += 50) await Task.Delay(50);
        await Task.Delay(50);
    }
    private static TextBlock? Shown(ComboBox selector, string name) =>
        UiRefinementChecks.Descendants<TextBlock>(selector).FirstOrDefault(t => AutomationProperties.GetName(t) == name);
    // 模板用 Run 组合主名称与次级号码，TextBlock.Text 为空，需要读取各 Run。
    private static string Text(TextBlock block) => string.Concat(block.Inlines.OfType<System.Windows.Documents.Run>().Select(r => r.Text));
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

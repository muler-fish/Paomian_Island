using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

/// <summary>用固定接口响应验证状态组合，避免在线内容变化使红名 / SAGE 检查失去覆盖。</summary>
internal static class UiRefinementChecks
{
    public static XIslandApi CreateApi() => new(new HttpClient(new FixtureHandler()));

    public static async Task RunAsync(MainWindow window, MainViewModel vm, string output, Action<Window, string> capture)
    {
        vm.ForumGroups.Clear();
        vm.ForumGroups.Add(new("时间线", [new(1, "综合线", "时间线", IsTimeline: true)]));
        vm.ForumGroups.Add(new("综合", [new(30, "综合版1", "综合")]));
        vm.ForumGroups.Add(new("兴趣", [new(17, "动画", "兴趣")]));
        await vm.OpenThreadAsync(101, 1);
        Require(vm.Original?.Id == 101, "固定接口响应未读入。");
        vm.Posts.Clear();
        foreach (var read in vm.ReadingPosts) vm.Posts.Add(new(read.Post, "综合版1"));
        await LayoutAsync(window);
        var groups = Descendants<Expander>(window).ToList();
        Require(groups.Count == 3 && groups.All(g => !g.IsExpanded), "分组没有默认收起。");
        // 通过控件自动化模式切换，检查双向绑定和内容可见性，而非只检查 XAML 字符串。
        var header = (ToggleButton)groups[0].Template.FindName("HeaderSite", groups[0]);
        var toggle = (IToggleProvider)new ToggleButtonAutomationPeer(header).GetPattern(PatternInterface.Toggle);
        toggle.Toggle();
        await LayoutAsync(window);
        Require(groups[0].IsExpanded && Descendants<Button>(groups[0]).Any(b => b.IsVisible), "分组展开未显示版块。");
        header.Focus();
        Require(header.IsKeyboardFocused, "分组标题无法获得键盘焦点。");
        capture(window, Path.Combine(output, "groups-expanded.png"));
        toggle.Toggle();
        await LayoutAsync(window);
        Require(!groups[0].IsExpanded, "分组无法再次收起。");

        var results = new Dictionary<string, object> { ["groupsCollapsedAndToggle"] = true, ["keyboardFocus"] = true };
        foreach (var theme in new[] { "light", "dark" })
        {
            App.ApplyTheme(theme);
            await LayoutAsync(window);
            var identities = Descendants<PostIdentity>(window).Where(i => i.IsVisible && i.Post is not null).ToList();
            Require(identities.Any(i => i.Post!.IsAdmin) && identities.Any(i => !i.Post!.IsAdmin), "未显示红名和普通饼干。");
            Require(identities.Any(i => i.Post!.IsSage) && identities.Any(i => !i.Post!.IsSage), "缺少 SAGE 或普通状态。");
            foreach (var identity in identities) CheckIdentity(identity);
            var sample = identities.First(i => i.Post!.IsAdmin && i.Post.IsSage);
            var original = sample.Post;
            // 在同一控件上覆盖红名 × SAGE 四种组合，检查回收换绑后旧状态确实消失。
            foreach (var read in vm.ReadingPosts)
            {
                sample.Post = read.Post;
                await LayoutAsync(window);
                CheckIdentity(sample);
            }
            sample.Post = original;
            await LayoutAsync(window);
            CheckIdentity(sample);
            results[theme + "IdentityAndRebind"] = true;
            results[theme + "CookieContrast"] = Contrast(
                Brush("CookieForeground").Color, Brush("CookieBackground").Color);
            results[theme + "RedNameContrast"] = Contrast(
                Brush("AdminCookieForeground").Color, Brush("AdminCookieBackground").Color);
            var primary = (Wpf.Ui.Controls.Button)window.FindName("NewThreadButton");
            var foreground = ((SolidColorBrush)primary.Foreground).Color;
            var primaryContrasts = new[] { primary.Background, primary.MouseOverBackground, primary.PressedBackground }
                .Select(b => Contrast(foreground, ((SolidColorBrush)b).Color)).ToArray();
            Require(primaryContrasts.All(c => c >= 4.5), "主按钮在正常、悬停或按下状态的对比度不足。");
            results[theme + "PrimaryContrast"] = primaryContrasts;
            capture(window, Path.Combine(output, $"identity-{theme}.png"));
        }
        // 引用窗口复用同一组件，不能漏掉红名或 SAGE。
        var reference = new TextWindow("引用 · 界面验证", "") { Owner = window, ShowInTaskbar = false };
        reference.SetPost(vm.Original!); reference.Show();
        await LayoutAsync(reference);
        CheckIdentity(Descendants<PostIdentity>(reference).Single());
        capture(reference, Path.Combine(output, "identity-reference.png"));
        reference.Close();
        results["referenceIdentity"] = true;
        window.Width = 1060; window.Height = 720;
        await LayoutAsync(window);
        capture(window, Path.Combine(output, "identity-compact.png"));
        foreach (var identity in Descendants<PostIdentity>(window).Where(i => i.IsVisible))
        {
            var bounds = identity.TransformToAncestor(window).TransformBounds(new Rect(identity.RenderSize));
            Require(bounds.Right <= window.ActualWidth && bounds.Left >= 0, "饼干标记在窄窗口横向溢出。");
        }
        results["compactIdentityFits"] = true;
        // 以实际内容边界验证阅读区，而非只检查 ListBox 属性；库模板不一定使用 Padding。
        var reader = (ListBox)window.FindName("ReaderList");
        foreach (var width in new[] { 900d, 1060d, 1280d })
        {
            window.Width = width; await LayoutAsync(window);
            var readerBounds = reader.TransformToAncestor(window).TransformBounds(new Rect(reader.RenderSize));
            Require(readerBounds.Right <= window.ActualWidth - 20, "阅读区右侧未保留可见留白。");
            foreach (var text in Descendants<TextBlock>(reader).Where(t => t.IsVisible && t.Text.StartsWith("No.")))
            {
                var bounds = text.TransformToAncestor(window).TransformBounds(new Rect(text.RenderSize));
                Require(bounds.Right <= readerBounds.Right - 10 && bounds.Left >= readerBounds.Left, "楼号被阅读区或覆盖式滚动条遮住。");
            }
        }
        results["readerFitsWidths"] = new[] { 900, 1060, 1280 };
        var sageContrast = Contrast(Brush("OnSage").Color, Brush("SageBackground").Color);
        Require(sageContrast >= 4.5, "SAGE 对比度不足。");
        results["sageContrast"] = sageContrast;
        results["identityCombinationsPerTheme"] = vm.ReadingPosts.Count;
        File.WriteAllText(Path.Combine(output, "ui-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        App.ApplyTheme("light");
        Console.WriteLine("PASS: collapsed groups, accessible toggle, light/dark identity states, rebind, reference, compact layout.");
    }

    private static void CheckIdentity(PostIdentity identity)
    {
        var cookie = (Border)identity.FindName("CookieBadge");
        var sage = (Border)identity.FindName("SageBadge");
        var post = identity.Post!;
        var expected = Brush(post.IsAdmin ? "AdminCookieBackground" : "CookieBackground");
        Require(((SolidColorBrush)cookie.Background).Color == expected.Color, "饼干底色与红名状态不符。");
        var label = Descendants<TextBlock>(cookie).Single();
        Require(label.Text == post.UserHash, "饼干 ID 与帖子不符。");
        Require(((SolidColorBrush)label.Foreground).Color == Brush(post.IsAdmin ? "AdminCookieForeground" : "CookieForeground").Color, "饼干文字颜色不符。");
        Require(Contrast(((SolidColorBrush)label.Foreground).Color, expected.Color) >= 4.5, "饼干对比度不足。");
        Require(sage.Visibility == (post.IsSage && identity.ShowSage ? Visibility.Visible : Visibility.Collapsed), "SAGE 状态显示不符。");
    }

    private static SolidColorBrush Brush(string name) => (SolidColorBrush)Application.Current.FindResource(name);
    private static double Contrast(Color a, Color b)
    {
        static double Linear(byte c) { var v = c / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        var first = Luminance(a); var second = Luminance(b);
        return Math.Round((Math.Max(first, second) + .05) / (Math.Min(first, second) + .05), 2);
    }
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static async Task LayoutAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
    }
    private sealed class FixtureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("界面验证禁止提交发言。");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""
                {"id":"101","fid":"30","user_hash":"admin","admin":"1","sage":"1","ReplyCount":"3",
                 "title":"界面验证：饼干与 SAGE","content":"红名饼干和 SAGE 应分别显示。","now":"2026-10-20",
                 "Replies":[
                   {"id":102,"fid":30,"user_hash":"7zWLImE","admin":0,"sage":0,"content":"普通发言人使用青绿色饼干，不显示 SAGE。"},
                   {"id":103,"fid":30,"user_hash":"RedName","admin":1,"sage":0,"content":"红名与 SAGE 独立判断。"},
                   {"id":104,"fid":30,"user_hash":"Normal1","admin":0,"sage":1,"content":"普通饼干也可以带 SAGE 标志。"}]}
                """) });
        }
    }
}

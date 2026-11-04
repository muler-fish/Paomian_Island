using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ContinuationVisualChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window, string> capture)
    {
        var handler = new Handler();
        var vm = new MainViewModel(new XIslandApi(new HttpClient(handler)), new LocalStore(Path.Combine(output, "loading-profile")));
        var window = new MainWindow(vm) { Owner = owner, ShowInTaskbar = false };
        window.Show();
        try
        {
            await vm.SelectForumAsync(new(4, "综合版1", "综合", MaxPage: 3)); await Task.Delay(180);
            var list = (ListBox)window.FindName("ThreadList");
            var height = list.ActualHeight;
            var pending = vm.LoadMoreListAsync(); await Task.Delay(180);
            var bar = (ProgressBar)window.FindName("ListContinuationProgress");
            var reader = (ProgressBar)window.FindName("ThreadContinuationProgress");
            Require(vm.ListAppending && bar.IsVisible, "续页进度条未显示");
            Require(ReferenceEquals(bar.Style, reader.Style) && bar.ActualWidth >= list.ActualWidth - 2 && Math.Abs(bar.ActualHeight - reader.Height) <= 1, $"串列表没有使用串阅读区的整栏进度条样式: {bar.ActualWidth}/{list.ActualWidth}, {bar.ActualHeight}/{reader.Height}");
            Require(!((Border)window.FindName("ListTailIndicator")).IsVisible && Math.Abs(list.ActualHeight - height) < 1, "加载时仍显示悬浮框或挤占正文");
            capture(window, Path.Combine(output, "list-continuation-light.png"));
            App.ApplyTheme("dark"); await Task.Delay(180); capture(window, Path.Combine(output, "list-continuation-dark.png"));
            Require(Equals(bar.Foreground, Application.Current.FindResource("Accent")) && Equals(reader.Foreground, bar.Foreground), "续页进度条未随主题使用强调色");
            handler.Release.TrySetResult(); await pending; await Task.Delay(180);
            Require(!bar.IsVisible && vm.ListAppendFailed, "失败后进度条未结束或重试状态丢失");
            var retry = UiRefinementChecks.Descendants<Button>(window).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "重试加载串列表");
            Require(retry.IsVisible && retry.Command == vm.RetryListLoadCommand, "续页失败没有保留原重试入口");
            handler.Fail = false; vm.RetryListLoadCommand.Execute(null);
            for (var i = 0; i < 50 && vm.ListBusy; i++) await Task.Delay(40);
            Require(vm.Posts.Count == 40 && !vm.ListAppendFailed, "重试没有完成原有的追加逻辑");
            Console.WriteLine("PASS: shared list/reader progress style, full-width loading, unchanged viewport and retry.");
        }
        finally { handler.Release.TrySetResult(); window.Close(); App.ApplyTheme("light"); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal TaskCompletionSource Release { get; } = new();
        internal bool Fail = true;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method != HttpMethod.Get) throw new InvalidOperationException("Read-only fixture");
            var second = request.RequestUri!.Query.Contains("page=2");
            if (second) { await Release.Task.WaitAsync(ct); if (Fail) throw new HttpRequestException("模拟续页失败"); }
            var posts = Enumerable.Range(1, 20).Select(i => new { id = 81000000 + (second ? 20 : 0) + i, fid = 4, user_hash = "列表测试", content = "保留正在阅读的内容。<br>续页使用整栏细进度条。", ReplyCount = 2 });
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(posts)) };
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}

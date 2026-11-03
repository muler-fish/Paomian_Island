using System.IO;
using System.Net;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class ReadingIdentityChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window,string> capture)
    {
        using var api = new XIslandApi(new System.Net.Http.HttpClient(new Handler()));
        var model = new MainViewModel(api, new LocalStore(Path.Combine(output,"reading-profile")));
        var window = new MainWindow(model) { Owner=owner, ShowInTaskbar=false, Width=1300, Height=760 };
        try
        {
            window.Show();
            await model.OpenThreadAsync(101, 1);
            model.Posts.Clear(); model.Posts.Add(new(model.Original!, "速报2"));
            await Task.Delay(200); window.UpdateLayout();
            var list = (ListBox)window.FindName("ThreadList");
            var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var preview = UiRefinementChecks.Descendants<PostPreview>(item).Single();
            var bodies = UiRefinementChecks.Descendants<PostBody>(window).Where(b=>b.IsVisible).ToArray();
            var fonts = TypographyChecks.Runs(preview);
            var rows = bodies.Select(b=>new { Weight=b.FontWeight.ToString(), DocumentWeight=b.Document.FontWeight.ToString(), Runs=TypographyChecks.Runs(b) }).ToArray();
            var labels = UiRefinementChecks.Descendants<EmojiLabel>(window).Where(l=>l.IsVisible).Select(l=>l.Text).ToArray();
            File.WriteAllText(Path.Combine(output,"reading-identity.json"), JsonSerializer.Serialize(new { PreviewWeight=preview.FontWeight.ToString(), Fonts=fonts, Rows=rows, Labels=labels },new JsonSerializerOptions{WriteIndented=true}));
            capture(window,Path.Combine(output,"reading-before-or-after.png"));
            var regular = fonts.Concat(rows.SelectMany(r=>r.Runs)).Where(r=>r.Text.Contains("普通正文"));
            if (!regular.Any() || regular.Any(r=>!r.Font.EndsWith("misans-light.otf") || r.Simulated)) throw new Exception("正文没有使用较轻的实际 Light 字形");
            var dice = rows.SelectMany(r=>r.Runs).Where(r=>r.Text=="2").ToArray();
            if (dice.Length==0 || dice.Any(r=>r.Simulated)) throw new Exception("骰子结果使用了模拟加粗");
            if (!labels.Contains("回应作者")) throw new Exception("串内没有显示作者名称");
            if (labels.Contains("无名氏")) throw new Exception("默认作者仍占行");
            foreach(var theme in new[]{"light","dark"})
            {
                App.ApplyTheme(theme); await Task.Delay(120);
                capture(window,Path.Combine(output,"reading-"+theme+".png"));
                ReadingPolishChecks.CaptureScreen(window,Path.Combine(output,"reading-native-"+theme+".png"));
            }
            Console.WriteLine("PASS: real list/reader Light glyphs, dice without simulated bold, reader authors.");
        }
        finally { window.Close(); App.ApplyTheme("light"); }
    }

    private sealed class Handler : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken ct)
        {
            if(request.Method != System.Net.Http.HttpMethod.Get) throw new Exception("Read-only fixture");
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK) { Content=new System.Net.Http.StringContent("""
                {"id":101,"fid":4,"title":"标题字重","name":"主楼作者","user_hash":"TestPo","content":"普通正文 千看七战越<br><b>2</b>[1,2]","ReplyCount":2,"Replies":[
                  {"id":102,"fid":4,"name":"回应作者","user_hash":"TestRe","content":"普通正文"},
                  {"id":103,"fid":4,"name":"无名氏","content":"普通正文"}]}
                """) });
        }
    }
}

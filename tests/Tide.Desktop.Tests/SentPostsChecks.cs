using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

internal static class SentPostsChecks
{
    internal static async Task RunAsync(MainWindow owner, string output, Action<Window,string> capture)
    {
        var handler = new Handler();
        var store = new LocalStore(Path.Combine(output,"profile-"+Guid.NewGuid().ToString("N")));
        var model = new MainViewModel(new XIslandApi(new HttpClient(handler)),store);
        model.State.Forums.Add(new(4,"综合版1","综合"));
        var cookie = new CookieIdentity("test", "测试饼干", SecretProtection.Protect(new string('a',32)));
        model.State.Cookies.Add(cookie); model.State.ActiveCookieId=cookie.Id;
        var main = new MainWindow(model) { Owner=owner, ShowInTaskbar=false };
        main.Show();
        try
        {
            var button = (Button)main.FindName("MyPostsButton");
            var draft = UiRefinementChecks.Descendants<Button>(main).Single(b=>UiRefinementChecks.Descendants<TextBlock>(b).Any(t=>t.Text=="草稿箱"));
            Require(button.TranslatePoint(new Point(),main).Y < draft.TranslatePoint(new Point(),main).Y,"入口没有位于草稿箱上方");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(100);
            var empty = main.OwnedWindows.OfType<MyPostsWindow>().Single();
            var emptyVm = (SentPostsViewModel)UiRefinementChecks.Descendants<MyPostsView>(empty).Single().DataContext;
            Require(emptyVm.EmptyThreads && emptyVm.EmptyReplies,"新资料不是空记录"); empty.Close();

            var editor = Editor(0,"主题标题","自定义作者","正文 <b>原始文本</b> 😀\n第二行");
            try
            {
                await Send(editor,false); Require(handler.Posts==0 && store.ReadSentPosts().Count==0,"取消发送被记录");
                handler.Result="fail"; await Send(editor,true); await Until(()=>handler.Posts==1 && Feedback(editor).Contains("模拟失败"));
                Require(!editor.Delivered && store.ReadSentPosts().Count==0,"失败发送被记录");
                handler.Result="unknown"; await Send(editor,true); await Until(()=>handler.Posts==2 && Feedback(editor).Contains("无法确认"));
                Require(!editor.Delivered && store.ReadSentPosts().Count==0,"未知送达被记录");
                handler.Result="success"; await Send(editor,true); await Until(()=>editor.Delivered);
            }
            finally { if(editor.IsVisible) editor.Close(); }
            var first=store.ReadSentPosts().Single();
            Require(!first.IsReply && first.ThreadId==101 && first.Title=="主题标题" && first.Name=="自定义作者" && first.Content.Contains("😀\r\n第二行"),"主题发送快照缺失"); // 编辑器按行分段，换行统一读回为 \r\n，与按 Enter 输入一致
            handler.Result="no-id";
            var reply=Editor(101,"","","我的回复"); await Send(reply,true); await Until(()=>reply.Delivered);
            var noId=Editor(0,"没有返回地址","","成功但没有串号"); await Send(noId,true); await Until(()=>noId.Delivered);
            await Until(()=>store.ReadSentPosts().Any(p=>p.ThreadId==202));
            handler.FailLookup=true;
            var pending=Editor(0,"尚未查到","","暂时找不到的主题"); await Send(pending,true); await Until(()=>pending.Delivered);
            var records=new LocalStore(store.Root).ReadSentPosts();
            Require(records.Count==4 && records.Count(p=>p.IsReply)==1 && records.Single(p=>p.IsReply).ThreadId==101,"重启读取或回复分类错误");
            Require(records.Any(p=>!p.IsReply && p.ThreadId is null),"成功无地址的记录丢失或编造串号");
            var json=File.ReadAllText(Path.Combine(store.Root,"sent-posts.json"));
            Require(!json.Contains(cookie.EncryptedValue) && !json.Contains(new string('a',32)),"记录包含认证内容");
            var recordWindow=new MyPostsWindow(model) { Owner=main,ShowInTaskbar=false };
            recordWindow.Show(); await Task.Delay(150);
            try
            {
                var view=UiRefinementChecks.Descendants<MyPostsView>(recordWindow).Single();
                var vm=(SentPostsViewModel)view.DataContext;
                Require(vm.Threads.Count==3 && vm.Replies.Count==1 && !vm.Threads.Single(p=>p.Record.ThreadId is null).CanOpen,"双栏目或未知地址按钮错误");
                var pendingItem = vm.Threads.Single(p=>p.Record.ThreadId is null);
                capture(recordWindow,Path.Combine(output,"sent-pending.png"));
                handler.FindPending=true; handler.FailLookup=false;
                vm.FindCommand.Execute(pendingItem);
                await Until(()=>vm.Threads.Any(p=>p.Record.ThreadId==303));
                Require(new LocalStore(store.Root).ReadSentPosts().Any(p=>p.ThreadId==303),"已有记录手动补查未持久化");
                Require(handler.Posts==6,"补查重复提交了 POST");
                foreach(var theme in new[]{"light","dark"})
                {
                    App.ApplyTheme(theme); await Task.Delay(100); capture(recordWindow,Path.Combine(output,"sent-topics-"+theme+".png"));
                }
                vm.ViewCommand.Execute(vm.Threads.Single(p=>p.Record.ThreadId==101)); await Task.Delay(100);
                var detail=recordWindow.OwnedWindows.OfType<TextWindow>().Single();
                var body=UiRefinementChecks.Descendants<PostBody>(detail).Single();
                Require(EmojiText.Read(body.Document.ContentStart,body.Document.ContentEnd).Contains("<b>原始文本</b> 😀"),"本机原文被当作 HTML 执行"); detail.Close();
                ((TabControl)view.FindName("SpeechTabs")).SelectedIndex=1; await Task.Delay(100);
                capture(recordWindow,Path.Combine(output,"sent-replies.png"));
                recordWindow.Width=480; await Task.Delay(100); capture(recordWindow,Path.Combine(output,"sent-narrow.png"));
                vm.OpenCommand.Execute(vm.Replies[0]); await Until(()=>!recordWindow.IsVisible);
                Require(model.Original?.Id==101,"回复没有打开原串");
            }
            finally { if(recordWindow.IsVisible) recordWindow.Close(); }
            var broken=new LocalStore(Path.Combine(output,"corrupt-"+Guid.NewGuid().ToString("N")));
            File.WriteAllText(Path.Combine(broken.Root,"sent-posts.json"),"broken-json");
            try { broken.AddSentPost(first); throw new Exception("损坏记录被静默覆盖"); } catch(IOException) { }
            Require(File.ReadAllText(Path.Combine(broken.Root,"sent-posts.json"))=="broken-json","损坏原文件未保留");
            Console.WriteLine("PASS: sidebar, cancel/failure/unknown excluded, topics/replies persisted, no secret, restart, automatic ID lookup, lookup failure preserves delivery, manual backfill, no repeated POST, two tabs, full text, navigation, corrupt-file retention. All POSTs mocked.");
        }
        finally { main.Close(); App.ApplyTheme("light"); }

        ComposerWindow Editor(long thread,string title,string name,string content)
        {
            var e=new ComposerWindow(model,thread) { Owner=main,ShowInTaskbar=false }; e.Show();
            UiRefinementChecks.Descendants<Expander>(e).Single(x=>Equals(x.Header,"可选项")).IsExpanded=true;
            e.UpdateLayout();
            UiRefinementChecks.Descendants<EmojiEditor>(e).Single().Text=content;
            foreach(var box in UiRefinementChecks.Descendants<Wpf.Ui.Controls.TextBox>(e))
            {
                var label=System.Windows.Automation.AutomationProperties.GetName(box);
                if(label=="可选标题") box.Text=title; if(label=="可选名称") box.Text=name;
            }
            return e;
        }
    }
    private static string Feedback(Window w)=>string.Join("\n",UiRefinementChecks.Descendants<TextBlock>(w).Select(t=>t.Text));
    private static async Task Send(ComposerWindow editor,bool confirm)
    {
        Task? task=null;
        _=editor.Dispatcher.BeginInvoke(()=>task=Check(),DispatcherPriority.Background);
        Click(editor,"预览并发送"); if(task is not null) await task;
        async Task Check()
        {
            await Until(()=>editor.OwnedWindows.OfType<TextWindow>().Any());
            var preview=editor.OwnedWindows.OfType<TextWindow>().Single();
            if(confirm) Click(preview,"确认发送"); else preview.Close();
        }
    }
    private static void Click(Window w,string text)=>UiRefinementChecks.Descendants<Button>(w).Single(b=>Equals(b.Content,text)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> ready) { for(var i=0;i<80&&!ready();i++) await Task.Delay(40); Require(ready(),"UI 操作超时"); }
    private static void Require(bool ok,string message) { if(!ok) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public int Posts;
        public string Result="success";
        public bool FindPending;
        public bool FailLookup;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            string content;
            if(FailLookup && request.Method==HttpMethod.Get && request.RequestUri!.AbsolutePath!="/Forum") throw new HttpRequestException("simulated lookup failure");
            if(request.Method==HttpMethod.Post)
            {
                Posts++;
                content=Result switch { "fail"=>"<p class=\"error\">模拟失败</p>","unknown"=>"unknown response", "no-id"=>"<p class=\"success\">成功</p>",_=>"<p class=\"success\">成功</p><a href=\"/t/101\">跳转</a>" };
            }
            else if(request.RequestUri!.AbsolutePath=="/Api/getLastPost" || request.RequestUri!.Query.Contains("id=202") || request.RequestUri!.Query.Contains("id=303")) content=System.Text.Json.JsonSerializer.Serialize(new {
                id=FindPending?303:202,resto=0,fid=4,now=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss"),user_hash="测试饼干",
                title=FindPending?"尚未查到":"没有返回地址",name="无名氏",content=FindPending?"暂时找不到的主题":"成功但没有串号"
            });
            else if(request.RequestUri!.AbsolutePath.EndsWith("showf")) content="[]";
            else content=request.RequestUri!.AbsolutePath=="/Forum" ? "<html/>" : """{"id":101,"fid":4,"content":"主楼","Replies":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(content)});
        }
    }
}

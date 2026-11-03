using System.Net;
using System.Text.Json;
using Tide.Core;

internal static class SentThreadChecks
{
    internal static async Task RunAsync()
    {
        var sent = new SentPost("saved", false, null, 4, "综合版1", "cookieA", "", "", "中文 & <b>字面</b>\n第二行", false,
            new DateTimeOffset(2026, 10, 31, 12, 34, 55, TimeSpan.FromHours(8)));
        Dictionary<string, object> Post(long id = 123) => new()
        {
            ["id"]=id,["fid"]=4,["resto"]=0,["user_hash"]="cookieA",["now"]="2026-10-31(六)12:34:52",
            ["title"]="无标题",["name"]="无名氏",["content"]="中文 &amp; &lt;b&gt;字面&lt;/b&gt;<br />\n第二行"
        };
        var success = Post();
        Assert(await Find(sent,success,success,[])==123,"last-post lookup lost the topic ID");
        foreach(var pair in new (string Key,object Value)[]{("resto",888),("user_hash","cookieB"),("now","2026-10-31(六)10:34:52"),
            ("title","另一标题"),("name","另一作者"),("content","其他正文")})
        {
            var unrelated=Post(); unrelated[pair.Key]=pair.Value;
            Assert(await Find(sent,unrelated,unrelated,[]) is null,"matched unrelated latest post: "+pair.Key);
        }
        var wrongForum=Post(); wrongForum["fid"]=18;
        Assert(await Find(sent,success,wrongForum,[]) is null,"matched same content from another forum");
        Assert(await Find(sent,null,success,[success])==123,"forum fallback did not recover an older record");
        Assert(await Find(sent,null,success,[success,Post(124)]) is null,"ambiguous topic was assigned");
        Assert(await Find(sent,"invalid-json",success,[success])==123,"malformed last-post response blocked fallback");
        var dice=Post(); dice["content"]="结果 <b>3</b>[1,6] / <b>-2</b>[-4,-1]";
        var rolled=sent with { Content="结果 [1,6] / [-4,-1]" };
        Assert(await Find(rolled,dice,dice,[])==123,"server dice formatting prevented identification");
        dice["content"]="结果 <b>9</b>[1,6] / <b>-2</b>[-4,-1]";
        Assert(await Find(rolled,dice,dice,[]) is null,"invalid dice range was accepted");

        using(var canceled = new CancellationTokenSource())
        using(var api = new XIslandApi(new HttpClient(new Handler((_,ct)=>Task.Delay(Timeout.Infinite,ct).ContinueWith<HttpResponseMessage>(
            _=>throw new OperationCanceledException(ct),TaskScheduler.Default)))))
        {
            canceled.Cancel();
            try { await api.FindSentThreadAsync(sent,"original-cookie-secret",canceled.Token); throw new Exception("cancellation ignored"); }
            catch(OperationCanceledException) { }
        }
        var root=Path.Combine(Path.GetTempPath(),"tide-sent-thread-"+Guid.NewGuid().ToString("N"));
        var store=new LocalStore(root);
        store.AddSentPost(sent); store.AddSentPost(sent with { Id="another",IsReply=true,ThreadId=777 });
        store.SetSentThreadId(sent.Id,123); store.SetSentThreadId(sent.Id,456); store.SetSentThreadId("another",999);
        var reloaded=new LocalStore(root).ReadSentPosts();
        Assert(reloaded.Count==2 && reloaded.Single(p=>p.Id==sent.Id).ThreadId==123 && reloaded.Single(p=>p.Id=="another").ThreadId==777,
            "ID backfill overwrote existing IDs or other records");
        Console.WriteLine("PASS sent-thread: last-post + forum lookup, original identity, HTML/default fields/dice, mismatches/ambiguity/cancel, atomic record backfill (mock GET only).");
    }

    private static async Task<long?> Find(SentPost sent, object? last, object original, object[] forum)
    {
        var gets=0;
        using var api=new XIslandApi(new HttpClient(new Handler((req,_)=>
        {
            Assert(req.Method==HttpMethod.Get,"lookup sent a mutation");
            Assert(req.Headers.GetValues("Cookie").Single()=="userhash=original-cookie-secret","lookup used the current browsing identity");
            gets++;
            var path=req.RequestUri!.AbsolutePath;
            if(path=="/Api/getLastPost") Assert(req.RequestUri.Host=="www.nmbxd1.com","last-post requested from the wrong server");
            var data=path=="/Api/getLastPost"?last:path.EndsWith("thread")?original:forum;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content=new StringContent(
                data is string text?text:JsonSerializer.Serialize(data)) });
        })));
        api.SetCookie("other-browsing-secret");
        var id=await api.FindSentThreadAsync(sent,"original-cookie-secret");
        Assert(gets<=4,"unbounded lookup");
        return id;
    }
    private static void Assert(bool ok,string why) { if(!ok) throw new Exception(why); }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>send(request,ct);
    }
}

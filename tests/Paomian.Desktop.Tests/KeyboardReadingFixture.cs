using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Paomian.Core;

namespace Paomian.Desktop.Tests;

internal sealed class KeyboardReadingFixture : HttpMessageHandler
{
    internal const long Thread = 710001, ImageReply = 710101, LongReply = 710102;
    internal int ListRequests { get; private set; }
    internal int ThreadRequests { get; private set; }
    internal int ReferenceRequests { get; private set; }
    internal List<int> ReplyPages { get; } = [];

    private static readonly string Links = string.Join("　", Enumerable.Range(720101, 16).Select(id => $"&gt;&gt;No.{id}"));
    private static readonly string LongText = string.Join("<br><br>", Enumerable.Range(1, 48).Select(i =>
        $"第 {i:D2} 段 · 河岸步道<br>从石阶向下走，柏油路在左边，河水在右边。沿栏杆慢慢走，桥下有一个停留的平台。这里的路面坡度很缓，转角之后可以再次看到旧桥。" +
        (i == 18 ? $"<br>{Links}" : "")));

    internal static void SeedImages(DiskCache cache)
    {
        var pixels = new byte[240 * 120 * 4];
        for (var y = 0; y < 120; y++)
        for (var x = 0; x < 240; x++)
        {
            var at = (y * 240 + x) * 4;
            var water = y > 60 + 20 * Math.Sin(x / 48d);
            pixels[at] = water ? (byte)190 : (byte)124;
            pixels[at + 1] = water ? (byte)157 : (byte)168;
            pixels[at + 2] = water ? (byte)109 : (byte)152;
            pixels[at + 3] = 255;
        }
        var bitmap = BitmapSource.Create(240, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 240 * 4);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new System.IO.MemoryStream();
        png.Save(stream);
        foreach (var part in new[] { "thumb", "image" })
        {
            var url = $"https://image.nmb.best/{part}/keyboard/walk.png";
            cache.Write("images/" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))) + ".bin", stream.ToArray());
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Method != HttpMethod.Get) throw new InvalidOperationException("键盘验收仅支持模拟读取。");
        var uri = request.RequestUri!;
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        _ = long.TryParse(query["id"], NumberStyles.None, CultureInfo.InvariantCulture, out var id);
        _ = int.TryParse(query["page"], NumberStyles.None, CultureInfo.InvariantCulture, out var page);
        var json = uri.AbsolutePath.Split('/')[^1] switch
        {
            "getForumList" => """[{"name":"兴趣交流","forums":[{"id":4,"name":"日常","msg":"这里有离线验收用的版规。"},{"id":5,"name":"摄影"}]},{"name":"阅读与创作","forums":[{"id":6,"name":"读书"}]}]""",
            "getTimelineList" => """[{"id":1,"name":"综合线","max_page":4}]""",
            "showf" or "timeline" => ThreadList(page, id is >= 4 and <= 6 ? (int)id : 4),
            "thread" => ReadThread(id, page),
            "ref" => Reference(id),
            _ => "[]"
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private string ThreadList(int page, int forum)
    {
        ListRequests++;
        return JsonSerializer.Serialize(Enumerable.Range(0, 20).Select(i => new
        {
            id = Thread + (page - 1) * 20 + i, fid = forum, ReplyCount = i == 0 ? 40 : 1,
            title = i == 0 ? "沿河步道随手记" : $"河岸见闻 {i:00}",
            content = "从广场走到旧桥，记录几处岔路。" + (i % 3 == 1 ? "<br>路边的石阶与水位，回程再补几张记录。<br>河岸的长椅边有两块路牌。" : ""),
            now = "2026-11-15 10:30:15", user_hash = "DemoRow"
        }));
    }

    private string ReadThread(long id, int page)
    {
        ThreadRequests++;
        ReplyPages.Add(page);
        var replies = new List<object>();
        if (id == Thread && page == 1)
        {
            replies.Add(Reply(ImageReply, $"整理了几条岔路和参考图。<br>{Links}<br><a href='https://www.nmbxd1.com/t/710002'>沿河路线图</a>　[h]对岸入口与 &gt;&gt;No.729999[/h]", "keyboard/walk"));
            replies.Add(Reply(LongReply, LongText));
        }
        for (var i = replies.Count; i < (page == 3 ? 2 : 19); i++)
            replies.Add(Reply(711000 + page * 100 + i, $"第 {page} 页的步道记录　{i + 1:00}。从桥脚走回总站，经过一排长椅。"));
        return JsonSerializer.Serialize(new { id, fid = 4, ReplyCount = id == Thread ? 40 : 1, title = "沿河步道随手记",
            content = "从广场启程，沿着河堤慢慢走。这个串里有 48 段长回应、引用、剧透和图片。<br>&gt;&gt;No.720101", Replies = replies });
    }

    private static object Reply(long id, string content, string image = "") => new
    { id, fid = 4, content, img = image, ext = image.Length == 0 ? "" : ".png", now = "2026-11-15 11:28:01", user_hash = "DemoPost" };

    private string Reference(long id)
    {
        ReferenceRequests++;
        return JsonSerializer.Serialize(Reply(id, id switch
        {
            720101 => "木栈道入口旁可以看到石阶。<br>&gt;&gt;No.720201",
            720201 => "过桥后沿指示牌继续走。<br>&gt;&gt;No.720301",
            _ => "旧站牌在蓝色围栏的另一侧。"
        }));
    }
}

using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Tide.Core;
using Tide.Desktop;

namespace Tide.Desktop.Tests;

// Production controls and view models with an isolated, read-only HTTP fixture. No personal data or posting.
internal static class RefinedReaderChecks
{
    private const string Welcome = "欢迎来到X岛，一个半全新的二次元泛ACG讨论区<br>网页端地址：<a href='https://www.nmbxd.com'>www.nmbxd.com</a><br><br><font color='green'>=========== 注册已开启 ==========<br>======= 周六日不定时开放注册 ======</font><br><br>最<font color='red'>新</font>官方　微博：<a href='https://weibo.com/u/7743400324'>@X岛极速版</a>　微信公众号：X岛揭示板<br><br>如您第一次使用本社区，建议您点击此文查看回复<br>否则我们不能保证其他用户对您的友善态度<br><br>客户端下载地址：<a href='https://app.nmbxd.com'>https://app.nmbxd.com</a><br>现行APP客户端均为第三方热心岛民为爱发电制作，我们只保证您在使用网页版x岛时获取的信息流程与方式";
    private static readonly object[] Threads = [
        Wire(52752005,"Admin",Welcome,7,true,true,"qa/welcome"),
        Wire(69731001,"lwfzrvi","(ﾟ∀。)X岛观鸟攻略串有没有搞头？<br><br>观鸟淡季+被台风关得鸟瘾大发，本地几个鸟点来回刷刷不出new，划拉小红书苦于地点模糊/交通不明总怕白费力气<br><br>希望肥哥多多分享自己的观鸟宝地(ゝ∀･)",31),
        Wire(69731002,"fRZJgpO","今天，母亲下葬了；<br><br>也许是昨天，<br><br>觅蝉没赶上。",104),
        Wire(69731003,"ADB4eH","记录一下在学校见到的猫猫<br>开学拍的第一张猫|∀` )",29),
        Wire(69731004,"q1k9WAw","你知道的，我一直热衷给大家拍照<br>摄影串来(ﾟ∀。)",51)
    ];
    private static object Wire(long id,string user,string content,int replies=0,bool admin=false,bool sage=false,string image="") => new {
        id=id.ToString(),fid="4",user_hash=user,admin=admin?"1":"0",sage=sage?"1":"0",ReplyCount=replies.ToString(),
        title="无标题",name="无名氏",content,now=admin?"2022-10-16 23:14:27":"2026-07-13 15:19:26",img=image,ext=image.Length>0?".jpg":""
    };
    internal static XIslandApi CreateApi() => new(new HttpClient(new Handler()));
    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method!=HttpMethod.Get) throw new InvalidOperationException("UI validation must never submit posts.");
            var thread=JsonSerializer.SerializeToNode(Threads[0])!;
            thread["Replies"]=JsonSerializer.SerializeToNode(new[] {
                Wire(52752007,"Admin","注册入口于网页版左侧侧边栏底部【用户系统】<br><br>在上手前您可能需要浏览总版规<br>&gt;&gt;No.50000001<br>以及对此所进行解读的《X岛使用指南》<br>&gt;&gt;No.50577215",admin:true),
                Wire(69731021,"Rvciely","&gt;&gt;No.52752005<br>(ﾟ∀ﾟ) 留个脚印，慢慢看。"),
                Wire(69731022,"lwfzrvi","谢谢肥哥！有新的内容会接着更新。|∀` )") });
            var path=request.RequestUri!.AbsolutePath;
            var json=path.Contains("thread",StringComparison.OrdinalIgnoreCase)?thread.ToJsonString():JsonSerializer.Serialize(Threads);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
    internal static async Task RunAsync(MainWindow window,string output,Action<Window,string> capture,bool inspectOnly=false)
    {
        var vm=window.Model;
        // Let the test window render a 1920 DIP surface even on this 125% desktop.
        // This changes only the test HWND's tracking limit, never the user's monitor settings.
        var source=HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source.AddHook(UnboundedTestSurface);
        SetDpi(window,1,1280,800);
        await Layout(window);
        vm.State.FontSize=16;vm.AppearanceChanged();
        vm.State.Forums=[new(1,"综合线","时间线",IsTimeline:true),new(2,"日常线","时间线",IsTimeline:true),new(4,"综合版1","综合"),new(5,"综合版2","综合"),new(20,"技术宅(代码)","综合"),new(30,"动画","亚文化"),new(17,"跑团","游戏")];
        vm.ForumGroups.Clear();foreach(var g in vm.State.Forums.GroupBy(f=>f.Group)) vm.ForumGroups.Add(new(g.Key,g.ToList()));
        if(vm.State.FavoriteForums.Count==0){vm.ToggleForumFavorite(vm.State.Forums[0]);vm.ToggleForumFavorite(vm.State.Forums[4]);}
        var image=File.ReadAllBytes(Path.GetFullPath("tests/Tide.Desktop.Tests/Fixtures/welcome.jpg"));
        foreach(var url in new[]{"https://image.nmb.best/image/qa/welcome.jpg","https://image.nmb.best/thumb/qa/welcome.jpg"})
            vm.Store.Cache.Write("images/"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))+".bin",image);
        await vm.SelectForumAsync(vm.State.Forums[0]); await vm.OpenThreadAsync(52752005);
        Require(vm.Original?.Id==52752005,"thread fixture not loaded");
        ((ListBox)window.FindName("ThreadList")).SelectedIndex=0;
        await Task.Delay(1400);
        if(inspectOnly) { SetDpi(window,1.25,1280,800);window.Left=20;window.Top=20;return; }
        var composite = new Dictionary<string,object>();
        foreach(var theme in new[]{"light","dark"})
        {
            App.ApplyTheme(theme);vm.State.Theme=theme;
            foreach(var size in new[]{new Size(1280,800),new Size(1920,1080)})
            {
                window.Width=size.Width;window.Height=size.Height;await Layout(window);
                Require(Math.Abs(window.ActualWidth-size.Width)<2 && Math.Abs(window.ActualHeight-size.Height)<2,$"screenshot surface was constrained: {window.ActualWidth} x {window.ActualHeight}, DPI {VisualTreeHelper.GetDpi(window)}; expected {size}");
                Render(window,Path.Combine(output,$"main-{theme}-{size.Width}x{size.Height}.png"));
                var reader=(ListBox)window.FindName("ReaderList");
                var rows=All<Border>(reader).Where(b=>b.Name=="PostRow").ToArray();
                var heading=(TextBlock)window.FindName("ReaderForum");
                Require(rows.Length>0,"no visible reader rows");
                var start=heading.TranslatePoint(new Point(),window).X;
                foreach(var row in rows) Require(Math.Abs(row.TranslatePoint(new Point(),window).X-start)<3,"reader/header alignment drift");
                foreach(var preview in All<PostPreview>(window))Require(preview.MaxHeight<=Math.Ceiling(preview.LineHeight*7)+1,"preview exceeds seven lines");
                composite[$"{theme}-{size}"]=new{ReadingWidth=rows[0].ActualWidth,Alignment=start,ActualDpi=VisualTreeHelper.GetDpi(window).PixelsPerInchX};
            }
            window.Width=1280;window.Height=800;await Layout(window);
            foreach(var dialog in new DialogWindow[]{new ComposerWindow(vm,0),new ComposerWindow(vm,52752005),new SettingsWindow(vm),new CookieWindow(vm)})
            {
                dialog.Owner=window;dialog.ShowInTaskbar=false;dialog.Show();await Layout(dialog);
                CheckCloseGlyph(dialog);
                var name=dialog switch{ComposerWindow=>dialog.Title=="发串"?"compose":"reply",SettingsWindow=>"settings",_=>"cookies-empty"};
                Render(dialog,Path.Combine(output,$"{name}-{theme}.png"));
                Require(System.Windows.Documents.AdornerLayer.GetAdornerLayer((UIElement)window.Content)?.GetAdorners((UIElement)window.Content)?.Length > 0,"dialog owner scrim missing");
                if(dialog is ComposerWindow && name=="compose") Render(window,Path.Combine(output,$"modal-backdrop-{theme}.png"));
                if(dialog is ComposerWindow)
                {
                    var editor=All<EmojiEditor>(dialog).Single();editor.Text="正在编辑的正文　连续  空格\n(ﾟ∀ﾟ)\n第二行";
                    var before=editor.Text;App.ApplyTheme(theme=="light"?"dark":"light");await Layout(dialog);
                    Require(editor.Text==before,"theme switch changed draft");App.ApplyTheme(theme);
                    All<Expander>(dialog).Single().IsExpanded=true;await Layout(dialog);
                    Require(All<Wpf.Ui.Controls.TextBox>(dialog).Count()>=2,"optional title/name fields absent");
                    editor.Focus();Require(editor.IsKeyboardFocusWithin,"editor cannot receive keyboard focus");
                    Render(dialog,Path.Combine(output,$"{name}-expanded-{theme}.png"));
                    var primary=All<Button>(dialog).Last(b=>b.Content as string=="预览并发送");AssertVisibleInWindow(primary,dialog);
                }
                if(dialog is SettingsWindow)
                {
                    var slider=All<Slider>(dialog).First();
                    var oldSize=vm.State.FontSize;
                    Slider.IncreaseLarge.Execute(null,slider);await Layout(dialog);
                    Require(vm.State.FontSize>oldSize,"slider command did not update reading size");
                    var track=All<System.Windows.Controls.Primitives.Track>(slider).Single();
                    Require(track.DecreaseRepeatButton.ActualWidth>20 && track.DecreaseRepeatButton.Opacity==1,"slider selection track is not visible");
                    slider.Value=oldSize;await Layout(dialog);
                    var navigation=All<ListBox>(dialog).Single(n=>System.Windows.Automation.AutomationProperties.GetName(n)=="设置分类");
                    foreach(var name2 in new[]{"阅读","外观","网络","缓存","数据与记录","关于"})Require(navigation.Items.Contains(name2),"missing settings group "+name2);
                    navigation.SelectedItem="关于";await Layout(dialog);Render(dialog,Path.Combine(output,$"settings-about-{theme}.png"));
                }
                dialog.Close();await Layout(window);
            }
            foreach(var extra in new ChildWindow[] {
                new SubscriptionWindow(vm),new BlockedThreadsWindow(vm),new DraftWindow(vm,(_,_)=>{}),
                new TextWindow("引用预览","&gt;&gt;No.52752005<br>(ﾟ∀ﾟ) 留个脚印，慢慢看。"),
                new SiteNoticeWindow(new SiteNotice("reader-fixture","这是一条界面验证通知。\n正文可滚动和复制，确认操作始终可见。")),
                new MyPostsWindow(vm),new ThreadLinkPreviewWindow(vm,IslandLink.Parse("https://www.nmbxd.com/t/52752005")!),
                new ForumLinkPromptWindow(vm,IslandLink.Parse("https://www.nmbxd.com/f/综合版1")!,_=>{}) })
            {
                extra.Owner=window;extra.ShowInTaskbar=false;extra.Show();await Layout(extra);
                CheckCloseGlyph(extra);
                Require(All<Button>(extra).Any(b=>AutomationProperties.GetName(b)=="关闭"),"custom dialog close missing");
                Render(extra,Path.Combine(output,$"{extra.GetType().Name}-{theme}.png"));extra.Close();
            }
        }
        vm.State.Cookies=Enumerable.Range(1,20).Select(i=>new CookieIdentity("fixture"+i,"本机测试饼干 "+i,SecretProtection.Protect("fixture-cookie-"+i))).ToList();
        vm.State.ActiveCookieId="fixture1";
        foreach(var theme in new[]{"light","dark"})
        {
            App.ApplyTheme(theme);
            var dialog=new CookieWindow(vm){Owner=window,ShowInTaskbar=false};dialog.Show();await Layout(dialog);
            CheckCloseGlyph(dialog);
            var list=All<ListBox>(dialog).Single();Require(list.Items.Count==20,"cookie entries lost");list.SelectedIndex=19;list.ScrollIntoView(list.SelectedItem);await Layout(dialog);
            Render(dialog,Path.Combine(output,$"cookies-populated-{theme}.png"));dialog.Close();
        }
        // Layout bounds are independently computed for all required monitor scale factors.
        foreach(var scale in new[]{1d,1.25,1.5})
        {
            var bounds=WindowPlacement.Calculate(new Rect(0,0,1280,760),scale,new Size(640,820),new Size(420,240));
            Require(bounds.Bounds.Width*scale<=1280 && bounds.Bounds.Height*scale<=760,"dialog exceeds scaled work area");
            composite[$"fit-{scale}"]=bounds;
        }
        // Exercise WPF's per-monitor DPI message on isolated HWNDs, without altering display configuration.
        foreach(var scale in new[]{1d,1.25,1.5})
        {
            foreach(var theme in new[]{"light","dark"})
            {
                App.ApplyTheme(theme);
                SetDpi(window,scale,1280/scale,800/scale);await Layout(window);
                Require(Math.Abs(VisualTreeHelper.GetDpi(window).DpiScaleX-scale)<.01,"WPF did not apply the requested DPI");
                capture(window,Path.Combine(output,$"dpi-{scale*100:0}-{theme}-main.png"));
                foreach(var dialog in new DialogWindow[]{new ComposerWindow(vm,52752005),new SettingsWindow(vm),new CookieWindow(vm)})
                {
                    dialog.Owner=window;dialog.ShowInTaskbar=false;dialog.Show();
                    dialog.MinHeight=Math.Min(dialog.MinHeight,760/scale);
                    SetDpi(dialog,scale,Math.Min(640,1240/scale),Math.Min(740,740/scale));await Layout(dialog);
                    Require(Math.Abs(VisualTreeHelper.GetDpi(dialog).DpiScaleX-scale)<.01,"child DPI did not update");
                    CheckCloseGlyph(dialog);
                    foreach(var button in All<Button>(dialog).Where(b=>b.IsVisible && b.Content as string is "完成" or "预览并发送" or "导入并使用"))AssertVisibleInWindow(button,dialog);
                    if(dialog is ComposerWindow){All<Expander>(dialog).Single().IsExpanded=true;await Layout(dialog);}
                    if(dialog is SettingsWindow){All<ListBox>(dialog).Single(n=>System.Windows.Automation.AutomationProperties.GetName(n)=="设置分类").SelectedItem="关于";await Layout(dialog);}
                    capture(dialog,Path.Combine(output,$"dpi-{scale*100:0}-{theme}-{dialog.GetType().Name}.png"));
                    dialog.Close();
                }
            }
            composite[$"dpi-{scale}"]="WM_DPICHANGED applied; main and composer/settings/cookies rendered, fixed actions inside window";
        }
        SetDpi(window,1.25,1280,800);
        window.Left=20;window.Top=20;App.ApplyTheme("light");vm.State.Theme="light";vm.AppearanceChanged();
        File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(composite,new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("PASS Refined Reader: production main/dialog rendering, both themes, content alignment, seven-line previews, full settings, draft preservation, cookie list and DPI bounds.");
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMax { public PointI Reserved,MaxSize,MaxPosition,MinTrack,MaxTrack; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left,Top,Right,Bottom; }
    private static IntPtr UnboundedTestSurface(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled)
    {
        if(msg==0x24){var info=Marshal.PtrToStructure<MinMax>(lParam);info.MaxTrack=new(){X=32000,Y=32000};Marshal.StructureToPtr(info,lParam,false);handled=true;}
        return IntPtr.Zero;
    }
    private static void SetDpi(Window window,double scale,double width,double height)
    {
        var rect=new RectI{Left=0,Top=0,Right=(int)(width*scale),Bottom=(int)(height*scale)};
        var memory=Marshal.AllocHGlobal(Marshal.SizeOf<RectI>());
        try{Marshal.StructureToPtr(rect,memory,false);var dpi=(int)(96*scale);SendMessage(new WindowInteropHelper(window).Handle,0x02E0,new IntPtr((dpi<<16)|dpi),memory);window.Width=width;window.Height=height;}
        finally{Marshal.FreeHGlobal(memory);}
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam);
    private static void CheckCloseGlyph(Window window)
    {
        var close=All<Button>(window).Single(b=>AutomationProperties.GetName(b)=="关闭");
        var glyph=(System.Windows.Shapes.Path)close.Content;
        var presenter=All<ContentPresenter>(close).First();
        Require(presenter.ActualWidth>=glyph.DesiredSize.Width && presenter.ActualHeight>=glyph.DesiredSize.Height,"close glyph is squeezed by button padding");
        var bounds=glyph.TransformToAncestor(close).TransformBounds(new Rect(glyph.RenderSize));
        Require(bounds.Left>=0 && bounds.Top>=0 && bounds.Right<=close.ActualWidth && bounds.Bottom<=close.ActualHeight,"close glyph is clipped");
        AssertVisibleInWindow(close,window);
    }
    private static void AssertVisibleInWindow(FrameworkElement child,Window window){var rect=child.TransformToAncestor(window).TransformBounds(new Rect(child.RenderSize));Require(rect.Bottom<=window.ActualHeight && rect.Top>=0,"dialog action clipped");}
    internal static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject=>UiRefinementChecks.Descendants<T>(root);
    private static async Task Layout(Window w){await w.Dispatcher.InvokeAsync(w.UpdateLayout,DispatcherPriority.ApplicationIdle);await Task.Delay(180);w.UpdateLayout();}
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    private static void Render(Window window,string file)
    {
        // Rasterize the actual WPF visual tree at 96 dpi to make the four DIP baselines directly comparable.
        window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(file);png.Save(stream);
    }
}

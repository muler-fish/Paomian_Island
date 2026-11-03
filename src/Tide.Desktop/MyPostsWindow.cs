using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>本机已发送记录，与可丢弃缓存和草稿区分；主题和回复各自保留发送时间顺序。</summary>
public sealed class MyPostsWindow : ChildWindow
{
    public MyPostsWindow(MainViewModel model) : base("我的发言")
    {
        Width=850; Height=700; MinWidth=480; MinHeight=400;
        var viewModel = new SentPostsViewModel(model, this);
        var view = new MyPostsView { DataContext=viewModel };
        SetClientContent(view); UseReadingScroll(view);
        model.SentPostsChanged += viewModel.Reload;
        Closed += (_,_) => model.SentPostsChanged -= viewModel.Reload;
        viewModel.Reload();
    }
}

public sealed class SentPostItem(SentPost record, bool lookingUp = false)
{
    public SentPost Record { get; } = record;
    public string Title => Record.Title;
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);
    public string Author => Record.Name;
    public bool HasAuthor => !string.IsNullOrWhiteSpace(Author);
    public string CookieName => Record.CookieName;
    public string Time => PostTime.Format(Record.SentAt.ToString("yyyy-MM-dd HH:mm:ss"));
    public string Destination => Record.IsReply ? $"回复 No.{Record.ThreadId}" :
        Record.ThreadId is > 0 ? $"{Record.ForumName} · No.{Record.ThreadId}" : $"{Record.ForumName} · {(lookingUp ? "正在获取串号…" : "尚未获取串号")}";
    public bool CanOpen => Record.ThreadId is > 0;
    public bool CanFind => !Record.IsReply && !CanOpen;
    public bool LookupEnabled => !lookingUp;
    public bool HasImage => Record.HasImage;
    public string Html => WebUtility.HtmlEncode(Record.Content).Replace("\r\n", "\n").Replace("\n", "<br>");
}

public sealed class SentPostsViewModel : Observable
{
    private readonly MainViewModel _main;
    public ObservableCollection<SentPostItem> Threads { get; } = [];
    public ObservableCollection<SentPostItem> Replies { get; } = [];
    private string _error = "";
    public string Error { get => _error; private set => Set(ref _error,value); }
    public bool HasError => Error.Length > 0;
    public double FontSize => _main.ReadingFontSize;
    public bool EmptyThreads => Threads.Count == 0 && !HasError;
    public bool EmptyReplies => Replies.Count == 0 && !HasError;
    public ActionCommand ViewCommand { get; }
    public TaskCommand OpenCommand { get; }
    public ActionCommand ReloadCommand { get; }
    public TaskCommand FindCommand { get; }
    public SentPostsViewModel(MainViewModel main, MyPostsWindow window)
    {
        _main=main;
        ViewCommand = new(p =>
        {
            if (p is not SentPostItem item) return;
            var text = item.Record;
            var html = (text.Title.Length>0 ? $"<b>{WebUtility.HtmlEncode(text.Title)}</b><br>" : "") +
                (text.Name.Length>0 ? WebUtility.HtmlEncode(text.Name)+"<br>" : "") + item.Html;
            new TextWindow("我的发言",html) { Owner=window }.Show();
        });
        OpenCommand = new(async p =>
        {
            if (p is not SentPostItem { Record.ThreadId: > 0 } item) return;
            await main.OpenThreadAsync(item.Record.ThreadId!.Value);
            if (main.Original?.Id == item.Record.ThreadId && main.Error.Length==0) window.Close();
            else { Error = main.Error.Length>0 ? main.Error : "原串暂时无法打开。"; Changed(nameof(HasError)); }
        }, ex => { Error=ex.Message; Changed(nameof(HasError)); });
        ReloadCommand = new(_ => Reload());
        FindCommand = new(async p =>
        {
            if (p is not SentPostItem item || !item.CanFind) return;
            Error = await main.ResolveSentThreadAsync(item.Record);
            Changed(nameof(HasError));
        }, _ => { Error="暂时无法查找串号，请稍后重试。"; Changed(nameof(HasError)); });
    }
    public void Reload()
    {
        try
        {
            var records = _main.Store.ReadSentPosts();
            Threads.Clear(); Replies.Clear();
            foreach (var post in records) (post.IsReply ? Replies : Threads).Add(new(post, _main.IsLookingUpSentPost(post.Id)));
            Error="";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Error=ex.Message; }
        foreach(var name in new[]{nameof(HasError),nameof(EmptyThreads),nameof(EmptyReplies)}) Changed(name);
    }
}

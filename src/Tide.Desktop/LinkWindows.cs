using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>正文链接先预览串首，只有用户明确打开时才改变主阅读区。</summary>
internal sealed class ThreadLinkPreviewWindow : DialogWindow
{
    private readonly MainViewModel _model;
    private readonly IslandLink _link;
    private readonly Button _open, _retry;
    private CancellationTokenSource? _request;
    private bool _closed;
    private int _generation;

    public ThreadLinkPreviewWindow(MainViewModel model, IslandLink link) : base($"串预览 · No.{link.Id}")
    {
        _model = model; _link = link;
        _retry = Button("重试", () => _ = LoadAsync(true));
        _open = Button(link.Page > 1 ? $"打开第 {link.Page} 页" : "打开串", async () =>
        {
            Close(); await model.OpenThreadAsync(link.Id, link.Page, targetPostId: link.ReplyId);
        }, true);
        _open.IsEnabled = false;
        AddAction("关闭", Close); Actions.Children.Add(_open);
        Loaded += async (_, _) => await LoadAsync(false);
        Closed += (_, _) => { _closed = true; ++_generation; _request?.Cancel(); _request?.Dispose(); };
    }

    private async Task LoadAsync(bool refresh)
    {
        _request?.Cancel(); _request?.Dispose(); _request = new();
        var ct = _request.Token; var generation = ++_generation;
        _open.IsEnabled = false; Body.Children.Clear();
        var progress = new ProgressBar { Height = 3, IsIndeterminate = SystemParameters.ClientAreaAnimation, Value = 50 };
        Body.Children.Add(progress); Body.Children.Add(Hint("正在读取串预览…"));
        try
        {
            var post = await _model.ReadLinkPreviewAsync(_link.Id, refresh, ct);
            if (_closed || ct.IsCancellationRequested || generation != _generation) return;
            Body.Children.Clear();
            var title = PostCard.Optional(post.Title, "无标题", _model.State.HideSpoilers);
            if (title.Length > 0) Body.Children.Add(new TextBlock { Text = title, FontSize = _model.ReadingFontSize + 2,
                FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) });
            Body.Children.Add(new PostIdentity { Post = post, IsPo = true });
            Body.Children.Add(Hint($"{PostTime.Format(post.Time, true)}  ·  {post.ReplyCount} 回复"));
            var name = PostCard.Optional(post.Name, "无名氏", _model.State.HideSpoilers);
            if (name.Length > 0) Body.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 0) });
            Body.Children.Add(new PostBody { Html = post.Content, FontSize = _model.ReadingFontSize, Margin = new(0, 12, 0, 0) });
            if (post.HasImage) Body.Children.Add(new PostImage { Post = post, ShowImages = _model.ReaderImagesAuto, Margin = new(0, 16, 0, 0) });
            _open.IsEnabled = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_closed || generation != _generation) return;
            Body.Children.Clear(); Feedback.Text = ex is IslandException ? ex.Message : "预览加载失败";
            Body.Children.Add(Feedback); Body.Children.Add(_retry);
        }
    }
}

/// <summary>板块链接先说明目标；确认前不修改主列表，未知入口也需明确选择网页打开。</summary>
internal sealed class ForumLinkPromptWindow : ChildWindow
{
    private readonly CancellationTokenSource _request = new();
    private readonly TextBlock _message = new() { FontSize = 20, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    private readonly TextBlock _detail = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
    private readonly ProgressBar _progress = new() { Height = 3, Value = 50, Margin = new(0, 0, 0, 16) };
    private readonly Button _go;
    private Forum? _forum;
    private bool _closed;

    public ForumLinkPromptWindow(MainViewModel model, IslandLink link, Action<string> openBrowser) : base("跳转板块")
    {
        Width = 480; Height = 260; MinWidth = 360; MinHeight = 230;
        var grid = new Grid { Margin = new(24) };
        grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var body = new StackPanel(); body.Children.Add(_message); body.Children.Add(_detail); body.Children.Add(_progress);
        grid.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        _detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _progress.IsIndeterminate = SystemParameters.ClientAreaAnimation;
        var name = link.FindForum(model.State.Forums)?.Name ?? (link.Name.Length > 0 ? link.Name : $"{(link.Kind == IslandLinkKind.Timeline ? "时间线" : "板块")} No.{link.Id}");
        _message.Text = $"即将跳转到‘{name}’";
        _detail.Text = "正在确认板块…";
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Wpf.Ui.Controls.Button { Content = "取消", IsCancel = true, Margin = new(0, 0, 8, 0) };
        cancel.Click += (_, _) => Close(); buttons.Children.Add(cancel);
        _go = new Wpf.Ui.Controls.Button { Content = "跳转", IsEnabled = false };
        _go.SetResourceReference(StyleProperty, "Primary");
        _go.Click += async (_, _) =>
        {
            Close();
            if (_forum is { } forum) await model.SelectForumAsync(forum, link.Page);
            else openBrowser(link.Url.AbsoluteUri);
        };
        buttons.Children.Add(_go); Grid.SetRow(buttons, 1); grid.Children.Add(buttons); SetClientContent(grid);
        AutomationProperties.SetLiveSetting(_detail, AutomationLiveSetting.Polite);
        Closed += (_, _) => { _closed = true; _request.Cancel(); _request.Dispose(); };
        Loaded += async (_, _) =>
        {
            cancel.Focus(); var ct = _request.Token;
            try { _forum = await model.ResolveLinkForumAsync(link, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception) { /* 目录不可用时仍保留明确的网页打开选择。 */ }
            if (_closed || ct.IsCancellationRequested) return;
            _progress.Visibility = Visibility.Collapsed;
            if (_forum is { } forum)
            {
                _message.Text = $"即将跳转到‘{forum.Name}’";
                _detail.Text = link.Page > 1 ? $"第 {link.Page} 页" : "";
                _detail.Visibility = link.Page > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            else { _detail.Text = "无法在客户端打开此板块，将使用浏览器。"; _go.Content = "浏览器打开"; }
            _go.IsEnabled = true;
        };
    }
}

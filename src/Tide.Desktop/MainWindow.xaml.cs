using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>处理必须接触窗口或控件的操作：焦点、滚动、弹窗及外部链接；数据逻辑交给 MainViewModel。</summary>
public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainViewModel Model { get; }
    private ReadingAnchor? _readerAnchor;
    private readonly ReadingProgressTracker _readingProgress;
    private readonly MainLayoutController _layout;
    private readonly Action<string> _openBrowser;
    private bool _closed, _syncingThreadSelection, _threadSelectionQueued;
    public MainWindow(MainViewModel model) : this(model, url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })) { }
    internal MainWindow(MainViewModel model, Action<string> openBrowser)
    {
        // DataContext 是 MainWindow.xaml 里普通 Binding 的默认数据来源。
        InitializeComponent(); Model = model; DataContext = model; _openBrowser = openBrowser;
        _readingProgress = new(ReaderList, model);
        model.ReaderLeaving += _readingProgress.Flush;
        Closing += (_, _) => _readingProgress.Flush();
        _layout = new(this);
        model.ReaderReset += ResetReader;
        model.ReaderPrepending += BeforePrepend;
        model.ReaderPrepended += AfterPrepend;
        model.ListReset += ResetList;
        model.FeedbackRaised += FeedbackReceived;
        model.PropertyChanged += FeedbackStateChanged;
        Loaded += (_, _) => { if (Model.HasError) ShowFeedback(Model.Error, FeedbackArea.General, true); };
        Deactivated += (_, _) => FeedbackLayer.Clear();
        PostBody.ReferenceClicked += Reference;
        PostBody.UrlClicked += ContentLinkClicked;
        PostImage.ImageClicked += OpenImage;
        model.PropertyChanged += ReadingStateChanged;
        ((INotifyCollectionChanged)ThreadList.Items).CollectionChanged += ThreadItemsChanged;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control) { JumpBox.Focus(); JumpBox.SelectAll(); e.Handled = true; }
            else if (ReadingHistoryStep(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers, Keyboard.FocusedElement) is not 0 and var step)
            {
                // 按住不放时不连续后退；处理掉按键，避免焦点控件再响应同一组合键。
                if (!e.IsRepeat) NavigateReadingHistory(step);
                e.Handled = true;
            }
        };
        // 鼠标侧键与浏览器一致：XButton1 后退，XButton2 前进。
        PreviewMouseDown += (_, e) =>
        {
            if (e.ChangedButton is not (MouseButton.XButton1 or MouseButton.XButton2)) return;
            NavigateReadingHistory(e.ChangedButton == MouseButton.XButton1 ? -1 : 1); e.Handled = true;
        };
        Closed += (_, _) =>
        {
            _closed = true;
            // 正文和图片控件使用静态事件，关闭窗口时必须退订，避免旧窗口被事件引用保留。
            model.ReaderReset -= ResetReader; model.ListReset -= ResetList; PostBody.ReferenceClicked -= Reference; PostBody.UrlClicked -= ContentLinkClicked; PostImage.ImageClicked -= OpenImage;
            model.PropertyChanged -= ReadingStateChanged; ((INotifyCollectionChanged)ThreadList.Items).CollectionChanged -= ThreadItemsChanged;
            model.ReaderPrepending -= BeforePrepend; model.ReaderPrepended -= AfterPrepend;
            model.ReaderLeaving -= _readingProgress.Flush; _readingProgress.Dispose(); _layout.Dispose();
            model.FeedbackRaised -= FeedbackReceived; model.PropertyChanged -= FeedbackStateChanged; FeedbackLayer.Clear();
            model.Save(); model.Dispose();
        };
    }
    private void ResetReader()
    {
        InertialScroll.Stop(ReaderList);
        _readerAnchor = null;
        IncrementalScroll.Reset(ReaderList, Model.ReaderStartsAfterFirstPage || Model.ReaderRestorePosition is not null);
        if (Model.ReaderRestorePosition is null)
        {
            if (Model.ReadingPosts.Count > 0) ReaderList.ScrollIntoView(Model.ReadingPosts[0]);
            ReadingAnchor.Children<ScrollViewer>(ReaderList).FirstOrDefault()?.ScrollToVerticalOffset(0);
        }
        _readingProgress.Reset(Model.ReaderRestorePosition);
    }
    private void BeforePrepend() { InertialScroll.Stop(ReaderList); _readingProgress.Pause(); _readerAnchor = ReadingAnchor.Capture(ReaderList); }
    private void AfterPrepend()
    {
        IncrementalScroll.PrependCompleted(ReaderList);
        _readerAnchor?.Restore(ReaderList); _readerAnchor = null;
        _readingProgress.Resume();
    }
    private void ResetList()
    {
        InertialScroll.Stop(ThreadList);
        if (Model.VisiblePostCount > 0) ThreadList.ScrollIntoView(Model.VisiblePosts.GetItemAt(0));
    }
    private async void Forum_Click(object sender, RoutedEventArgs e) { if (((Button)sender).Tag is Forum f) await Model.SelectForumAsync(f); }
    private async void HomeTimeline_Click(object sender, RoutedEventArgs e) => await Model.SelectForumAsync(Model.State.Forums.Find(f => f.IsTimeline && f.Id == 1) ?? new(1, "综合线", "时间线", "", true, 20));
    private async void Thread_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 选中项只是用户的打开操作；按阅读状态回写选中项时不能再次触发导航。
        if (_syncingThreadSelection || ThreadList.SelectedItem is not PostCard post) return;
        await Model.OpenListThreadAsync(post.Post.Id);
    }
    private void ReadingStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ReadingThreadId)) SyncThreadSelection();
    }
    private void ThreadItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 刷新、续页或取消屏蔽后，当前阅读串可能重新出现在列表里；只在需要时排一次同步。
        if (_threadSelectionQueued || e.Action == NotifyCollectionChangedAction.Add && (ThreadList.SelectedItem is not null ||
            e.NewItems?.OfType<PostCard>().Any(c => c.Post.Id == Model.ReadingThreadId) != true)) return;
        // 列表正在处理集合变化，等它更新完容器后再改选中项。
        _threadSelectionQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () => { _threadSelectionQueued = false; if (!_closed) SyncThreadSelection(); });
    }
    /// <summary>让串列表的选中项对应右侧当前阅读的串；该串不在列表（或被屏蔽）时不选中任何项。</summary>
    private void SyncThreadSelection()
    {
        var id = Model.ReadingThreadId;
        if (ThreadList.SelectedItem is PostCard selected && selected.Post.Id == id) return;
        var target = id is null ? null : ThreadList.Items.OfType<PostCard>().FirstOrDefault(c => c.Post.Id == id);
        if (target is null && ThreadList.SelectedItem is null) return;
        _syncingThreadSelection = true;
        try { ThreadList.SelectedItem = target; }
        finally { _syncingThreadSelection = false; }
    }
    /// <summary>Alt+←/→ 对应阅读历史后退/前进；焦点在可编辑的输入框里时不处理，保留输入框自己的按键。</summary>
    internal static int ReadingHistoryStep(Key key, ModifierKeys modifiers, IInputElement? focused) =>
        modifiers != ModifierKeys.Alt || focused is TextBoxBase { IsReadOnly: false } ? 0 : key switch { Key.Left => -1, Key.Right => 1, _ => 0 };
    internal void NavigateReadingHistory(int step) => (step < 0 ? Model.BackCommand : Model.ForwardCommand).Execute(null);
    private void MyPosts_Click(object sender, RoutedEventArgs e) => new MyPostsWindow(Model) { Owner = this }.Show();
    private void Jump_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Model.JumpCommand.Execute(null); e.Handled = true; } }
    private void Page_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { Model.JumpPageCommand.Execute(null); e.Handled = true; } }
    private void Page_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ((TextBox)sender).SelectAll();
    private void Page_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var input = (TextBox)sender;
        // 只接管首次左键聚焦；随后点击、拖选、双击和右键菜单仍由原生编辑控件处理。
        if (e.ChangedButton != MouseButton.Left || input.IsKeyboardFocusWithin) return;
        input.Focus(); input.SelectAll(); e.Handled = true;
    }
    private async void QuickThread_Click(object sender, RoutedEventArgs e)
    {
        if (long.TryParse(((Button)sender).Tag?.ToString(), out var id)) await Model.OpenThreadAsync(id);
    }
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        Model.State.Theme = Model.State.Theme == "dark" ? "light" : "dark"; App.ApplyTheme(Model.State.Theme); Model.AppearanceChanged();
    }
    private async void Cookies_Click(object sender, RoutedEventArgs e)
    {
        var thread = Model.Original?.Id; var identity = Model.State.ActiveCookieId;
        new CookieWindow(Model) { Owner = this }.ShowDialog();
        Model.ApplyCookie(); Model.Save(); await Model.RefreshListAsync();
        // 切换身份会清理旧内容；重开刚才的串以便立刻核验权限，位置仍由本机进度恢复。
        if (identity != Model.State.ActiveCookieId && thread is { } id) await Model.OpenThreadAsync(id, refresh: true);
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => new SettingsWindow(Model) { Owner = this }.ShowDialog();
    private async void SubscriptionSettings_Click(object sender, RoutedEventArgs e)
    {
        new SubscriptionWindow(Model) { Owner = this }.ShowDialog();
        await Model.LoadSubscriptionsAsync();
    }
    private void Notice_Click(object sender, RoutedEventArgs e)
    {
        var forum = Model.ActiveForum;
        var html = forum?.Notice;
        new TextWindow(forum is null ? "全岛版规" : forum.Name + " · 版规", string.IsNullOrWhiteSpace(html) ? "请阅读 <a href=\"https://www.nmbxd1.com/t/50000001\">全岛总版规</a>。部分板块需要有效饼干才能访问。" : html) { Owner = this }.ShowDialog();
    }
    private void NewThread_Click(object sender, RoutedEventArgs e) { if (Model.ShowNewThreadAction) OpenComposer(0); }
    private void Reply_Click(object sender, RoutedEventArgs e) { if (Model.CanInteractThread && Model.Original is not null) OpenComposer(Model.Original.Id); }
    private void Quote_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Post { IsSiteTip: false } post && Model.Original is not null) OpenComposer(Model.Original.Id, $">>No.{post.Id}\n");
    }
    private async void OpenComposer(long id, string quote = "", long reportPostId = 0)
    {
        // 编辑器负责保存草稿和发送；返回后根据发送结果、身份变化刷新相关列表。
        var identity = Model.State.ActiveCookieId;
        var composer = new ComposerWindow(Model, id, quote, reportPostId: reportPostId) { Owner = this };
        composer.ShowDialog();
        if (composer.Delivered && id > 0) await Model.OpenThreadAsync(id, refresh: true);
        if ((composer.Delivered && reportPostId == 0) || identity != Model.State.ActiveCookieId) await Model.RefreshListAsync();
        if (composer.Delivered && reportPostId > 0) ShowFeedback("举报已发送", FeedbackArea.Reader);
        else if (composer.Delivered) ShowFeedback(composer.DeliveredMessage, id > 0 ? FeedbackArea.Reader : FeedbackArea.List);
    }
    private void Drafts_Click(object sender, RoutedEventArgs e)
    {
        new DraftWindow(Model, draft =>
        {
            var composer = new ComposerWindow(Model, draft.ThreadId, "", draft) { Owner = this };
            composer.ShowDialog();
            if (composer.Delivered) ShowFeedback(composer.DeliveredMessage, FeedbackArea.General);
        }) { Owner = this }.ShowDialog();
    }
    private void CopyThreadLink_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Original is null) return;
        try { Clipboard.SetText($"{XIslandApi.Site}/t/{Model.Original.Id}"); ShowFeedback("已复制串链接", FeedbackArea.Reader, anchor: (FrameworkElement)sender); }
        catch (System.Runtime.InteropServices.ExternalException) { ShowFeedback("剪贴板暂时不可用，请重试。", FeedbackArea.Reader, true, (FrameworkElement)sender); }
    }
    private void PostHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 阅读栏可拖窄：日期换行后仍完整可见，楼号始终留在右上角。
        var header = (Grid)sender;
        var date = header.Children.OfType<TextBlock>().Single();
        var compact = header.ActualWidth < 420;
        Grid.SetRow(date, compact ? 1 : 0); Grid.SetColumn(date, compact ? 0 : 1);
        Grid.SetColumnSpan(date, compact ? 3 : 1);
        date.HorizontalAlignment = HorizontalAlignment.Left;
        date.Margin = compact ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 8, 0);
        header.ColumnDefinitions[2].Width = new GridLength(1, GridUnitType.Star);
    }
    private void BackToTop_Click(object sender, RoutedEventArgs e)
    {
        InertialScroll.Stop(ReaderList);
        if (ReaderList.Items.Count > 0) ReaderList.ScrollIntoView(ReaderList.Items[0]);
        ReadingAnchor.Children<ScrollViewer>(ReaderList).FirstOrDefault()?.ScrollToTop();
    }
    private void ReaderHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 加入后退/前进后操作区宽约 72 DIP；断点同步后移，串号仍保留原来约 226 DIP 的空间。
        // 窄栏时后退/前进留在串号同一行右侧，其余操作换到下一行，标题栏仍保持两行。
        var compact = e.NewSize.Width < 532;
        ReaderHistoryActions.Margin = compact ? new Thickness(0) : new Thickness(0, 0, 8, 0);
        Grid.SetRow(ReaderHeaderActions, compact ? 1 : 0);
        Grid.SetColumn(ReaderHeaderActions, compact ? 0 : 2);
        Grid.SetColumnSpan(ReaderHeaderActions, compact ? 3 : 1);
        ReaderHeaderActions.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        ReaderHeaderActions.Margin = compact ? new Thickness(0, 10, 0, 0) : new Thickness(0);
    }
    public void OpenExternal(string url)
    {
        try
        {
            // 帖子内容不可信：交给系统浏览器前只接受 HTTP/HTTPS，拒绝文件和自定义协议。
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) throw new ArgumentException("只支持 HTTP/HTTPS 网页链接。");
            _openBrowser(uri.AbsoluteUri);
        }
        catch (Exception ex) { Model.Report(ex); }
    }
    private async void ContentLinkClicked(System.Windows.Documents.Hyperlink source, string url)
    {
        // 引用/版规等子窗口也属于其主窗口；其他主窗口不能响应同一个静态事件。
        var owner = Window.GetWindow(source);
        while (owner is not null && owner is not MainWindow) owner = owner.Owner;
        if (!ReferenceEquals(owner, this)) return;
        await OpenContentLinkAsync(url, Window.GetWindow(source));
    }
    internal Task OpenContentLinkAsync(string url, Window? sourceWindow = null)
    {
        if (_closed) return Task.CompletedTask;
        try
        {
            var link = IslandLink.Parse(url) ?? throw new ArgumentException("只支持 HTTP/HTTPS 网页链接。");
            // 从版规/引用等子窗口点击时，新小窗跟随该窗口；关闭后仍回到点击前的位置。
            var owner = sourceWindow is { IsVisible: true } ? sourceWindow : this;
            if (link.Kind == IslandLinkKind.Thread) new ThreadLinkPreviewWindow(Model, link) { Owner = owner }.Show();
            else if (link.Kind is IslandLinkKind.Forum or IslandLinkKind.Timeline)
                new ForumLinkPromptWindow(Model, link, OpenExternal) { Owner = owner }.Show();
            else OpenExternal(link.Url.AbsoluteUri);
        }
        catch (Exception ex) { if (!_closed) Model.Report(ex); }
        return Task.CompletedTask;
    }
    private async void Reference(long id)
    {
        // 先展示占位窗口再异步读取，用户不必等待网络即可关闭弹窗。
        var dialog = new TextWindow($"引用 · No.{id}", "正在读取引用…", referencePreview: true, Model.ReadingFontSize) { Owner = this };
        dialog.Show();
        // 取消源跟随弹窗生命周期；保存 Token 供 await 后检查，不能在此方法返回时提前 Dispose。
        var cts = new CancellationTokenSource(); var ct = cts.Token;
        dialog.Closed += (_, _) => { cts.Cancel(); cts.Dispose(); };
        try
        {
            var post = await Model.Api.GetReferenceAsync(id, ct);
            if (ct.IsCancellationRequested) return;
            dialog.SetPost(post);
            // ref 接口不保证返回所属主串；只给已经确认的串首提供打开入口。
            // 回复与类型未知的引用不再显示必然或可能失败的试探性跳转按钮。
            if (Model.IsKnownThreadRoot(id))
                dialog.AddAction("打开串", async () => { dialog.Close(); await Model.OpenThreadAsync(id); });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) dialog.SetHtml(System.Net.WebUtility.HtmlEncode(ex.Message)); }
    }
    private void OpenImage(PostImage source, Post post)
    {
        // 仅从串阅读区进入时串联同串图片；串列表与引用弹窗保持单图，避免混入其他串。
        DependencyObject? parent = source;
        while (parent is not null && !ReferenceEquals(parent, ReaderList)) parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        var gallery = parent is null ? new[] { post } : Model.ReadingPosts.Select(p => p.Post).Where(p => p.HasImage).ToArray();
        new ImageWindow(gallery.Length == 0 ? [post] : gallery, post.Id, ImageService.BytesAsync,
            parent is null ? null : Model.CreateImageCursor()) { Owner = this }.Show();
    }
}

using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>通用对话框骨架：固定标题、可滚动内容和固定底部操作栏，统一使用主程序主题。</summary>
public class DialogWindow : ChildWindow
{
    protected StackPanel Body { get; } = new() { Margin = UiMetrics.DialogBodyMargin };
    protected WrapPanel Actions { get; } = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = UiMetrics.DialogActionsMargin };
    protected TextBlock Feedback { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 12, 0, 8) };
    private readonly Border _bodyHost = new();
    protected void SetBodyContent(UIElement content) => _bodyHost.Child = content;
    public DialogWindow(string title, string subtitle = "", double subtitleFontSize = 12) : base(title)
    {
        Width = UiMetrics.DialogWidth; Height = 660; MinWidth = 420; MinHeight = 240;
        var grid = new Grid(); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        // 没有副标题时不预留说明行，让表单紧接标题。
        var heading = new StackPanel { Margin = new(24, 0, 24, 0) };
        if (subtitle.Length > 0) { var text = Hint(subtitle); text.FontSize = subtitleFontSize; text.LineHeight = subtitleFontSize * 1.5; heading.Children.Add(text); }
        grid.Children.Add(heading);
        var scroll = new ScrollViewer { Content = Body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _bodyHost.Child = scroll; Grid.SetRow(_bodyHost, 1); grid.Children.Add(_bodyHost);
        UseReadingScroll(scroll);
        var footer = new Border { Child = Actions }; Grid.SetRow(footer, 2); grid.Children.Add(footer);
        Feedback.SetResourceReference(TextBlock.ForegroundProperty, "Danger");
        var feedbackStyle = new Style(typeof(TextBlock));
        var emptyFeedback = new Trigger { Property = TextBlock.TextProperty, Value = "" };
        emptyFeedback.Setters.Add(new Setter(VisibilityProperty, Visibility.Collapsed));
        feedbackStyle.Triggers.Add(emptyFeedback); Feedback.Style = feedbackStyle;
        SetClientContent(grid);
        // Short dialogs fit their real content; long forms keep a bounded scroll area and fixed actions.
        Loaded += (_, _) =>
        {
            if (Body.Children.Count == 0) return;
            Body.Measure(new Size(Math.Max(1, ActualWidth - 2), double.PositiveInfinity));
            var fitted = Math.Max(MinHeight, Body.DesiredSize.Height + heading.ActualHeight + Actions.ActualHeight + Actions.Margin.Top + Actions.Margin.Bottom + UiMetrics.ChildTitleHeight + 4);
            if (fitted < Height) { var difference = Height - fitted; Height = fitted; Top += difference / 2; }
        };
    }
    protected static TextBlock Hint(string text) { var b = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new(0, 9, 0, 9), LineHeight = 21 }; b.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return b; }
    protected void Label(string text) { var label = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.Medium, Margin = new(0, 16, 0, 10) }; label.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Body.Children.Add(label); }
    protected static Button Button(string label, Action action, bool primary = false)
    {
        var b = new Wpf.Ui.Controls.Button { Content = label, Margin = new(8, 0, 0, 0) };
        if (primary) b.SetResourceReference(StyleProperty, "Primary");
        b.Click += (_, _) => action(); return b;
    }
    public void AddAction(string text, Action action, bool primary = false) => Actions.Children.Add(Button(text, action, primary));
}

/// <summary>复用正文控件展示版规、发送预览或单条引用，调用方可追加操作按钮。</summary>
public sealed class TextWindow : DialogWindow
{
    private readonly PostBody _body = new() { FontSize = 16 };
    private readonly bool _referencePreview;
    private bool _fitQueued, _fitting, _closed;
    public TextWindow(string title, string html, bool referencePreview = false, double readingFontSize = 16) : base(title)
    {
        _referencePreview = referencePreview;
        _body.FontSize = readingFontSize;
        if (referencePreview)
        {
            _body.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _body.SizeChanged += (_, _) => QueueReferenceFit();
            _body.LayoutUpdated += (_, _) =>
            {
                var lineHeight = _body.Document.LineHeight;
                if (double.IsFinite(lineHeight) && Math.Abs(_body.MaxHeight - (lineHeight * 10 + 4)) > .1) QueueReferenceFit();
            };
            Body.SizeChanged += (_, _) => QueueReferenceFit();
            Loaded += (_, _) => QueueReferenceFit();
            SizeChanged += (_, e) => { if (e.WidthChanged) QueueReferenceFit(); };
            Closed += (_, _) => _closed = true;
        }
        Body.Children.Add(_body); SetHtml(html); AddAction("关闭", Close);
    }
    public void SetHtml(string html) { _body.Html = html; QueueReferenceFit(); }
    public void SetPost(Post post)
    {
        Body.Children.Clear(); Body.Children.Add(_body);
        SetHtml(post.Content);
        Body.Children.Insert(0, new PostIdentity { Post = post, Margin = new(0, 0, 0, 6) });
        var timestamp = Hint($"{PostTime.Format(post.Time, includeSeconds: true)}  ·  {post.Number}");
        timestamp.ToolTip = post.Time;
        Body.Children.Insert(1, timestamp);
        var author = PostCard.Optional(post.Name, "无名氏", Application.Current.Resources["HideSpoilers"] is true);
        if (author.Length > 0) Body.Children.Insert(2, new EmojiLabel { Text = author, Margin = new(0,0,0,8), Foreground = (Brush)FindResource("Muted") });
        if (post.HasImage) Body.Children.Add(new PostImage { Post = post, PreviewHeight = _referencePreview ? 160 : 240, Margin = new(0, 20, 0, 0) });
        QueueReferenceFit();
    }
    private void QueueReferenceFit()
    {
        if (!_referencePreview || _closed || _fitting || _fitQueued) return;
        _fitQueued = true;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, (Action)(() =>
        {
            _fitQueued = false;
            if (_closed || !IsLoaded || WindowState != WindowState.Normal) return;
            _fitting = true;
            try
            {
                var lineHeight = _body.Document.LineHeight;
                if (!double.IsFinite(lineHeight) || lineHeight <= 0) lineHeight = _body.FontSize * UiMetrics.BodyLineHeightRatio;
                _body.MaxHeight = lineHeight * 10 + 4;
                Body.Measure(new Size(Math.Max(1, ActualWidth - 2), double.PositiveInfinity));
                var desired = Body.DesiredSize.Height + Actions.ActualHeight + Actions.Margin.Top + Actions.Margin.Bottom + UiMetrics.ChildTitleHeight + 4;
                WindowPlacement.FitContentHeight(this, desired);
            }
            finally { _fitting = false; }
        }));
    }
}

/// <summary>列出未发送草稿，通过回调打开编辑器；编辑器关闭后重新读取列表。</summary>
public sealed class DraftWindow : DialogWindow
{
    public DraftWindow(MainViewModel vm, Action<DraftWindow, Draft> open) : base("草稿箱")
    {
        void Refresh()
        {
            Body.Children.Clear();
            if (vm.State.Drafts.Count == 0) Body.Children.Add(Hint("暂无草稿"));
            foreach (var draft in vm.State.Drafts.OrderByDescending(d => d.ThreadId))
            {
                var panel = new StackPanel { Margin = new(0, 12, 0, 16) };
                panel.Children.Add(new TextBlock { Text = draft.ReportPostId > 0 ? $"举报 No.{draft.ReportPostId}" : draft.ThreadId > 0 ? $"回复 No.{draft.ThreadId}" : "新串 · " + vm.ForumName(draft.ForumId), FontWeight = FontWeights.SemiBold });
                var preview = ContentText.Plain(draft.Content, vm.State.HideSpoilers); panel.Children.Add(Hint(preview.Length > 100 ? preview[..100] + "…" : preview));
                var bar = new WrapPanel(); bar.Children.Add(Button("继续编辑", () => { open(this, draft); Refresh(); }));
                bar.Children.Add(Button("删除草稿", () =>
                {
                    if (!ConfirmWindow.Ask(this, "删除草稿", "删除这份本机草稿？")) return;
                    vm.State.Drafts.RemoveAll(d => d.Key == draft.Key); vm.Save(); Refresh();
                })); panel.Children.Add(bar); Body.Children.Add(panel);
            }
        }
        Refresh(); AddAction("关闭", Close);
    }
}

internal sealed class ConfirmWindow : DialogWindow
{
    private ConfirmWindow(string title, string message) : base(title)
    {
        Width = 440; Height = 260; MinHeight = 200;
        Body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 15, Margin = new(0, 8, 0, 8) });
        AddAction("取消", Close);
        AddAction("确定", () => { DialogResult = true; }, true);
        var confirm = Actions.Children.OfType<Button>().Last(); confirm.IsDefault = true;
        Loaded += (_, _) => confirm.Focus();
    }
    internal static bool Ask(Window owner, string title, string message) => new ConfirmWindow(title, message) { Owner = owner, ShowInTaskbar = false }.ShowDialog() == true;
}





using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Tide.Core;

namespace Tide.Desktop;

public partial class MainWindow
{
    // 阻止 ListBoxItem 默认的右键选中行为；鼠标抬起仍由 WPF 打开原生菜单。
    // 否则右键想屏蔽一个未打开的串，也会触发 SelectionChanged 并打断当前阅读。
    private void Thread_RightButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static void MenuAction(ContextMenu menu, string title, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = title, IsEnabled = enabled };
        item.Click += (_, _) => action(); menu.Items.Add(item);
    }

    private void ThreadMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender; menu.Items.Clear();
        if (menu.PlacementTarget is FrameworkElement { DataContext: PostCard card } target)
        {
            MenuAction(menu, "屏蔽串", () => RunFeedbackAction(FeedbackArea.List, target, () => Model.BlockThread(card.Post)));
            var name = card.Post.Fid > 0 ? Model.ForumName(card.Post.Fid) : "未知板块";
            MenuAction(menu, $"屏蔽板块（{name}）", () => RunFeedbackAction(FeedbackArea.List, target, () => Model.BlockForum(card.Post)), card.Post.Fid > 0);
        }
    }

    private void ForumMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender; menu.Items.Clear();
        if (menu.PlacementTarget is FrameworkElement { DataContext: Forum forum } target)
            MenuAction(menu, Model.FavoriteForumKeys.Contains(forum.Key) ? "取消收藏板块" : "收藏板块",
                () => RunFeedbackAction(FeedbackArea.Navigation, target, () => Model.ToggleForumFavorite(forum)));
    }

    private void PostMenu_Opened(object sender, RoutedEventArgs e)
    {
        var menu = (ContextMenu)sender; menu.Items.Clear();
        if (menu.PlacementTarget is not FrameworkElement { DataContext: ReadPost entry } target) return;
        var body = target as PostBody ?? ReadingAnchor.Children<PostBody>(target).FirstOrDefault();
        // 从已经渲染的文档复制，保留换行和彩色 emoji，且不会泄漏未展开的防剧透文字。
        // “复制正文”始终复制整楼；选区复制仍使用正文原有的 Ctrl+C。
        var text = body is null ? "" : EmojiText.Read(body.Document.ContentStart, body.Document.ContentEnd).TrimEnd('\r', '\n');
        void Copy(string label, string value) => MenuAction(menu, label, () =>
        {
            try { Clipboard.SetText(value); ShowFeedback(label == "复制串号" ? "已复制串号" : "已复制正文", FeedbackArea.Reader, anchor: target); }
            catch (ExternalException) { ShowFeedback("剪贴板暂时不可用，请重试。", FeedbackArea.Reader, true, target); }
        }, value.Length > 0);
        if (!entry.IsTip && entry.Post.Id > 0) Copy("复制串号", entry.Post.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Copy("复制正文", text);
        // Tips 不是发言，不能回应或举报；菜单捕获当前目标，避免异步切串改变操作对象。
        if (!entry.IsTip && Model.Original is { } original)
        {
            MenuAction(menu, "回应", () => OpenComposer(original.Id, $">>No.{entry.Post.Id}\n"));
            MenuAction(menu, "举报", () => OpenComposer(0, reportPostId: entry.Post.Id));
        }
    }
}

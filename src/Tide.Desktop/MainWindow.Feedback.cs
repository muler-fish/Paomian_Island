using System.ComponentModel;
using System.Windows;

namespace Tide.Desktop;

public partial class MainWindow
{
    private (FeedbackArea Area, Point? Origin)? _actionOrigin;
    private Point? _favoriteOrigin;

    private void FeedbackReceived(InteractionFeedback feedback)
    {
        var origin = _actionOrigin is { } action && action.Area == feedback.Area ? action.Origin
            : feedback.Area == FeedbackArea.Navigation ? _favoriteOrigin : null;
        if (feedback.Area == FeedbackArea.Navigation) _favoriteOrigin = null;
        // 订阅也可由快捷键触发，仍以工具栏星标为动画起点。
        ShowFeedback(feedback.Message, feedback.Area, feedback.IsError,
            feedback.Area == FeedbackArea.Reader ? ThreadSubscriptionButton : null, origin);
    }

    private void ForumFavorite_Toggling(object? sender, EventArgs e) => _favoriteOrigin = FeedbackLayer.CaptureOrigin(sender as FrameworkElement);

    private void RunFeedbackAction(FeedbackArea area, FrameworkElement source, Action action)
    {
        var previous = _actionOrigin;
        _actionOrigin = (area, FeedbackLayer.CaptureOrigin(source));
        try { action(); } finally { _actionOrigin = previous; }
    }

    private void ShowFeedback(string message, FeedbackArea area, bool isError = false, FrameworkElement? anchor = null, Point? origin = null)
    {
        if (_closed || !IsLoaded) return;
        FrameworkElement region = area switch
        {
            FeedbackArea.Navigation => NavigationPane,
            FeedbackArea.List => ListPane,
            FeedbackArea.Reader => ReaderPane,
            _ => ContentColumns
        };
        FeedbackLayer.Show(new(message, area, isError), region, anchor, origin);
    }

    private void FeedbackStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 发起新请求时清除该区域的旧提示；另一栏的并行操作不受影响。
        if (e.PropertyName == nameof(MainViewModel.ListBusy) && Model.ListBusy) FeedbackLayer.Dismiss(FeedbackArea.List);
        if (e.PropertyName == nameof(MainViewModel.ThreadBusy) && Model.ThreadBusy) FeedbackLayer.Dismiss(FeedbackArea.Reader);
    }
}

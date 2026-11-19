namespace Paomian.Desktop;

public enum FeedbackArea { General, Navigation, List, Reader }
public sealed record InteractionFeedback(string Message, FeedbackArea Area, bool IsError = false);

public sealed partial class MainViewModel
{
    public event Action<InteractionFeedback>? FeedbackRaised;

    public void Notify(string message, FeedbackArea area) => FeedbackRaised?.Invoke(new(message, area));

    public void SetError(string message, FeedbackArea area)
    {
        if (Set(ref _error, message, nameof(Error))) Changed(nameof(HasError));
        // 相同错误在重试后仍要提示；不能依赖 PropertyChanged 的值去重。
        if (message.Length > 0) FeedbackRaised?.Invoke(new(message, area, true));
    }
}

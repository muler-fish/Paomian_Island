using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Paomian.Desktop;

/// <summary>让鼠标、空格键和屏幕阅读器的 Toggle 都更新同一份收藏数据。</summary>
public sealed class ForumFavoriteToggleButton : ToggleButton
{
    // 收藏快捷行可能在执行后消失，让所属视图在更改前记录反馈动画起点。
    public event EventHandler? Toggling;
    public static readonly DependencyProperty ToggleCommandProperty = DependencyProperty.Register(
        nameof(ToggleCommand), typeof(ICommand), typeof(ForumFavoriteToggleButton));
    public ICommand? ToggleCommand
    {
        get => (ICommand?)GetValue(ToggleCommandProperty);
        set => SetValue(ToggleCommandProperty, value);
    }

    protected override void OnToggle() => ToggleFavorite();

    internal void ToggleFavorite()
    {
        // Toggle 自动化会直接调用此处而不触发 Click。勾选状态由数据绑定回写，避免只改外观。
        // 不复用 ButtonBase.Command，否则普通点击还会再执行一次，导致收藏后立刻取消。
        if (IsEnabled && ToggleCommand?.CanExecute(CommandParameter) == true)
        {
            Toggling?.Invoke(this, EventArgs.Empty);
            ToggleCommand.Execute(CommandParameter);
        }
    }
}

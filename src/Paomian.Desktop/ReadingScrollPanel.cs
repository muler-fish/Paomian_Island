using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Paomian.Desktop;

/// <summary>变高行以首个可见容器为参照，把相对阅读位移留在同一段内容上。</summary>
public sealed class ReadingScrollPanel : VirtualizingStackPanel, IScrollInfo
{
    private FrameworkElement? _anchor;
    private object? _item;
    private double _top, _arrangedOffset, _pending;
    private FrameworkElement? _wheelRow;
    private object? _wheelItem;
    private double _wheelTop;
    internal double RequestedOffset => VerticalOffset + _pending;
    // 由离线性能专项在采样期间设置，普通阅读不读时钟。
    internal Action<bool, TimeSpan>? LayoutSample { get; set; }

    internal void ForgetWheelRow()
    {
        _wheelRow = null;
        _wheelItem = null;
    }

    internal void ScrollBy(double distance, bool wheelStep = false)
    {
        // 下一次输入/呈现帧看到的是上一次所有布局轮次的最终结果。
        // 用同一条可见内容消化设备像素舍入误差，尚未布局的请求已经包含在 _pending 中。
        if (wheelStep && _wheelRow is { } previous && ReferenceEquals(previous.DataContext, _wheelItem) &&
            VisualTreeHelper.GetParent(previous) == this)
        {
            var error = VisualTreeHelper.GetOffset(previous).Y - _wheelTop - _pending;
            if (Math.Abs(error) <= 2) distance += error;
        }
        _wheelRow = wheelStep ? _anchor : null;
        _wheelItem = _wheelRow?.DataContext;
        var offset = Math.Clamp(RequestedOffset + distance, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        _pending = offset - VerticalOffset;
        if (_wheelRow is not null) _wheelTop = VisualTreeHelper.GetOffset(_wheelRow).Y - _pending;
        SetVerticalOffset(offset);
    }

    void IScrollInfo.SetVerticalOffset(double offset)
    {
        // 滚动条与显式跳转直接定位；先让出动画对视口的控制。
        if (ItemsControl.GetItemsOwner(this) is ListBox list) InertialScroll.Stop(list);
        _anchor = null;
        _pending = 0;
        ForgetWheelRow();
        SetVerticalOffset(offset);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (LayoutSample is not { } sample) return base.MeasureOverride(availableSize);
        var start = Stopwatch.GetTimestamp();
        var size = base.MeasureOverride(availableSize);
        sample(true, Stopwatch.GetElapsedTime(start));
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (LayoutSample is not { } sample) return ArrangeReadingRows(arrangeSize);
        var start = Stopwatch.GetTimestamp();
        var size = ArrangeReadingRows(arrangeSize);
        sample(false, Stopwatch.GetElapsedTime(start));
        return size;
    }

    private Size ArrangeReadingRows(Size arrangeSize)
    {
        var result = base.ArrangeOverride(arrangeSize);
        if (_anchor is { } row && ReferenceEquals(row.DataContext, _item) && VisualTreeHelper.GetParent(row) == this &&
            (_pending != 0 || Math.Abs(_arrangedOffset - VerticalOffset) < .01))
        {
            // 只补偿同一楼层实际多移动的部分。面板自己已经补足时无需再次调整。
            var drift = VisualTreeHelper.GetOffset(row).Y - (_top - _pending);
            if (Math.Abs(drift) > 1)
            {
                var corrected = Math.Clamp(VerticalOffset + drift, 0, Math.Max(0, ExtentHeight - ViewportHeight));
                if (Math.Abs(corrected - VerticalOffset) > 1)
                {
                    _anchor = null;
                    _pending = 0;
                    SetVerticalOffset(corrected);
                    return result;
                }
            }
        }

        _pending = 0;
        _anchor = null;
        _item = null;
        _arrangedOffset = VerticalOffset;
        foreach (FrameworkElement child in InternalChildren)
        {
            var top = VisualTreeHelper.GetOffset(child).Y;
            if (top + child.ActualHeight <= 0 || top >= ViewportHeight) continue;
            _anchor = child;
            _item = child.DataContext;
            _top = top;
            break;
        }
        return result;
    }
}

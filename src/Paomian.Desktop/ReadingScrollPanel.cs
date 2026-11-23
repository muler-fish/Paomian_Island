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

    internal void ScrollBy(double distance)
    {
        var offset = Math.Clamp(VerticalOffset + _pending + distance, 0, Math.Max(0, ExtentHeight - ViewportHeight));
        _pending = offset - VerticalOffset;
        SetVerticalOffset(offset);
    }

    void IScrollInfo.SetVerticalOffset(double offset)
    {
        // 滚动条与显式跳转直接定位；先让出动画对视口的控制。
        if (ItemsControl.GetItemsOwner(this) is ListBox list) InertialScroll.Stop(list);
        _anchor = null;
        _pending = 0;
        SetVerticalOffset(offset);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
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

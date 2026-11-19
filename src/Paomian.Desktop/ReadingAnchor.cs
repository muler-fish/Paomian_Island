using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Paomian.Desktop;

/// <summary>在前面插入楼层时保留一个可见项及其屏幕位置，不依赖所有旧项的估算高度。</summary>
internal sealed record ReadingAnchor(object Item, double Top)
{
    public static ReadingAnchor? Capture(ListBox list, bool skipTips = false)
    {
        list.UpdateLayout();
        var container = Children<ListBoxItem>(list)
            .Where(c => !skipTips || c.DataContext is not ReadPost { IsTip: true })
            .Select(c => (Container: c, Y: c.TransformToAncestor(list).Transform(new Point()).Y))
            .Where(c => c.Container.ActualHeight > 0 && c.Y + c.Container.ActualHeight > 0 && c.Y < list.ActualHeight)
            .OrderBy(c => c.Y).FirstOrDefault();
        return container.Container is null ? null : new(container.Container.DataContext, container.Y);
    }

    public void Restore(ListBox list)
    {
        if (!list.Items.Contains(Item)) return;
        var viewer = Children<ScrollViewer>(list).FirstOrDefault();
        if (viewer is null) return;
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(Item) is not ListBoxItem)
        {
            list.ScrollIntoView(Item); list.UpdateLayout();
        }
        // 虚拟面板先用估算高度，再测量附近项；以真实容器的坐标进行有限次纠偏。
        for (var i = 0; i < 4; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(Item) is not ListBoxItem container) break;
            var difference = container.TransformToAncestor(list).Transform(new Point()).Y - Top;
            if (Math.Abs(difference) < .5) break;
            viewer.ScrollToVerticalOffset(Math.Max(0, viewer.VerticalOffset + difference)); list.UpdateLayout();
        }
    }

    internal static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Children<T>(child)) yield return nested;
        }
    }
}

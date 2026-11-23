using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static partial class KeyboardReadingChecks
{
    private static async Task CheckStability(MainWindow window)
    {
        var list = Find<ListBox>(window, "ThreadList");
        var reader = Find<ListBox>(window, "ReaderList");
        await window.Model.OpenThreadAsync(KeyboardReadingFixture.Thread);
        foreach (var surface in new[] { list, reader })
        {
            var viewer = Descendants<ScrollViewer>(surface).First();
            var pane = Find<FrameworkElement>(window, surface == list ? "ListPane" : "ReaderPane");
            pane.Focus();
            await Idle();
            var item = surface.Items[surface == list ? 8 : 2];
            foreach (var wheel in new[] { false, true })
            foreach (var direction in new[] { -1, 1 })
            {
                for (var i = 0; i < 4; i++)
                {
                    new ReadingAnchor(item, -70).Restore(surface);
                    await Idle();
                    if (surface.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem positioned &&
                        Near(positioned.TranslatePoint(new Point(), viewer).Y, -70, 2)) break;
                }
                var row = (ListBoxItem)surface.ItemContainerGenerator.ContainerFromItem(item);
                var observed = surface == list ? (ListBoxItem)surface.ItemContainerGenerator.ContainerFromIndex(10) : row;
                var observedItem = observed.DataContext;
                double Y() => observed.TranslatePoint(new Point(), viewer).Y;
                var previous = (ListBoxItem)surface.ItemContainerGenerator.ContainerFromIndex(surface.Items.IndexOf(item) - 1);
                var previousHeight = previous.ActualHeight;
                Require(Near(row.TranslatePoint(new Point(), viewer).Y, -70, 2), $"测试起点未到达 {surface.Name}");
                Button? loadingImage = null;
                if (wheel && direction < 0 && surface == reader)
                {
                    var image = Descendants<PostImage>(previous).Single();
                    ImageService.ClearMemoryCache();
                    image.SetCurrentValue(PostImage.ShowImagesProperty, false);
                    image.SetCurrentValue(PostImage.ShowImagesProperty, true);
                    loadingImage = (Button)image.Content;
                    Require(loadingImage.Content is ImageLoading, "未进入本地缩略图的异步加载路径");
                }
                if (wheel && surface == reader) Descendants<PostBody>(row).Single().Focus();
                var key = direction < 0 ? Key.Up : Key.Down;
                var start = Y();
                var last = start;
                var steps = new List<double>();
                void Sample(object? sender, EventArgs e)
                {
                    var y = Y();
                    steps.Add(direction * (last - y));
                    last = y;
                }
                CompositionTarget.Rendering += Sample;
                try
                {
                    if (wheel)
                    {
                        for (var i = 0; i < 3; i++)
                        {
                            var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -direction * 120)
                                { RoutedEvent = Mouse.PreviewMouseWheelEvent };
                            viewer.RaiseEvent(args);
                            if (!args.Handled)
                                viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -direction * 120)
                                    { RoutedEvent = Mouse.MouseWheelEvent });
                            await Task.Delay(55);
                            if (i == 0) previous.Height = previousHeight + 180;
                        }
                        await Until(() => !InertialScroll.IsRunning(surface), "滚轮未停止");
                    }
                    else
                    {
                        Down(key);
                        await Task.Delay(65);
                        previous.Height = previousHeight + direction * 24;
                        await Task.Delay(225);
                        Up(key);
                    }
                    await Idle();
                }
                finally { CompositionTarget.Rendering -= Sample; }
                var travel = direction * (start - Y());
                Require(ReferenceEquals(observed.DataContext, observedItem) && travel > 20 && steps.Count > 0 &&
                    steps.Min() >= -1.1 && steps.Max() < (wheel ? viewer.ViewportHeight / 2 : 32),
                    $"{surface.Name} wheel={wheel} dir={direction} travel={travel:F1} step=[{steps.DefaultIfEmpty().Min():F1},{steps.DefaultIfEmpty().Max():F1}]");
                Require(pane.IsKeyboardFocused, "滚动时焦点仍留在可回收的楼层");
                if (loadingImage is not null) Require(loadingImage.Content is Image, "滚动期间缩略图未完成加载");
                if (wheel && SystemParameters.ClientAreaAnimation && SystemParameters.WheelScrollLines == 3)
                    Require(Near(travel, 216, 12), $"三次滚轮输入移动了 {travel:F1} DIP，预期 216 DIP");
                Console.WriteLine($"PASS {surface.Name} wheel={wheel} dir={direction} travel={travel:F1} DIP");
                previous.ClearValue(FrameworkElement.HeightProperty);
                await Idle();
                if (!wheel)
                {
                    var before = Y();
                    Press(key);
                    await Until(() => direction * (before - Y()) >= 26, "短按未移动视口");
                    await Idle();
                    Require(Near(direction * (before - Y()), 28, 2), "短按超过一次步长");
                }
            }
            if (surface == list)
            {
                var anchor = ReadingAnchor.Capture(list)!;
                var id = ((PostCard)anchor.Item).Post.Id;
                await window.Model.RefreshListAsync();
                await Idle();
                var after = ReadingAnchor.Capture(list)!;
                Require(((PostCard)after.Item).Post.Id == id && Near(after.Top, anchor.Top, 2),
                    $"刷新前后首个可见串 {id} → {((PostCard)after.Item).Post.Id}，纵坐标 {anchor.Top:F1} → {after.Top:F1}");
            }
            if (surface == list) await window.Model.LoadMoreListAsync();
            else await window.Model.LoadMoreThreadAsync();
            await CheckRecycling(surface, viewer, surface == list ? 30 : 12);
        }
    }

    private static async Task CheckRecycling(ListBox list, ScrollViewer viewer, int index)
    {
        list.ScrollIntoView(list.Items[index]);
        await Idle();
        var panel = Descendants<ReadingScrollPanel>(list).Single();
        foreach (var direction in new[] { -1, 1 })
        {
            object? item = null;
            double previousY = 0, distance = 0, smallest = 0, largest = 0;
            var lost = false;
            void Sample(object? sender, EventArgs e)
            {
                if (item is not null)
                {
                    if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem previous)
                    {
                        var step = direction * (previousY - previous.TranslatePoint(new Point(), viewer).Y);
                        distance += step;
                        smallest = Math.Min(smallest, step);
                        largest = Math.Max(largest, step);
                    }
                    else lost = true;
                }
                var mid = viewer.ViewportHeight / 2;
                var next = panel.Children.OfType<ListBoxItem>().FirstOrDefault(c =>
                {
                    var y = c.TranslatePoint(new Point(), viewer).Y;
                    return y <= mid && y + c.ActualHeight > mid;
                });
                item = next?.DataContext;
                previousY = next?.TranslatePoint(new Point(), viewer).Y ?? 0;
                if (item is null) lost = true;
            }
            var key = direction < 0 ? Key.Up : Key.Down;
            CompositionTarget.Rendering += Sample;
            try { Down(key); await Task.Delay(2600); Up(key); await Idle(); }
            finally { CompositionTarget.Rendering -= Sample; }
            Require(!lost && distance > 650 && smallest >= -1.1 && largest < 40,
                $"{list.Name} 连续跨行 dir={direction} travel={distance:F1} step=[{smallest:F1},{largest:F1}] lost={lost}");
            Console.WriteLine($"PASS {list.Name} continuous dir={direction} travel={distance:F1} step=[{smallest:F1},{largest:F1}]");
        }
    }
}

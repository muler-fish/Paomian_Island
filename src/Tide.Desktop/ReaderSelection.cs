using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Tide.Desktop;

internal static class ReaderSelection
{
    internal static void Attach(TextBoxBase reader, Func<bool> hasSelection)
    {
        reader.SetResourceReference(TextBoxBase.SelectionBrushProperty, "ReaderSelection");
        // WPF 把选区画在文字上方，透明度需同时保留字色和选区边界。
        reader.SelectionOpacity = .32;
        reader.IsInactiveSelectionHighlightEnabled = true;
        reader.Loaded += (_, _) =>
        {
            // 模板内的正文使用主窗口现有菜单，独立预览和串号才创建选区菜单。
            if (reader.ContextMenu is not null) return;
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = "复制选中文本", Command = ApplicationCommands.Copy, CommandTarget = reader });
            reader.ContextMenu = menu;
        };
        reader.PreviewMouseRightButtonUp += (_, e) =>
        {
            if (!hasSelection() || reader.ContextMenu is not { } context) return;
            // WPF 的文本菜单会在右击选区外时移动光标。已有选区时直接打开同一个菜单。
            e.Handled = true;
            context.PlacementTarget = reader;
            context.Placement = PlacementMode.MousePoint;
            context.IsOpen = true;
        };
    }
}

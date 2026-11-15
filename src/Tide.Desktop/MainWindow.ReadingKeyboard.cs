using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Tide.Desktop;

public partial class MainWindow
{
    private KeyboardReadingScroll _keyboardScroll = null!;
    private ButtonBase? _navigationFocus;
    private Expander? _navigationGroup;
    private bool _keepNavigationOffset;

    private void InitializeReadingKeyboard()
    {
        _keyboardScroll = new(Dispatcher);
        ContentRendered += (_, _) =>
        {
            if (IsActive && (Keyboard.FocusedElement is null || Keyboard.FocusedElement == this)) ListPane.Focus();
        };
        PreviewKeyDown += ReadingKeyDown;
        PreviewKeyUp += (_, e) => _keyboardScroll.Release(e.Key);
        PreviewMouseDown += ReadingMouseDown;
        PreviewMouseWheel += ReadingMouseWheel;
        Deactivated += (_, _) => { CancelReadingKeyboard(); ReadingKeyboardLayer.ShowFocus(null, null); };
        Activated += (_, _) => UpdateReadingFocus(Keyboard.FocusedElement as DependencyObject);
        GotKeyboardFocus += (_, e) =>
        {
            CancelReadingKeyboard();
            UpdateReadingFocus(e.NewFocus as DependencyObject);
        };
        Closed += (_, _) => CancelReadingKeyboard();
        ((INotifyCollectionChanged)ThreadList.Items).CollectionChanged += KeyboardItemsChanged;
        Model.ReadingPosts.CollectionChanged += KeyboardItemsChanged;
        Closed += (_, _) =>
        {
            ((INotifyCollectionChanged)ThreadList.Items).CollectionChanged -= KeyboardItemsChanged;
            Model.ReadingPosts.CollectionChanged -= KeyboardItemsChanged;
        };
        ContentColumns.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, e) =>
        {
            ClearReadingHints();
            if (e.OriginalSource == ForumScroll) ReadingKeyboardLayer.InvalidateVisual();
        }));
        foreach (var pane in KeyboardPanes()) pane.SizeChanged += (_, _) => { ClearReadingHints(); ReadingKeyboardLayer.InvalidateVisual(); };
        ((UIElement)ForumScroll.Content).AddHandler(RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler((_, e) =>
        {
            if (_keepNavigationOffset) e.Handled = true;
        }));
    }

    private void CancelReadingKeyboard()
    {
        _keyboardScroll.Stop();
        ClearReadingHints();
    }

    private void KeyboardItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ClearReadingHints();

    private FrameworkElement[] KeyboardPanes() => [NavigationPane, ListPane, ReaderPane];

    private static IEnumerable<DependencyObject> KeyboardAncestors(DependencyObject? element)
    {
        for (; element is not null; element = element switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(element),
            FrameworkContentElement content => content.Parent,
            ContentElement content => ContentOperations.GetParent(content),
            _ => LogicalTreeHelper.GetParent(element)
        }) yield return element;
    }

    private FrameworkElement? KeyboardPane(DependencyObject? source) => KeyboardAncestors(source)
        .OfType<FrameworkElement>().FirstOrDefault(element => element == NavigationPane || element == ListPane || element == ReaderPane);

    private TaskCommand? ActivePaneRefreshCommand()
    {
        var pane = KeyboardPane(Keyboard.FocusedElement as DependencyObject);
        if (pane == ListPane) return Model.RefreshListCommand;
        if (pane == ReaderPane) return Model.RefreshThreadCommand;
        return null;
    }

    private static bool HasNativeInput(DependencyObject? source) => KeyboardAncestors(source)
        .Any(element => element is TextBoxBase and not PostBody or PasswordBox or ComboBox or ScrollBar);

    private void ReadingKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        var source = Keyboard.FocusedElement as DependencyObject ?? e.OriginalSource as DependencyObject;
        var pane = KeyboardPane(source);
        var modifiers = e.KeyboardDevice.Modifiers;
        if (pane is null || HasNativeInput(source) || e.Key is Key.ImeProcessed or Key.DeadCharProcessed)
        {
            ClearReadingHints();
            return;
        }

        if (e.Key == Key.Tab && (modifiers is ModifierKeys.None or ModifierKeys.Shift))
        {
            e.Handled = true;
            if (!e.IsRepeat) CycleReadingPane(pane, modifiers == ModifierKeys.Shift ? -1 : 1);
            return;
        }
        if (modifiers != ModifierKeys.None || e.Key == Key.System)
        {
            ClearReadingHints();
            return;
        }

        if (pane == NavigationPane)
        {
            e.Handled = MoveNavigation(e.Key, e.IsRepeat, source);
            return;
        }

        if (HandleReadingHint(e.Key, e.IsRepeat, pane)) { e.Handled = true; return; }
        if (e.Key == Key.Enter && pane == ListPane && KeyboardAncestors(source).OfType<ListBoxItem>()
            .FirstOrDefault(item => ItemsControl.ItemsControlFromItemContainer(item) == ThreadList) is { } row)
        {
            if (!e.IsRepeat) OpenKeyboardThread(row);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)) return;
        // 右栏中的主体可选文，组合键留给原生操作。普通浏览键先回到栏位焦点，避免列表选择或光标导致跳页。
        e.Handled = true;
        if (e.IsRepeat) return;
        var list = pane == ListPane ? ThreadList : ReaderList;
        var viewer = ReadingAnchor.Children<ScrollViewer>(list).FirstOrDefault();
        if (viewer is not { IsVisible: true }) return;
        pane.Focus();
        InertialScroll.Stop(list);
        if (list == ReaderList) _readingProgress.BeginUserScroll();
        var direction = e.Key is Key.Up or Key.PageUp or Key.Home ? -1 : 1;
        IncrementalScroll.RequestScroll(list, direction);
        _keyboardScroll.Move(list, viewer, e.Key, direction);
    }

    private void CycleReadingPane(FrameworkElement pane, int step)
    {
        CancelReadingKeyboard();
        var panes = KeyboardPanes();
        var target = panes[(Array.IndexOf(panes, pane) + step + panes.Length) % panes.Length];
        FocusReadingPane(target);
    }

    private void FocusReadingPane(FrameworkElement target)
    {
        if (target != NavigationPane) { target.Focus(); return; }
        var rows = NavigationRows();
        var remembered = _navigationFocus is { } last && rows.Contains(last) ? last
            : rows.Find(row => _navigationGroup is not null && row.TemplatedParent == _navigationGroup);
        _keepNavigationOffset = true;
        try { (remembered ?? rows.FirstOrDefault())?.Focus(); }
        finally { _keepNavigationOffset = false; }
    }

    private void ReadingMouseDown(object sender, MouseButtonEventArgs e)
    {
        CancelReadingKeyboard();
        if (e.ChangedButton is MouseButton.XButton1 or MouseButton.XButton2 || e.OriginalSource is not DependencyObject source) return;
        if (ActivateMousePane(source) != ListPane || e.ChangedButton != MouseButton.Left) return;
        // 等 ListBoxItem 完成原生鼠标选中。缩略图和列表内其他控件仍处理自己的点击。
        var row = KeyboardAncestors(source).TakeWhile(element => element is not (ButtonBase or TextBoxBase or ComboBox or ScrollBar or Thumb))
            .OfType<ListBoxItem>().FirstOrDefault();
        if (row is null || ThreadList.ItemContainerGenerator.ItemFromContainer(row) is not PostCard card) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (IsActive && ListPane.IsKeyboardFocusWithin && row.IsSelected && ReferenceEquals(ThreadList.SelectedItem, card))
                ReaderPane.Focus();
        });
    }

    private void ReadingMouseWheel(object sender, MouseWheelEventArgs e)
    {
        CancelReadingKeyboard();
        if (e.Delta != 0) ActivateMousePane(e.OriginalSource as DependencyObject);
    }

    private FrameworkElement? ActivateMousePane(DependencyObject? source)
    {
        var pane = KeyboardPane(source);
        if (IsActive && pane is { IsKeyboardFocusWithin: false }) FocusReadingPane(pane);
        return pane;
    }

    private void UpdateReadingFocus(DependencyObject? source)
    {
        var pane = KeyboardPane(source);
        ButtonBase? row = null;
        if (pane == NavigationPane)
        {
            var rows = NavigationRows();
            row = NavigationRow(source, rows);
            if (row is not null)
            {
                _navigationFocus = row;
                _navigationGroup = KeyboardAncestors(row).OfType<Expander>().FirstOrDefault();
            }
        }
        ReadingKeyboardLayer.ShowFocus(pane, row);
    }

    private List<ButtonBase> NavigationRows()
    {
        List<ButtonBase> rows = [HomeTimelineButton, SubscriptionsButton, RecentThreadsButton, MyPostsButton, DraftsButton];
        rows.AddRange(ReadingAnchor.Children<ButtonBase>(ForumScroll).Where(button => button.Name == "ForumOpenButton" || button is { Name: "HeaderSite", TemplatedParent: Expander }));
        rows.AddRange([CookiesButton, SettingsButton, ThemeButton]);
        rows.RemoveAll(row => !row.IsVisible || !row.IsEnabled);
        return rows;
    }

    private static ButtonBase? NavigationRow(DependencyObject? source, List<ButtonBase> rows)
    {
        foreach (var element in KeyboardAncestors(source))
        {
            if (element is ButtonBase button && rows.Contains(button)) return button;
            if (element is FrameworkElement { Name: "ForumRow" } row)
                return ReadingAnchor.Children<ButtonBase>(row).FirstOrDefault(rows.Contains);
        }
        return null;
    }

    private bool MoveNavigation(Key key, bool repeat, DependencyObject? source)
    {
        if (key is not (Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter)) return false;
        // 收藏星等原生子控件仍可用 Enter 执行自身的动作。
        var rows = NavigationRows();
        var current = NavigationRow(source, rows);
        if (current is null || rows.Count == 0) return false;
        var index = rows.IndexOf(current);
        var group = current.TemplatedParent as Expander;
        switch (key)
        {
            case Key.Up or Key.Down:
                FocusNavigationRow(rows[Math.Clamp(index + (key == Key.Up ? -1 : 1), 0, rows.Count - 1)]);
                break;
            case Key.Right when group is { IsExpanded: false }:
                group.SetCurrentValue(Expander.IsExpandedProperty, true);
                break;
            case Key.Right when group is not null && index + 1 < rows.Count && group.IsAncestorOf(rows[index + 1]):
                FocusNavigationRow(rows[index + 1]);
                break;
            case Key.Left when group is { IsExpanded: true }:
                group.SetCurrentValue(Expander.IsExpandedProperty, false);
                break;
            case Key.Left:
                var parent = KeyboardAncestors(current).OfType<Expander>().FirstOrDefault(expander => expander != group);
                if (parent is not null && rows.FirstOrDefault(row => row.TemplatedParent == parent) is { } header) FocusNavigationRow(header);
                break;
            case Key.Enter when source is ForumFavoriteToggleButton star:
                if (!repeat) star.ToggleFavorite();
                break;
            case Key.Enter when !repeat && group is not null:
                group.SetCurrentValue(Expander.IsExpandedProperty, !group.IsExpanded);
                break;
            case Key.Enter when !repeat:
                InvokeKeyboardButton(current);
                break;
        }
        return true;
    }

    private static void FocusNavigationRow(ButtonBase row)
    {
        row.Focus();
        row.BringIntoView();
    }

    private void OpenKeyboardThread(ListBoxItem row)
    {
        if (ThreadList.ItemContainerGenerator.ItemFromContainer(row) is not PostCard card || !ReferenceEquals(row.DataContext, card)) return;
        ThreadList.SetCurrentValue(Selector.SelectedItemProperty, card);
        ReaderPane.Focus();
    }

    private void ReadingBody_BringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // 鼠标选文或原生遍历聚焦已进入视口的长回应时，保留当前段落。
        if (sender is not PostBody body || e.TargetObject != body || !body.IsKeyboardFocusWithin) return;
        var viewport = KeyboardAncestors(body).OfType<ScrollContentPresenter>().FirstOrDefault();
        if (viewport is not null && new Rect(viewport.RenderSize).IntersectsWith(body.TransformToAncestor(viewport).TransformBounds(new Rect(body.RenderSize))))
            e.Handled = true;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Media;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Tide.Desktop;

/// <summary>应用子窗口共用的 Fluent 外壳：统一标题栏、主题、关闭入口和首次显示尺寸。</summary>
public abstract class ChildWindow : FluentWindow
{
    private readonly ContentPresenter _client = new();

    protected ChildWindow(string title, bool dialogChrome = true)
    {
        Title = title;
        // 显式沿用 FluentWindow 模板，不能再引用旧 Window 样式，否则会退回系统标题栏。
        SetResourceReference(StyleProperty, typeof(FluentWindow));
        ExtendsContentIntoTitleBar = true;
        WindowBackdropType = WindowBackdropType.None;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowPlacement.FitOnStartup(this);

        if (dialogChrome)
        {
            ResizeMode = ResizeMode.CanResize;
            var shell = new Grid();
            shell.RowDefinitions.Add(new() { Height = new GridLength(UiMetrics.ChildTitleHeight) });
            shell.RowDefinitions.Add(new());
            var header = new Grid { Margin = new(UiMetrics.DialogInset, 16, UiMetrics.DialogInset, 8), Background = Brushes.Transparent };
            header.ColumnDefinitions.Add(new()); header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            var heading = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            heading.SetBinding(TextBlock.TextProperty, new Binding(nameof(Title)) { Source = this });
            header.Children.Add(heading);
            var cross = new System.Windows.Shapes.Path { Data = Geometry.Parse("M1,1 L11,11 M11,1 L1,11"), Width = 12, Height = 12, StrokeThickness = 1.25, VerticalAlignment = VerticalAlignment.Center };
            cross.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
            var close = new System.Windows.Controls.Button { Content = cross };
            close.SetResourceReference(StyleProperty, "DialogCloseButton");
            System.Windows.Automation.AutomationProperties.SetName(close, "关闭");
            close.ToolTip = "关闭 · Esc"; close.Click += (_, _) => Close();
            Grid.SetColumn(close, 1); header.Children.Add(close);
            header.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is TextBlock || ReferenceEquals(e.OriginalSource, header)) DragMove(); };
            shell.Children.Add(header); Grid.SetRow(_client, 1); shell.Children.Add(_client);
            var chrome = new Border { Child = shell, CornerRadius = UiMetrics.DialogCornerRadius, BorderThickness = new(1) };
            chrome.SetResourceReference(Border.BackgroundProperty, "Surface");
            chrome.SetResourceReference(Border.BorderBrushProperty, "Line");
            Content = chrome;
            SetResourceReference(BackgroundProperty, "Surface");
            Loaded += (_, _) => AddOwnerScrim();
            Closed += (_, _) => RemoveOwnerScrim();
        }
        else
        {
            var caption = new TitleBar { Height = UiMetrics.MainTitleHeight, Padding = new(16, 0, 0, 0) };
            caption.SetBinding(TitleBar.TitleProperty, new Binding(nameof(Title)) { Source = this });
            caption.SetResourceReference(BackgroundProperty, "Sidebar");
            caption.SetResourceReference(ForegroundProperty, "Text");
            caption.SetResourceReference(TitleBar.ButtonsForegroundProperty, "Text");
            System.Windows.Automation.AutomationProperties.SetName(caption, "窗口标题栏");
            var buttons = new Style(typeof(TitleBarButton), (Style)FindResource(typeof(TitleBarButton)));
            buttons.Setters.Add(new Setter(HeightProperty, UiMetrics.MainTitleHeight));
            buttons.Setters.Add(new Setter(WidthProperty, 44d));
            caption.Resources.Add(typeof(TitleBarButton), buttons);

            var shell = new Grid();
            shell.SetResourceReference(Panel.BackgroundProperty, "Canvas");
            shell.RowDefinitions.Add(new() { Height = new GridLength(UiMetrics.MainTitleHeight) });
            shell.RowDefinitions.Add(new());
            var divider = new Border { Child = caption, BorderThickness = new(0, 0, 0, 1) };
            divider.SetResourceReference(Border.BorderBrushProperty, "Line");
            shell.Children.Add(divider);
            Grid.SetRow(_client, 1); shell.Children.Add(_client);
            Content = shell;
        }

        // Esc 和标题栏关闭都走正常 Window.Close；编辑器仍能在 Closing 中保存草稿或取消关闭。
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    private AdornerLayer? _ownerLayer;
    private Scrim? _scrim;
    private void AddOwnerScrim()
    {
        if (Owner?.Content is not UIElement ownerContent) return;
        _ownerLayer = AdornerLayer.GetAdornerLayer(ownerContent);
        if (_ownerLayer is null) return;
        _scrim = new Scrim(ownerContent); _ownerLayer.Add(_scrim);
    }
    private void RemoveOwnerScrim()
    {
        if (_scrim is not null) _ownerLayer?.Remove(_scrim);
        _scrim = null; _ownerLayer = null;
    }
    private sealed class Scrim : Adorner
    {
        private readonly Border _tint = new();
        public Scrim(UIElement owner) : base(owner)
        {
            IsHitTestVisible = false;
            _tint.SetResourceReference(Border.BackgroundProperty, "Scrim");
            AddVisualChild(_tint);
        }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => _tint;
        protected override Size ArrangeOverride(Size finalSize) { _tint.Arrange(new Rect(finalSize)); return finalSize; }
    }

    // 派生窗口只提供客户区内容，避免无意替换掉公共标题栏。
    protected void SetClientContent(UIElement content) => _client.Content = content;

    /// <summary>子窗口的内容滚动跟随所属主窗口设置；输入框内部和下拉框仍用原生滚动。</summary>
    protected void UseReadingScroll(FrameworkElement surface)
    {
        Loaded += (_, _) =>
        {
            Window? owner = Owner;
            while (owner is not null && owner is not MainWindow) owner = owner.Owner;
            if (owner is MainWindow main)
                surface.SetBinding(InertialScroll.IsEnabledProperty, new Binding(nameof(MainViewModel.InertialScrolling)) { Source = main.Model });
        };
    }
}

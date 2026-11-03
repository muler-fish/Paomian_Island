using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>管理主窗口尺寸与分栏；布局瞬时适配与用户偏好分开，避免小窗口覆盖原栏宽。</summary>
internal sealed class MainLayoutController : IDisposable
{
    private readonly MainWindow _window;
    private readonly MainViewModel _model;
    private readonly WindowLayoutState _state;
    private readonly Grid _grid;
    private readonly GridSplitter _navigationSplitter, _listSplitter;
    private readonly DispatcherTimer _timer;
    private bool _ready, _applying, _disposed;
    private bool _defaultColumns;

    public MainLayoutController(MainWindow window)
    {
        _window = window; _model = window.Model;
        var restoring = _model.State.WindowLayout is not null;
        _defaultColumns = !restoring;
        _state = _model.State.WindowLayout ??= new(); _state.Normalize();
        _grid = (Grid)window.FindName("ContentColumns");
        _navigationSplitter = (GridSplitter)window.FindName("NavigationSplitter");
        _listSplitter = (GridSplitter)window.FindName("ListSplitter");
        _timer = new(DispatcherPriority.Background, window.Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Flush();
        if (restoring) { window.Width = _state.Width; window.Height = _state.Height; }
        WindowPlacement.FitOnStartup(window, restoring, _state.Maximized);
        window.Loaded += Loaded;
        window.SizeChanged += WindowChanged;
        window.StateChanged += StateChanged;
        window.Closing += Closing;
        _grid.SizeChanged += GridChanged;
        _navigationSplitter.DragCompleted += SplitterChanged; _listSplitter.DragCompleted += SplitterChanged;
        _navigationSplitter.KeyUp += SplitterKeyUp; _listSplitter.KeyUp += SplitterKeyUp;
        ApplyColumns();
    }

    private void Loaded(object sender, RoutedEventArgs e)
    {
        _ready = true; ApplyColumns(); CaptureWindow(); Schedule();
    }
    private void WindowChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_ready || _applying) return;
        CaptureWindow(); Schedule();
    }
    private void GridChanged(object sender, SizeChangedEventArgs e) { if (_ready && !_applying) ApplyColumns(); }
    private void StateChanged(object? sender, EventArgs e) { if (_ready) { CaptureWindow(); Schedule(); } }
    private void CaptureWindow()
    {
        // 最小化既不覆盖正常尺寸，也不把“从最大化最小化”误记成普通窗口。
        if (_window.WindowState == WindowState.Minimized) return;
        _state.Maximized = _window.WindowState == WindowState.Maximized;
        if (_state.Maximized) return;
        if (_window.ActualWidth > 0 && _window.ActualHeight > 0)
        { _state.Width = _window.ActualWidth; _state.Height = _window.ActualHeight; }
    }
    private void ApplyColumns()
    {
        _applying = true;
        try
        {
            var available = Math.Max(1, (_grid.ActualWidth > 0 ? _grid.ActualWidth : _window.Width) - 2);
            if (_defaultColumns)
            {
                var wide = available >= 1500;
                _state.NavigationWidth = wide ? 232 : 188;
                _state.ListWidth = wide ? 520 : 378;
            }
            var scale = Math.Min(1, available / 668);
            var navigationMin = 148 * scale;
            var listMin = 240 * scale; var readerMin = 280 * scale;
            var navigationWidth = Math.Clamp(_state.NavigationWidth, navigationMin, Math.Max(navigationMin, Math.Min(300, available - listMin - readerMin)));
            var listMax = Math.Max(listMin, available - navigationWidth - readerMin);
            var nav = _grid.ColumnDefinitions[0]; var list = _grid.ColumnDefinitions[2];
            // 限制当前可拖范围，保证右侧阅读区不会被固定栏宽挤到窗口外。
            nav.MinWidth = navigationMin; nav.MaxWidth = Math.Max(navigationMin, Math.Min(300, available - listMin - readerMin));
            nav.Width = new(navigationWidth);
            list.MinWidth = listMin; list.MaxWidth = listMax; list.Width = new(Math.Clamp(_state.ListWidth, listMin, listMax));
            _grid.ColumnDefinitions[4].MinWidth = readerMin;
            _grid.ColumnDefinitions[4].Width = new(1, GridUnitType.Star);
        }
        finally { _applying = false; }
    }
    private void SplitterChanged(object sender, DragCompletedEventArgs e) => RememberColumns();
    private void SplitterKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Left or Key.Right) _window.Dispatcher.BeginInvoke((Action)(() => { if (!_disposed) RememberColumns(); }));
    }
    private void RememberColumns()
    {
        if (!_ready || _applying) return;
        _defaultColumns = false;
        // 键盘调整后的 Width 已变化，但 ActualWidth 可能尚未经过本轮排版。
        _grid.UpdateLayout();
        _state.NavigationWidth = _grid.ColumnDefinitions[0].ActualWidth;
        _state.ListWidth = _grid.ColumnDefinitions[2].ActualWidth;
        _state.Normalize(); ApplyColumns(); Flush();
    }
    private void Schedule() { _timer.Stop(); _timer.Start(); }
    private void Closing(object? sender, System.ComponentModel.CancelEventArgs e) => Flush();
    private void Flush()
    {
        _timer.Stop(); if (!_ready || _disposed) return;
        CaptureWindow(); _model.Save();
    }
    public void Dispose()
    {
        _disposed = true; _timer.Stop();
        _window.Loaded -= Loaded;
        _window.SizeChanged -= WindowChanged; _window.StateChanged -= StateChanged; _window.Closing -= Closing;
        _grid.SizeChanged -= GridChanged;
        _navigationSplitter.DragCompleted -= SplitterChanged; _listSplitter.DragCompleted -= SplitterChanged;
        _navigationSplitter.KeyUp -= SplitterKeyUp; _listSplitter.KeyUp -= SplitterKeyUp;
    }
}

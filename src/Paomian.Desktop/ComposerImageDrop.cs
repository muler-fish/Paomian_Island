using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace Paomian.Desktop;

internal sealed class ImageDropSurface : Grid
{
    private readonly Rectangle _outline = new() { RadiusX = 6, RadiusY = 6, StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };
    private readonly SymbolIcon _icon = new(SymbolRegular.ImageAdd24) { FontSize = 32 };
    private readonly TextBlock _message = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new(12, 10, 12, 0) };
    private readonly StackPanel _items = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new(12) };
    internal ImageDropSurface(Button? choose = null)
    {
        MinHeight = choose is null ? 128 : 204;
        var fill = new Border { CornerRadius = new(6) }; fill.SetResourceReference(Border.BackgroundProperty, "Canvas");
        Children.Add(fill); Children.Add(_outline); Children.Add(_items);
        _items.Children.Add(_icon); _items.Children.Add(_message);
        if (choose is not null)
        {
            var hint = new TextBlock { Text = "或者选择本地图片", TextAlignment = TextAlignment.Center, Margin = new(0, 4, 0, 10) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); _items.Children.Add(hint); _items.Children.Add(choose);
        }
        Display(false, null);
    }
    internal void Display(bool dragging, string? error)
    {
        var key = error is not null ? "Danger" : dragging ? "Accent" : "Muted";
        _outline.SetResourceReference(Shape.StrokeProperty, dragging ? key : "Line");
        _icon.SetResourceReference(Control.ForegroundProperty, key); _icon.Symbol = error is null ? SymbolRegular.ImageAdd24 : SymbolRegular.Prohibited24;
        _message.Text = error ?? (dragging ? "松开以添加图片" : "将图片拖到这里");
        _message.SetResourceReference(TextBlock.ForegroundProperty, error is null ? "Text" : "Danger");
    }
}

/// <summary>父窗预览事件先于 RichTextBox 的默认拖放，普通文本原样通过。不接管鼠标捕获，不启动网络请求。</summary>
internal sealed class ComposerImageDrop : IDisposable
{
    private readonly Window _window;
    private readonly Func<string?> _unavailable;
    private readonly Action<bool, string?> _display;
    private readonly Action<string> _accept, _report;
    private string? _lastPath, _fileError;
    private string? _shownError;
    private int _epoch;
    private bool _shown;
    internal ComposerImageDrop(Window window, Func<string?> unavailable, Action<bool, string?> display, Action<string> accept, Action<string> report)
    {
        _window = window; _unavailable = unavailable; _display = display; _accept = accept; _report = report;
        window.AllowDrop = true;
        window.PreviewDragEnter += Over; window.PreviewDragOver += Over; window.PreviewDrop += Drop;
        window.PreviewDragLeave += Leave;
        window.Deactivated += Deactivated; window.Closed += Closed;
    }
    private void Leave(object sender, DragEventArgs e)
    {
        if (!_shown) return;
        // 跨子控件会先 Leave 再 Enter；真正离窗或 Esc 取消则没有后继 Enter。
        var epoch = ++_epoch;
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (epoch == _epoch) Hide(); }));
    }
    private void Hide()
    {
        ++_epoch; _lastPath = _fileError = null; _shownError = null;
        if (_shown) { _shown = false; _display(false, null); }
    }
    private bool Inspect(DragEventArgs e, out string? path, out string? error)
    {
        path = error = null;
        try
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop, false)) return false;
            path = ComposerImageInput.FileFromDrop(e.Data);
            error = _unavailable();
            if (error is null && !e.AllowedEffects.HasFlag(DragDropEffects.Copy)) error = "请复制图片，不能移动原文件。";
            if (error is null && !string.Equals(_lastPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _lastPath = path;
                try { ComposerImageInput.CheckFile(path); _fileError = null; }
                catch (Exception ex) { _fileError = ComposerImageInput.Error(ex); }
            }
            error ??= _fileError;
        }
        catch (Exception ex) { error = ComposerImageInput.Error(ex); }
        e.Handled = true; e.Effects = error is null ? DragDropEffects.Copy : DragDropEffects.None;
        return true;
    }
    private void Over(object sender, DragEventArgs e)
    {
        if (!Inspect(e, out _, out var error)) { Hide(); return; }
        ++_epoch;
        if (!_shown || error != _shownError) { _shown = true; _shownError = error; _display(true, error); }
    }
    private void Drop(object sender, DragEventArgs e)
    {
        var files = Inspect(e, out var path, out var error); Hide();
        if (!files) return;
        if (error is not null) _report(error); else _accept(path!);
    }

    public void Dispose()
    {
        _window.PreviewDragEnter -= Over; _window.PreviewDragOver -= Over; _window.PreviewDrop -= Drop; _window.PreviewDragLeave -= Leave;
        _window.Deactivated -= Deactivated; _window.Closed -= Closed;
        Hide();
    }
    private void Deactivated(object? sender, EventArgs e) => Hide();
    private void Closed(object? sender, EventArgs e) => Dispose();
}

internal sealed class ImageDropWindow : DialogWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    internal ImageDropWindow(Func<string?> unavailable, Func<ComposerImageInput, Action<string>, CancellationToken, Task<bool>> add) : base("添加图片", feedbackInFooter: true)
    {
        Width = 440; Height = 420; MinWidth = 340; ShowInTaskbar = false;
        async void Accept(string path)
        {
            var token = _lifetime.Token;
            if (await add(new(FileName: path), text => Feedback.Text = text, token) && !token.IsCancellationRequested) DialogResult = true;
        }
        var choose = Button("选择本地图片", () =>
        {
            if (unavailable() is { } error) { Feedback.Text = error; return; }
            var picker = new OpenFileDialog { Title = "选择本地图片", Filter = "支持的图片|*.jpg;*.jpeg;*.png;*.gif", Multiselect = false };
            if (picker.ShowDialog(this) == true) Accept(picker.FileName);
        }, true);
        choose.HorizontalAlignment = HorizontalAlignment.Center; choose.Margin = new(0);
        var zone = new ImageDropSurface(choose); Body.Children.Add(zone);
        var limits = Hint(ComposerImageInput.Limits); limits.TextAlignment = TextAlignment.Center; Body.Children.Add(limits);
        AddAction("取消", Close);
        _ = new ComposerImageDrop(this, unavailable, zone.Display, Accept, text => Feedback.Text = text);
        Loaded += (_, _) => choose.Focus(); Closed += (_, _) => { _lifetime.Cancel(); _lifetime.Dispose(); };
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Paomian.Desktop;

public sealed partial class ComposerWindow
{
    private ComposerImage? _image;
    private bool _imageBusy;
    private readonly CancellationTokenSource _imageLifetime = new();
    private readonly ComposerImageCard _card = new();
    // 工具栏下已有 8 DIP 留白，留给角标的上半个点击区。
    private readonly Grid _attachmentRow = new() { Margin = new(0, -8, 0, 4) };
    private readonly TextBlock _filename = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 0, 5) };

    private string? ImageUnavailable() => _sending ? "正在发送。" : _imageBusy ? "正在读取图片。" : _image is not null ? ComposerImageInput.Occupied : null;
    private void ImageFeedback(string text) => Feedback.Text = text;

    private void SetupImages(string? saved, Button attach)
    {
        _attachmentRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); _attachmentRow.ColumnDefinitions.Add(new());
        var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 16, 0, 0) };
        _filename.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _water.ToolTip = "缩略图显示原图，发送后由 X 岛添加水印。";
        details.Children.Add(_filename); details.Children.Add(_water); Grid.SetColumn(details, 1);
        _attachmentRow.Children.Add(_card); _attachmentRow.Children.Add(details); Body.Children.Add(_attachmentRow);
        _card.RemoveRequested += () =>
        {
            if (_sending || _imageBusy) return;
            _image = null; ShowImage(); ImageFeedback(""); SaveDraft(); attach.Focus();
        };
        if (!string.IsNullOrEmpty(saved)) _image = new(saved);
        ShowImage();
        if (_image is not null) Loaded += Restore;
        async void Restore(object sender, RoutedEventArgs e)
        {
            Loaded -= Restore;
            await PrepareImageAsync(new(FileName: _vm.Store.ResolveDraftImage(saved!)), ImageFeedback, default, saved);
        }

        var zone = new ImageDropSurface { Margin = new(20) };
        var overlay = new Border { Visibility = Visibility.Collapsed, IsHitTestVisible = false, Focusable = false, Child = zone };
        overlay.SetResourceReference(Border.BackgroundProperty, "Scrim"); AddClientOverlay(overlay);
        _ = new ComposerImageDrop(this, ImageUnavailable, (dragging, error) =>
        {
            if (dragging) zone.Display(true, error);
            overlay.Visibility = dragging ? Visibility.Visible : Visibility.Collapsed;
        }, path => _ = PrepareImageAsync(new(FileName: path), ImageFeedback, default), ImageFeedback);

        // 富文本框默认不能执行仅含文件的粘贴命令。只对图片补足 CanExecute；文字继续走原有纯文本/IME 路径。
        CommandManager.AddPreviewCanExecuteHandler(_content, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Paste) return;
            try
            {
                if (!Clipboard.ContainsData(DataFormats.FileDrop) && !Clipboard.ContainsImage() && !Clipboard.ContainsData("PNG") && !Clipboard.ContainsData("image/png")) return;
                e.CanExecute = true; e.Handled = true;
            }
            catch (System.Runtime.InteropServices.ExternalException) { e.CanExecute = true; e.Handled = true; } // 实际执行粘贴时报告读取失败。
        });
        CommandManager.AddPreviewExecutedHandler(_content, (_, e) =>
        {
            if (e.Command != ApplicationCommands.Paste) return;
            try
            {
                var input = ComposerImageInput.FromClipboard(Clipboard.GetDataObject());
                if (input is null) return;
                e.Handled = true; _ = PrepareImageAsync(input, ImageFeedback, default);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                e.Handled = true;
                ImageFeedback("无法读取剪贴板，请重试。");
            }
            catch (Exception ex) { ImageFeedback(ComposerImageInput.Error(ex)); }
        });
        Closed += (_, _) =>
        {
            _imageLifetime.Cancel(); _imageLifetime.Dispose();
            _image = null; _card.ShowImage(null);
        };
    }

    private void ShowImage()
    {
        _attachmentRow.Visibility = _image is null ? Visibility.Collapsed : Visibility.Visible;
        _filename.Text = _image?.Name ?? ""; _filename.ToolTip = _filename.Text; _card.ShowImage(_image);
    }

    private void PickImage()
    {
        if (ImageUnavailable() is { } error) { ImageFeedback(error); return; }
        new ImageDropWindow(ImageUnavailable, (input, feedback, ct) => PrepareImageAsync(input, feedback, ct)) { Owner = this }.ShowDialog();
    }

    private async Task<bool> PrepareImageAsync(ComposerImageInput input, Action<string> feedback, CancellationToken ct, string? restore = null)
    {
        if (restore is null && ImageUnavailable() is { } disabled) { feedback(disabled); return false; }
        using var work = CancellationTokenSource.CreateLinkedTokenSource(ct, _imageLifetime.Token);
        _imageBusy = true; feedback("正在读取图片…");
        try
        {
            var result = await input.PrepareAsync(_vm.Store, work.Token, restore);
            if (work.IsCancellationRequested)
            {
                if (input.FileName is null) _vm.Store.DiscardClipboardImage(result.Reference);
                return false;
            }
            _image = result; ShowImage(); feedback(""); Feedback.Text = "";
            if (restore is null) { SaveDraft(); _card.BringIntoView(); }
            return true;
        }
        catch (Exception ex) { if (!work.IsCancellationRequested) feedback(ComposerImageInput.Error(ex)); return false; }
        finally { _imageBusy = false; }
    }
}

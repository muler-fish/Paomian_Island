using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Paomian.Core;

namespace Paomian.Desktop;

/// <summary>每次启动检查一次公告；窗口退出后取消，其他模态窗口使用期间延后呈现。</summary>
internal sealed class StartupNoticePresenter : IDisposable
{
    private readonly Window _owner;
    private readonly MainViewModel _model;
    private readonly SiteNoticeClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _started;
    internal string? Failure { get; private set; }

    public StartupNoticePresenter(Window owner, MainViewModel model, SiteNoticeClient client)
    {
        _owner = owner; _model = model; _client = client;
        _owner.Closed += OwnerClosed;
    }

    public async Task RunAsync()
    {
        if (_started) return;
        _started = true;
        var ct = _lifetime.Token;
        try
        {
            // 让主窗口先完成首帧，即使测试 / 缓存响应同步完成，也不在窗口创建中弹框。
            await Task.Yield();
            var notice = await _client.GetAsync(ct);
            if (notice is null || _model.State.SuppressedNoticeKeys.Contains(notice.Key)) return;
            // 不在用户编辑、最小化或切换到其他应用时抢占焦点。
            while (_owner.IsVisible && (_owner is MainWindow { BossKey.IsHidden: true } ||
                !_owner.IsEnabled || !_owner.IsActive || _owner.WindowState == WindowState.Minimized))
                await Task.Delay(150, ct);
            if (ct.IsCancellationRequested || !_owner.IsVisible) return;
            var dialog = new SiteNoticeWindow(notice) { Owner = _owner };
            dialog.ShowDialog();
            if (!ct.IsCancellationRequested && dialog.SuppressThisNotice) _model.SuppressNotice(notice);
        }
        catch (OperationCanceledException) { /* 超时或主窗口关闭不打断浏览。 */ }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or IslandException)
        {
            // 公告获取失败不覆盖目录 / 正文错误，也不把错误响应当成公告弹窗。
            Failure = ex.Message;
        }
    }

    private void OwnerClosed(object? sender, EventArgs e) => _lifetime.Cancel();
    public void Dispose()
    {
        _owner.Closed -= OwnerClosed;
        _lifetime.Cancel(); _lifetime.Dispose();
    }
}

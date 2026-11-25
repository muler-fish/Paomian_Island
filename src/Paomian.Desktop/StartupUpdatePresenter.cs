using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Paomian.Core;

namespace Paomian.Desktop;

/// <summary>主窗口显示后处理一次本地升级；通知可在历史日志仍在读取时关闭。</summary>
internal sealed class StartupUpdatePresenter(MainWindow owner)
{
    private bool _started;

    internal async Task RunAsync()
    {
        if (_started) return;
        _started = true;
        var vm = owner.Model;
        using var lifetime = new CancellationTokenSource();
        void OwnerClosed(object? sender, EventArgs args) => lifetime.Cancel();
        owner.Closed += OwnerClosed;
        try
        {
            PendingUpdateNotice? pending;
            try
            {
                // 旧安装计划与旧 EXE 可用来确认来源，哈希校验在后台完成。
                pending = await Task.Run(() => vm.Store.BeginUpdateNotice(vm.Updates.InstalledVersion,
                    Environment.ProcessPath, UpdateController.BundledNote));
            }
            finally
            {
                // 迁移读完后才能清理旧版安装助手留下的备份。
                _ = vm.Updates.CleanupAsync();
            }
            if (pending is null || lifetime.IsCancellationRequested) return;
            var local = await Task.Run(() => vm.Updates.LocalStartupNotes(pending), lifetime.Token);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            while (!lifetime.IsCancellationRequested &&
                (!owner.IsVisible || !owner.IsActive || !owner.IsEnabled || owner.BossKey.IsHidden || owner.WindowState == WindowState.Minimized))
                await Task.Delay(150, lifetime.Token);
            if (lifetime.IsCancellationRequested) return;

            var window = new UpdateNoticeWindow(pending.Target, owner.OpenExternal) { Owner = owner };
            window.Display(local.Entries, Status(local, pending));
            using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            async void Rendered(object? sender, EventArgs args)
            {
                window.ContentRendered -= Rendered;
                try
                {
                    var result = await vm.Updates.CompleteStartupNotesAsync(pending, local, request.Token);
                    if (!request.IsCancellationRequested) window.Display(result.Entries, Status(result, pending));
                }
                catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            }
            if (local.NeedsOnline) window.ContentRendered += Rendered;
            window.Closed += (_, _) => request.Cancel();
            // Owner 销毁子 HWND 时不会走通知的 Closing，先正常 Close 才能退出 ShowDialog 的消息循环。
            void CloseWithOwner(object? sender, CancelEventArgs args)
            {
                if (!args.Cancel && window.IsVisible) window.Close();
            }
            owner.Closing += CloseWithOwner;
            try
            {
                if (window.ShowDialog() == true && !lifetime.IsCancellationRequested)
                    vm.Store.AcknowledgeUpdateNotice(pending.Target);
            }
            finally { owner.Closing -= CloseWithOwner; }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (!lifetime.IsCancellationRequested) vm.Report(ex);
        }
        finally { owner.Closed -= OwnerClosed; }
    }

    private static string? Status(StartupNotes notes, PendingUpdateNotice pending)
    {
        if (notes.NeedsOnline) return "正在补充历史更新说明…";
        if (pending.Source is null) return "旧资料未记录升级前的版本，仅展示可确认的更新说明。";
        return notes.IsComplete ? null : "部分更新说明暂不可用，当前仅展示已取得的内容。";
    }
}

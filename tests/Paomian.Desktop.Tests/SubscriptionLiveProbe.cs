using System.IO;
using System.Text.Json;
using Paomian.Core;
using Paomian.Desktop;

namespace Paomian.Desktop.Tests;

internal static class SubscriptionLiveProbe
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var identityFile = Path.Combine(output, "test-subscription.dpapi");
        if (File.Exists(identityFile)) throw new InvalidOperationException("请使用全新的测试目录，避免重用已有订阅号。");
        var id = Guid.NewGuid().ToString();
        File.WriteAllText(identityFile, SecretProtection.Protect(id));
        using var api = new XIslandApi();
        var empty = await api.GetSubscriptionsAsync(id);
        if (empty.Count != 0) throw new InvalidOperationException("新测试号不是空列表，停止操作。");
        const long thread = 52752005;
        var added = false; var cleaned = false;
        try
        {
            await api.SetSubscriptionAsync(id, thread, true);
            added = (await api.GetSubscriptionsAsync(id)).Any(p => p.Id == thread);
            if (!added) throw new InvalidOperationException("添加后未读到测试串。");
        }
        finally
        {
            // 只清理本次生成的测试号；即使添加回包丢失，也先读取后决定是否取消。
            var state = await api.GetSubscriptionsAsync(id);
            if (state.Any(p => p.Id == thread)) await api.SetSubscriptionAsync(id, thread, false);
            cleaned = !(await api.GetSubscriptionsAsync(id)).Any(p => p.Id == thread);
            File.WriteAllText(Path.Combine(output, "live-results.json"), JsonSerializer.Serialize(new { AddedAndRead = added, RemovedAndEmpty = cleaned }));
        }
        if (!cleaned) throw new InvalidOperationException("测试订阅未清理，需使用本目录加密号码核对。");
        Console.WriteLine("PASS: live independent test ID add/read/delete/read; cloud list empty after cleanup.");
    }
}

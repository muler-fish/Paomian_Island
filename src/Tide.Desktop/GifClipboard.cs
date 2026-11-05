using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using Tide.Core;

namespace Tide.Desktop;

/// <summary>复制完整 GIF 字节，同时提供文件粘贴格式；不发布静态位图以免接收端优先取首帧。</summary>
internal static class GifClipboard
{
    internal static void Copy(byte[] bytes, long postId, string? directory = null)
    {
        if (XIslandApi.DetectImage(bytes) != "image/gif") throw new ArgumentException("不是 GIF 图片。", nameof(bytes));
        var root = Path.GetFullPath(directory ?? Path.Combine(Path.GetTempPath(), AppIdentity.DataDirectoryName, "Clipboard"));
        Directory.CreateDirectory(root);
        var name = "No." + postId.ToString(CultureInfo.InvariantCulture) + "-" + Convert.ToHexString(SHA256.HashData(bytes)) + ".gif";
        var path = Path.Combine(root, name);
        // 文件不跟随看图窗口或进程释放，保证退出程序后仍可粘贴；相同原图复用一份文件。
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) File.WriteAllBytes(path, bytes);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        using var mime = new MemoryStream(bytes, writable: false);
        using var gif = new MemoryStream(bytes, writable: false);
        using var effect = new MemoryStream(BitConverter.GetBytes(1)); // DROPEFFECT_COPY
        var data = new DataObject();
        data.SetData("image/gif", mime, autoConvert: false);
        data.SetData("GIF", gif, autoConvert: false);
        data.SetFileDropList(new StringCollection { path });
        data.SetData("Preferred DropEffect", effect, autoConvert: false);
        // Flush 后剪贴板拥有字节副本，不依赖上面的流或应用进程生命周期。
        Clipboard.SetDataObject(data, copy: true);
        Trim(root, path);
    }

    private static void Trim(string root, string current)
    {
        // 只清理本模块命名的文件。保留当前剪贴板文件；其他副本最长 7 天，总计不超过 64 MiB。
        try
        {
            var files = new DirectoryInfo(root).GetFiles("No.*.gif", SearchOption.TopDirectoryOnly)
                .Where(f => Regex.IsMatch(f.Name, @"^No\.\d+-[A-F0-9]{64}\.gif$"))
                .OrderBy(f => f.LastWriteTimeUtc).ToArray();
            var total = files.Sum(f => f.Length);
            foreach (var file in files)
            {
                if (string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (total <= 64L * 1024 * 1024 && file.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-7)) continue;
                var length = file.Length; file.Delete(); total -= length;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

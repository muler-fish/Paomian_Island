using System.Security.Cryptography;

namespace Paomian.Core;

public static class PostImageFile
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const string TooLarge = "图片不能超过 5 MB。";
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    public static async Task<byte[]> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("找不到附图，请移除后重新添加。");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        if (stream.Length > MaxBytes) throw new InvalidDataException(TooLarge);
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return bytes;
    }
}

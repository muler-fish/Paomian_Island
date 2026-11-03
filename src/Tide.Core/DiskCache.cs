using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Tide.Core;

/// <summary>Disposable page, subscription and image files. Never owns profile records or update packages.</summary>
public sealed class DiskCache
{
    private readonly object _gate = new();
    private readonly string _profile, _key;
    public string Root { get; private set; }
    public long LimitBytes { get; private set; } = 256_000_000;
    public DiskCache(string profile)
    {
        _profile = Path.GetFullPath(profile);
        _key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_profile.ToUpperInvariant())))[..16];
        Root = Path.Combine(_profile, "cache");
    }
    public string Configure(string? parent, int limitMb)
    {
        lock (_gate)
        {
            var next = string.IsNullOrWhiteSpace(parent) ? Path.Combine(_profile, "cache") : Path.Combine(Path.GetFullPath(parent), "TideCache-" + _key);
            NoLinks(next);
            Directory.CreateDirectory(next);
            var marker = Path.Combine(next, ".tide-cache");
            if (File.Exists(marker) && File.ReadAllText(marker) != _key) throw new IOException("此缓存目录属于另一份资料。");
            if (!File.Exists(marker) && !string.Equals(next, Path.Combine(_profile, "cache"), StringComparison.OrdinalIgnoreCase) && Directory.EnumerateFileSystemEntries(next).Any())
                throw new IOException("目标缓存目录已有其他文件，请选择其他位置。");
            File.WriteAllText(marker, _key);
            var previous = Root;
            if (!string.Equals(next, previous, StringComparison.OrdinalIgnoreCase))
            {
                // Only copy managed files. Switch the active root after every copy succeeds.
                foreach (var file in Files())
                {
                    var dest = Path.Combine(next, Path.GetRelativePath(previous, file.FullName));
                    NoLinks(dest); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file.FullName, dest, true);
                }
                var old = Files().ToArray(); Root = next;
                foreach (var file in old) { try { file.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            }
            LimitBytes = Math.Clamp(limitMb, 0, 4096) * 1_000_000L; Trim();
            return Root;
        }
    }
    public byte[]? Read(string key)
    {
        lock (_gate)
        {
            try { var path = Resolve(key); if (!File.Exists(path)) return null; var bytes = File.ReadAllBytes(path); File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return bytes; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }
    public void Write(string key, byte[] bytes)
    {
        lock (_gate)
        {
            var path = Resolve(key);
            if (bytes.LongLength > LimitBytes || LimitBytes == 0) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path + ".tmp", bytes); File.Move(path + ".tmp", path, true); Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Cache failure must not fail a successful read/send. */ }
        }
    }
    public long SizeBytes() { lock (_gate) return Files().Sum(f => f.Length); }
    public void Remove(string key) { lock (_gate) { var path = Resolve(key); if (File.Exists(path)) File.Delete(path); } }
    public void Clear() { lock (_gate) { foreach (var file in Files()) file.Delete(); } }
    private void Trim()
    {
        var files = Files().OrderBy(f => f.LastWriteTimeUtc).ToArray(); var total = files.Sum(f => f.Length);
        foreach (var file in files)
        { if (total <= LimitBytes) break; var size = file.Length; file.Delete(); total -= size; }
    }
    private IEnumerable<FileInfo> Files()
    {
        NoLinks(Root);
        if (!Directory.Exists(Root)) return [];
        return new DirectoryInfo(Root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false })
            .Where(f => Managed(Path.GetRelativePath(Root, f.FullName))).ToArray();
    }
    private static bool Managed(string key) => Regex.IsMatch(key.Replace('\\', '/'), @"^(?:guest|[A-F0-9]{20}|images|subscriptions)/(?:[A-F0-9]{64}|[0-9]+-[0-9]+)\.(?:json|bin)(?:\.tmp)?$");
    private string Resolve(string key)
    {
        if (!Managed(key)) throw new ArgumentException("无效的缓存键。");
        var path = Path.GetFullPath(Path.Combine(Root, key)); NoLinks(path); return path;
    }
    public static void NoLinks(string path)
    {
        for (var part = Path.GetFullPath(path); !string.IsNullOrEmpty(part); part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("缓存或更新目录不能使用符号链接或目录联接。");
    }
}

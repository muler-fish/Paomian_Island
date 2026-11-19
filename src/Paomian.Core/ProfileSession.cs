using System.Security.Cryptography;
using System.Text;

namespace Paomian.Core;

/// <summary>在打开资料文件前选定并锁定目录；已有资料原地使用，不搬动缓存或改写路径。</summary>
public sealed class ProfileSession : IDisposable
{
    private readonly List<Mutex> _locks;
    public string Root { get; }
    private ProfileSession(string root, List<Mutex> locks) { Root = root; _locks = locks; }

    public static ProfileSession? TryOpen(string localAppData, string? dataDirectory = null, string? smokeDirectory = null)
    {
        var locks = new List<Mutex>();
        try
        {
            var root = dataDirectory ?? (smokeDirectory is null ? null : Path.Combine(smokeDirectory, "profile"));
            if (root is not null)
            {
                root = Path.GetFullPath(root);
                if (!LockPath(root, locks)) return null;
            }
            else
            {
                if (!Path.IsPathFullyQualified(localAppData)) throw new IOException("无法确定本机资料目录，请用 --data-dir 指定目录。");
                var legacy = Path.Combine(localAppData, AppIdentity.LegacyDataDirectoryName);
                var current = Path.Combine(localAppData, AppIdentity.DataDirectoryName);
                // 旧程序先取锁再建目录，必须先占旧锁，才能区分全新安装与正在首次启动的旧程序。
                if (!LockPath(legacy, locks)) return null;
                var hasLegacy = IsDirectory(legacy);
                var hasCurrent = IsDirectory(current);
                if (hasLegacy && hasCurrent)
                    throw new IOException($"发现两份默认资料目录。请用 --data-dir 明确选择「{legacy}」或「{current}」。未合并或更改任何资料。");
                root = hasLegacy ? legacy : current;
                if (!hasLegacy && !LockPath(current, locks)) return null;
            }
            var session = new ProfileSession(root, locks);
            locks = [];
            return session;
        }
        finally { foreach (var mutex in locks) Release(mutex); }
    }

    private static bool IsDirectory(string path)
    {
        // Directory.Exists 会把无权读取误报为不存在，进而悄悄打开一份空资料。
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != FileAttributes.Directory)
            throw new IOException("默认资料路径不是普通文件夹。未更改资料，请检查路径或用 --data-dir 指定目录。");
        return true;
    }

    private static bool LockPath(string fullPath, List<Mutex> locks)
    {
        // 1.0.2 对带/不带末尾分隔符的路径分别散列。两把旧锁都取，但不改变传给缓存的原路径。
        var plain = Path.TrimEndingDirectorySeparator(fullPath);
        return Take(plain) && (Path.EndsInDirectorySeparator(plain) || Take(plain + Path.DirectorySeparatorChar));

        bool Take(string path)
        {
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())))[..24];
            var mutex = new Mutex(true, AppIdentity.MutexPrefix + key, out var created);
            if (!created) { mutex.Dispose(); return false; }
            locks.Add(mutex); return true;
        }
    }

    private static void Release(Mutex mutex)
    {
        try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
        mutex.Dispose();
    }

    public void Dispose()
    {
        foreach (var mutex in _locks) Release(mutex);
        _locks.Clear();
    }
}

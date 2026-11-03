namespace Tide.Core;

public sealed partial class LocalStore
{
    public void ConfigureCache(UserState state)
    {
        state.CacheLimitMb = Math.Clamp(state.CacheLimitMb, 0, 4096);
        Cache.Configure(state.CacheParentPath, state.CacheLimitMb);
        // Move only old subscription snapshots; credentials and the saved subscription ID remain in state.json.
        var legacy = Path.Combine(Root, "subscription-cache");
        DiskCache.NoLinks(legacy);
        if (!Directory.Exists(legacy)) return;
        foreach (var file in new DirectoryInfo(legacy).EnumerateFiles("*.json"))
        {
            if (file.Name.Length != 69 || !file.Name[..64].All(Uri.IsHexDigit) || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            try { Cache.Write("subscriptions/" + file.Name, File.ReadAllBytes(file.FullName)); file.Delete(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

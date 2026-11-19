using System.Reflection;

namespace Paomian.Core;

/// <summary>现行品牌和跨版本兼容标识的单一来源。</summary>
public static class AppIdentity
{
    public static string DisplayName { get; } = typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "DisplayName").Value!;
    public static string EnglishName { get; } = typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "EnglishName").Value!;
    public static string WindowTitle => DisplayName;
    public const string ExecutableName = "Paomian Island.exe";
    public const string LegacyExecutableName = "Tide.exe";
    public static bool IsExecutableName(string name) => name is ExecutableName or LegacyExecutableName;
    public const string UserAgent = "PaomianIsland (+Windows; WPF)";
    // 新用户使用现行目录；旧目录、路径锁、DPAPI 描述和外置缓存名称属于兼容标识。
    public const string DataDirectoryName = "PaomianIsland";
    public const string LegacyDataDirectoryName = "TideIsland";
    public const string MutexPrefix = "Local\\TideIsland-";
    public const string CacheDirectoryPrefix = "TideCache-";
    public const string CacheOwnerMarker = ".tide-cache";
    public const string ProtectionDescription = "TideIsland cookie";
    public const string UpdaterExecutableName = "PaomianUpdater.exe";
    public const string LegacyUpdaterExecutableName = "TideUpdater.exe";

    // GitHub release assets cannot contain spaces, so the English name is hyphenated in package file names.
    public static string PackageName(string version) => $"{EnglishName.Replace(' ', '-')}-{version}-win-x64.zip";
    public static string ChinesePackageName(string version) => $"{DisplayName}-{version}-win-x64.zip";
    public static string LegacyPackageName(string version) => $"Tide-{version}-win-x64.zip";
}

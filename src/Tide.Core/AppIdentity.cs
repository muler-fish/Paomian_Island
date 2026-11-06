using System.Reflection;

namespace Tide.Core;

/// <summary>Public branding comes from Directory.Build.props; persisted technical identifiers remain stable.</summary>
public static class AppIdentity
{
    public static string DisplayName { get; } = typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "DisplayName").Value!;
    public static string EnglishName { get; } = typeof(AppIdentity).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "EnglishName").Value!;
    public static string WindowTitle => DisplayName + " · X岛";
    public const string ExecutableName = "Paomian Island.exe";
    public const string LegacyExecutableName = "Tide.exe";
    public static bool IsExecutableName(string name) => name is ExecutableName or LegacyExecutableName;
    // Persisted identities remain stable across the public rename.
    public const string DataDirectoryName = "TideIsland";
    public const string MutexPrefix = "Local\\TideIsland-";
    public const string ProtectionDescription = "TideIsland cookie";
    public const string UpdaterExecutableName = "TideUpdater.exe";
    // GitHub release assets cannot contain spaces, so the English name is hyphenated in package file names.
    public static string PackageName(string version) => $"{EnglishName.Replace(' ', '-')}-{version}-win-x64.zip";
    public static string ChinesePackageName(string version) => $"{DisplayName}-{version}-win-x64.zip";
    public static string LegacyPackageName(string version) => $"Tide-{version}-win-x64.zip";
}

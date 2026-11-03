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
    // Changing these would break existing shortcuts, update packages, profiles or parallel-instance protection.
    public const string ExecutableName = "Tide.exe";
    public const string DataDirectoryName = "TideIsland";
    public const string MutexPrefix = "Local\\TideIsland-";
    public const string ProtectionDescription = "TideIsland cookie";
    public const string UpdaterExecutableName = "TideUpdater.exe";
    public static string PackageName(string version) => $"{DisplayName}-{version}-win-x64.zip";
    public static string LegacyPackageName(string version) => $"Tide-{version}-win-x64.zip";
}

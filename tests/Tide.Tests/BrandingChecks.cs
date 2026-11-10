using System.Text.Json;
using Tide.Core;

internal static class BrandingChecks
{
    internal static void Run()
    {
        Require(AppIdentity.DisplayName == "泡面岛" && AppIdentity.WindowTitle == "泡面岛 · X 岛", "public name");
        Require(AppIdentity.EnglishName == "Paomian Island" && AppIdentity.ExecutableName == "Paomian Island.exe" && AppIdentity.LegacyExecutableName == "Tide.exe", "English brand and executable");
        Require(AppIdentity.DataDirectoryName == "TideIsland" && AppIdentity.MutexPrefix == "Local\\TideIsland-" && AppIdentity.ProtectionDescription == "TideIsland cookie", "legacy data identity");
        object Asset(string name, bool escaped) => new { name, browser_download_url = ReleaseUpdater.Repository + "/releases/download/v9.0.0/" + (escaped ? Uri.EscapeDataString(name) : name), digest = "sha256:" + new string('a', 64), size = 100 };
        byte[] Release(params object[] assets) => JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "v9.0.0", draft = false, prerelease = false, assets });
        Require(AppIdentity.PackageName("9.0.0") == "Paomian-Island-9.0.0-win-x64.zip", "package name without spaces");
        var branded = AppIdentity.PackageName("9.0.0"); var legacy = AppIdentity.LegacyPackageName("9.0.0");
        foreach (var escaped in new[] { false, true })
        {
            var item = ReleaseUpdater.ParseRelease(Release(Asset(legacy, false), Asset(branded, escaped)), "0.2.2-preview.8");
            Require(item is not null && Uri.UnescapeDataString(item.Download.AbsolutePath).EndsWith(branded), "prefer Chinese package including escaped URL");
        }
        Require(ReleaseUpdater.ParseRelease(Release(Asset(legacy, false)), "0.2.2-preview.8") is not null, "legacy package fallback");
        Require(ReleaseUpdater.ParseRelease(Release(Asset(AppIdentity.ChinesePackageName("9.0.0"), true)), "0.2.2-preview.8") is not null, "Chinese package fallback");
        Require(ReleaseUpdater.ParseRelease(Release(Asset(legacy, false)), "9.0.0") is null, "no downgrade");
        object Hosted(string repository) => new { name = branded, browser_download_url = repository + "/releases/download/v9.0.0/" + Uri.EscapeDataString(branded), digest = "sha256:" + new string('a', 64), size = 100 };
        Require(ReleaseUpdater.ParseRelease(Release(Hosted(ReleaseUpdater.LegacyRepository)), "0.3.1") is not null, "pre-rename repository assets");
        try { ReleaseUpdater.ParseRelease(Release(Hosted("https://github.com/someone/Paomian_Island")), "0.3.1"); throw new Exception("foreign repository accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("PASS public English name/executable, stable profile/crypto/mutex identifiers, English/Chinese/legacy update packages, renamed repository");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}

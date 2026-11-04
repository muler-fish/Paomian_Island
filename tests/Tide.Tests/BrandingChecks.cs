using System.Text.Json;
using Tide.Core;

internal static class BrandingChecks
{
    internal static void Run()
    {
        Require(AppIdentity.DisplayName == "泡面岛" && AppIdentity.WindowTitle == "泡面岛 · X岛", "public name");
        Require(AppIdentity.EnglishName == "Paomian Island" && AppIdentity.ExecutableName == "Paomian Island.exe" && AppIdentity.LegacyExecutableName == "Tide.exe", "English brand and executable");
        Require(AppIdentity.DataDirectoryName == "TideIsland" && AppIdentity.MutexPrefix == "Local\\TideIsland-" && AppIdentity.ProtectionDescription == "TideIsland cookie", "legacy data identity");
        object Asset(string name, bool escaped) => new { name, browser_download_url = ReleaseUpdater.Repository + "/releases/download/v9.0.0/" + (escaped ? Uri.EscapeDataString(name) : name), digest = "sha256:" + new string('a', 64), size = 100 };
        byte[] Release(params object[] assets) => JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "v9.0.0", draft = false, prerelease = false, assets });
        var branded = AppIdentity.PackageName("9.0.0"); var legacy = AppIdentity.LegacyPackageName("9.0.0");
        foreach (var escaped in new[] { false, true })
        {
            var item = ReleaseUpdater.ParseRelease(Release(Asset(legacy, false), Asset(branded, escaped)), "0.2.2-preview.8");
            Require(item is not null && Uri.UnescapeDataString(item.Download.AbsolutePath).EndsWith(branded), "prefer Chinese package including escaped URL");
        }
        Require(ReleaseUpdater.ParseRelease(Release(Asset(legacy, false)), "0.2.2-preview.8") is not null, "legacy package fallback");
        Require(ReleaseUpdater.ParseRelease(Release(Asset(AppIdentity.ChinesePackageName("9.0.0"), true)), "0.2.2-preview.8") is not null, "Chinese package fallback");
        Require(ReleaseUpdater.ParseRelease(Release(Asset(legacy, false)), "9.0.0") is null, "no downgrade");
        Console.WriteLine("PASS public English name/executable, stable profile/crypto/mutex identifiers, English/Chinese/legacy update packages");
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}

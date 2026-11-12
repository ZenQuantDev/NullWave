using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Helpers.Localization;
using Xunit;

namespace NullWave.Tests;

public class SettingsLocalizationTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Settings_tabs_and_playlist_result_count_have_Russian_translations_for_every_used_key()
    {
        var viewPaths = new[]
        {
            "Views/Settings/DownloadsTab.axaml",
            "Views/Settings/IntegrationsTab.axaml",
            "Views/Settings/MaintenanceTab.axaml",
            "Views/Settings/GeneralTab.axaml",
            "Views/Dialogs/PackageManagerInstallDialog.axaml",
            "Views/Dialogs/ConfirmDialog.axaml"
        };
        var referencedKeys = viewPaths
            .Select(path => File.ReadAllText(Path.Combine(RepoRoot, path)))
            .SelectMany(text => Regex.Matches(text, @"\{loc:Loc\s+([A-Za-z0-9_]+)")
                .Select(match => match.Groups[1].Value));
        var requiredPrefixes = new[]
        {
            "Settings_Downloads_",
            "Settings_Integrations_",
            "Settings_Maintenance_",
            "Settings_General_Experimental_",
            "Settings_Updates_InstallConsent_",
            "Dialog_Confirm"
        };
        var dynamicallyUsedKeys = Locales.English.Keys.Where(key =>
            requiredPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal)));
        var requiredKeys = referencedKeys
            .Concat(dynamicallyUsedKeys)
            .Append("Playlist_RESULTCount_Format")
            .Distinct()
            .ToArray();

        var missingEnglish = requiredKeys.Where(key => !Locales.English.ContainsKey(key)).ToArray();
        var missingRussian = requiredKeys.Where(key => !Locales.Russian.ContainsKey(key)).ToArray();

        Assert.True(missingEnglish.Length == 0, $"Missing English localization keys: {string.Join(", ", missingEnglish)}");
        Assert.True(missingRussian.Length == 0, $"Missing Russian localization keys: {string.Join(", ", missingRussian)}");
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "NullWave.csproj")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException("Could not find repo root.");
    }
}

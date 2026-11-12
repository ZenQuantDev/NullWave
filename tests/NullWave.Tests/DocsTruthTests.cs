using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class DocsTruthTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    
    // Cache file contents to avoid reading from disk on every test
    private static readonly string PrivacyMd = File.ReadAllText(Path.Combine(RepoRoot, "PRIVACY.md"));
    private static readonly string ReadmeMd = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
    private static readonly string ChangelogMd = File.ReadAllText(Path.Combine(RepoRoot, "CHANGELOG.md"));
    private static readonly string DisclaimerMd = File.ReadAllText(Path.Combine(RepoRoot, "DISCLAIMER.md"));
    private static readonly string RoadmapMd = File.ReadAllText(Path.Combine(RepoRoot, "ROADMAP.md"));
    private static readonly string Csproj = File.ReadAllText(Path.Combine(RepoRoot, "NullWave.csproj"));
    private static readonly string PackagesProps = File.ReadAllText(Path.Combine(RepoRoot, "Directory.Packages.props"));

    [Fact]
    public void Privacy_md_lists_all_network_hosts()
    {
        var hosts = NetworkHosts.All.Select(h => h.Host).ToList();
        foreach (var host in hosts)
        {
            Assert.Contains(host, PrivacyMd, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Readme_version_matches_csproj()
    {
        var csprojVersion = Regex.Match(Csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Contains(csprojVersion, ReadmeMd);
    }

    [Fact]
    public void Changelog_top_version_matches_csproj()
    {
        var csprojVersion = Regex.Match(Csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;
        var changelogVersionMatch = Regex.Match(ChangelogMd, @"^## \[([^\]]+)\]", RegexOptions.Multiline);
        Assert.True(changelogVersionMatch.Success, "Could not find a version header in CHANGELOG.md");
        
        var changelogVersion = changelogVersionMatch.Groups[1].Value;
        Assert.Equal(csprojVersion, changelogVersion);
    }

    [Fact]
    public void Readme_requirements_list_probed_tools()
    {
        // StartupDiagnosticsService checks these tools
        var requiredTools = new[] { "yt-dlp", "ffmpeg", "vlc", "libvlc" };
        foreach (var tool in requiredTools)
        {
            Assert.Contains(tool, ReadmeMd, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Readme_requirements_mention_js_runtimes()
    {
        // DownloadService probes for Deno and Node
        Assert.Contains("Deno", ReadmeMd, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Node", ReadmeMd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Disclaimer_does_not_claim_spotify_web_api()
    {
        // Master Guide Item 4: "Remove 'Spotify Web API' (you scrape OG tags, you don't use the API)"
        Assert.DoesNotContain("Spotify Web API", DisclaimerMd, StringComparison.OrdinalIgnoreCase);
    }
    
    [Fact]
    public void Disclaimer_does_not_claim_soundcloud_api()
    {
        // We use yt-dlp for SoundCloud, not an official API
        Assert.DoesNotContain("SoundCloud API", DisclaimerMd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_does_not_have_duplicate_headers()
    {
        var duplicateHeaders = Regex.Matches(ReadmeMd, @"^## Tech Stack\s*$", RegexOptions.Multiline);
        Assert.True(duplicateHeaders.Count <= 1, "README.md contains duplicate '## Tech Stack' headers.");
    }

    [Fact]
    public void Roadmap_dependency_versions_match_packages_props()
    {
        var packagesToCheck = new[] { "Avalonia", "LibVLCSharp" };
        foreach (var pkg in packagesToCheck)
        {
            var propsMatch = Regex.Match(PackagesProps, $@"<PackageVersion Include=\""{pkg}\"" Version=\""(.*?)\""", RegexOptions.IgnoreCase);
            Assert.True(propsMatch.Success, $"Could not find {pkg} in Directory.Packages.props");
            var propsVersion = propsMatch.Groups[1].Value;

            Assert.Contains(propsVersion, RoadmapMd);
        }
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
        throw new InvalidOperationException("Could not find repo root. Ensure tests are run from the build output directory.");
    }
}
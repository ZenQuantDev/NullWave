using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class DocsTruthTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Fact]
    public void Privacy_md_lists_all_network_hosts()
    {
        var hosts = NetworkHosts.All.Select(h => h.Host).ToList();
        var privacyPath = Path.Combine(RepoRoot, "PRIVACY.md");
        var privacy = File.ReadAllText(privacyPath);

        foreach (var host in hosts)
        {
            Assert.Contains(host, privacy, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Readme_version_matches_csproj()
    {
        var readmePath = Path.Combine(RepoRoot, "README.md");
        var csprojPath = Path.Combine(RepoRoot, "NullWave.csproj");
        
        var readme = File.ReadAllText(readmePath);
        var csproj = File.ReadAllText(csprojPath);

        var csprojVersion = Regex.Match(csproj, @"<Version>([^<]+)</Version>").Groups[1].Value;
        Assert.Contains(csprojVersion, readme);
    }

    [Fact]
    public void Readme_requirements_match_startup_diagnostics()
    {
        var readmePath = Path.Combine(RepoRoot, "README.md");
        var readme = File.ReadAllText(readmePath);

        // These are the tools StartupDiagnosticsService checks
        var requiredTools = new[] { "yt-dlp", "ffmpeg", "vlc" };
        foreach (var tool in requiredTools)
        {
            Assert.Contains(tool, readme, StringComparison.OrdinalIgnoreCase);
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
        throw new InvalidOperationException("Could not find repo root");
    }
}
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class DependencyStatusTests
{
    private static DependencyInfo Info(string have, string latest, bool installed = true) =>
        new() { Name = "yt-dlp", IsInstalled = installed, InstalledVersion = have, LatestVersion = latest };

    [Theory]
    [InlineData("2026.10.26", "2026.10.26", DependencyState.UpToDate)]
    [InlineData("2026.08.19", "2026.10.26", DependencyState.UpdateAvailable)]
    [InlineData("2026.10.26.123456", "2026.10.26", DependencyState.UpToDate)]
    [InlineData("2026.10.26", "unknown", DependencyState.Ready)]
    [InlineData("weird", "weird", DependencyState.UpToDate)]
    [InlineData("weird", "other", DependencyState.UpdateAvailable)]
    public void ClassifyYtDlp_maps_versions(string have, string latest, DependencyState expected)
        => Assert.Equal(expected, DependencyStatus.ClassifyYtDlp(Info(have, latest)));

    [Fact]
    public void ClassifyYtDlp_missing_when_not_installed()
        => Assert.Equal(DependencyState.Missing, DependencyStatus.ClassifyYtDlp(Info("", "", installed: false)));

    [Fact]
    public void Text_formats_update_available()
        => Assert.Equal("1.0 → 2.0 available",
            DependencyStatus.Text(DependencyState.UpdateAvailable, "1.0", "2.0",
                key => key == "Settings_Updates_Dep_UpdateAvailable" ? "{0} → {1} available" : key));
}
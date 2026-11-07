using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class UpdateVersioningTests
{
    [Theory]
    [InlineData("0.6.5-beta.1+46a6e0a", "0.6.5-beta.1", true)]
    [InlineData("0.6.5+46a6e0a", "0.6.5", false)]
    [InlineData("", "0.0.0", false)]
    [InlineData(null, "0.0.0", false)]
    public void Display_and_prerelease(string? info, string display, bool pre)
    {
        Assert.Equal(display, UpdateVersioning.Display(info));
        Assert.Equal(pre, UpdateVersioning.IsPreRelease(info));
    }

    [Fact] public void Failed_check_never_says_up_to_date() =>
        Assert.DoesNotContain("up to date", UpdateVersioning.StatusText(new UpdateCheckResult { Failed = true }));

    [Fact] public void Dev_build_is_reported() =>
        Assert.Contains("Dev build", UpdateVersioning.StatusText(new UpdateCheckResult { NotInstalled = true }));

    [Fact] public void Available_shows_target_version() =>
        Assert.Contains("0.6.6", UpdateVersioning.StatusText(new UpdateCheckResult { IsUpdateAvailable = true, LatestVersion = "0.6.6" }));
}
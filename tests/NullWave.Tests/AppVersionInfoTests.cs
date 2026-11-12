using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class AppVersionInfoTests
{
    [Theory]
    [InlineData("0.6.5-beta.2", "0.6.5", true)]
    [InlineData("0.6.5", "0.6.4", true)]
    [InlineData("0.6.5", "0.6.6", false)]
    [InlineData("1.0.0", "0.99.99", true)]
    public void Satisfies_compares_numeric_version_and_ignores_prerelease(
        string hostVersion, string minVersion, bool expected)
    {
        Assert.Equal(expected, AppVersionInfo.Satisfies(hostVersion, minVersion));
    }
}

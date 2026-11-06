using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class TagWriteModeTests
{
    [Fact]
    public void Default_is_DownloadsOnly()
    {
        var prefs = new Preferences();
        Assert.Equal(TagWriteMode.DownloadsOnly, prefs.TagWriteMode);
    }
    
    [Theory]
    [InlineData(TagWriteMode.Never)]
    [InlineData(TagWriteMode.DownloadsOnly)]
    [InlineData(TagWriteMode.Always)]
    public void All_modes_are_valid_enum_values(TagWriteMode mode)
    {
        Assert.True(Enum.IsDefined(typeof(TagWriteMode), mode));
    }
}
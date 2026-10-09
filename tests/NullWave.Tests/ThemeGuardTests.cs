using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class ThemeGuardTests
{
    [Fact]
    public void Skips_remix_when_same_accent_same_mode_and_no_rewrite()
        => Assert.False(ThemeGuard.ShouldRemixAccent("Purple", "Dark", "Purple", "Dark", paletteWasRewritten: false));

    [Fact]
    public void Remixes_when_accent_changes()
        => Assert.True(ThemeGuard.ShouldRemixAccent("Purple", "Dark", "Sky", "Dark", paletteWasRewritten: false));

    [Fact]
    public void Remixes_when_mode_changes()
        => Assert.True(ThemeGuard.ShouldRemixAccent("Purple", "Dark", "Purple", "TrueBlack", paletteWasRewritten: false));

    [Fact]
    public void Forces_remix_when_palette_was_rewritten()
        => Assert.True(ThemeGuard.ShouldRemixAccent("Purple", "Dark", "Purple", "Dark", paletteWasRewritten: true));

    [Fact]
    public void Remixes_on_first_run_when_applied_is_null()
        => Assert.True(ThemeGuard.ShouldRemixAccent(null, null, "Oxeye Daisy", "Dark", paletteWasRewritten: false));
}
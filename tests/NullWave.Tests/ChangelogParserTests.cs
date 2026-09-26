using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class ChangelogParserTests
{
    [Fact]
    public void ParseSections_SplitsHeadersAndBullets()
    {
        var md = "### Added\n- **A**: thing one\n- `B` thing two\n### Fixed\n- C thing three\n";
        var sections = ChangelogParser.ParseSections(md);

        Assert.Equal(2, sections.Count);
        Assert.Equal("Added", sections[0].Header);
        Assert.Equal(2, sections[0].Bullets.Count);
        Assert.Equal("**A**: thing one", sections[0].Bullets[0]);
        Assert.Equal("Fixed", sections[1].Header);
        Assert.Single(sections[1].Bullets);
    }

    [Fact]
    public void ParseSections_StarBulletsAreAccepted()
    {
        var sections = ChangelogParser.ParseSections("### Changed\n* star bullet\n");
        Assert.Single(sections[0].Bullets);
        Assert.Equal("star bullet", sections[0].Bullets[0]);
    }

    [Fact]
    public void ParseSections_IndentedSubBulletsAreDroppedByDesign()
    {
        // Documented decision: the popup is a summary; nested details
        // stay in the full changelog linked from the About tab.
        var sections = ChangelogParser.ParseSections("### Added\n- top\n    - nested\n");
        Assert.Single(sections[0].Bullets);
    }

    [Fact]
    public void ParseSections_EmptyInputGivesNoSections()
    {
        Assert.Empty(ChangelogParser.ParseSections(""));
        Assert.Empty(ChangelogParser.ParseSections("   \n  \n"));
    }

    [Fact]
    public void ParseSections_BulletsBeforeAnyHeaderAreIgnored()
    {
        var sections = ChangelogParser.ParseSections("- orphan\n### Added\n- real\n");
        Assert.Single(sections);
        Assert.Single(sections[0].Bullets);
    }
}
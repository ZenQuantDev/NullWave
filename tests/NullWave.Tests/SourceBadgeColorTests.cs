using Avalonia.Media;
using NullWave.Helpers.Converters;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class SourceBadgeColorTests
{
    [Theory]
    [InlineData(TrackSource.YouTube)]
    [InlineData(TrackSource.SoundCloud)]
    [InlineData(TrackSource.Spotify)]
    [InlineData(TrackSource.LastFm)]
    [InlineData(TrackSource.Local)]
    [InlineData(TrackSource.Unknown)]
    public void Every_source_has_a_translucent_fill_and_a_solid_text_color(TrackSource source)
    {
        foreach (var light in new[] { false, true })
        {
            var (bg, fg) = BadgeColors.For(source, light);
            Assert.InRange(bg.A, 0x1A, 0x29);   // stays a tint, never a solid pill
            Assert.Equal(0xFF, fg.A);            // text always fully opaque
            Assert.NotEqual(bg.ToUInt32(), fg.ToUInt32());
        }
    }

    [Fact]
    public void Dark_theme_text_is_light_and_light_theme_text_is_dark()
    {
        static double Luma(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
        Assert.True(Luma(BadgeColors.For(TrackSource.YouTube, light: false).Fg) > 128);
        Assert.True(Luma(BadgeColors.For(TrackSource.YouTube, light: true).Fg) < 128);
    }

    [Fact]
    public void Strings_and_nulls_coerce_to_unknown_without_throwing()
    {
        Assert.Equal(TrackSource.YouTube, BadgeColors.Coerce("YouTube"));
        Assert.Equal(TrackSource.YouTube, BadgeColors.Coerce("youtube"));
        Assert.Equal(TrackSource.Local, BadgeColors.Coerce(TrackSource.Local));
        Assert.Equal(TrackSource.Unknown, BadgeColors.Coerce(null));
        Assert.Equal(TrackSource.Unknown, BadgeColors.Coerce("nope"));
    }
}
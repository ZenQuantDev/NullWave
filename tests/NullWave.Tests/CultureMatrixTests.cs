using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class CultureMatrixTests
{
    public static IEnumerable<object[]> Cultures =>
        new[] { "en-US", "ru-RU", "tr-TR", "de-DE", "ja-JP", "ka-GE" }.Select(c => new object[] { c });

    private static void Under(string name, Action act)
    {
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(name); act(); }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Theory, MemberData(nameof(Cultures))]
    public void SearchKey_is_identical_in_every_culture(string culture) => Under(culture, () =>
    {
        Assert.Equal("мед", TrackNormalizer.SearchKey("Мёд"));
        Assert.Equal("мой", TrackNormalizer.SearchKey("Мой"));          // й is kept
        Assert.Equal("beyonce", TrackNormalizer.SearchKey("Beyoncé"));
        Assert.Equal("muller", TrackNormalizer.SearchKey("MÜLLER"));
        Assert.Equal("title", TrackNormalizer.SearchKey("TITLE"));      // tr-TR: no dotless ı
        Assert.Equal("cafe", TrackNormalizer.SearchKey("Cafe\u0301"));  // decomposed input
    });

    [Theory, MemberData(nameof(Cultures))]
    public void Identity_keys_separate_different_non_latin_tracks(string culture) => Under(culture, () =>
    {
        var a = TrackNormalizer.IdentityKey("Группа крови", "Кино");
        var b = TrackNormalizer.IdentityKey("Звезда по имени Солнце", "Кино");
        Assert.NotEqual(a, b);
        Assert.Equal(TrackNormalizer.IdentityKey("КИНО", "кино"), TrackNormalizer.IdentityKey("Кино", "Кино"));
        Assert.NotEqual("ta:|", TrackNormalizer.IdentityKey("夜空の歌", "元気バンド"));
        Assert.NotEqual("ta:|", TrackNormalizer.IdentityKey("კინო", "ქართული"));
    });

    [Fact]
    public void LooselyMatch_now_treats_unknown_cyrillic_words_like_unknown_latin_words()
    {
        // Before the tokenizer fix, Cyrillic tokens were empty, so this wrongly matched.
        Assert.False(TrackNormalizer.LooselyMatch("Группа крови", "Группа крови концерт в Москве", "Кино", "Кино"));
        Assert.True(TrackNormalizer.LooselyMatch("Группа крови", "Группа крови", "Кино", "Кино"));
    }
}
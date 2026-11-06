using System;
using System.Linq;
using System.Text;
using NullWave.Helpers;
using Xunit;

namespace NullWave.Tests;

public class FileNamerTests
{
    [Fact]
    public void Long_cyrillic_title_fits_255_bytes_and_keeps_suffix()
    {
        var name = FileNamer.Generate(
            "Артист",
            new string('ж', 400),
            "1e7de1ff-0000-0000-0000-000000000000",
            ".mp3");

        Assert.True(Encoding.UTF8.GetByteCount(name) <= 255,
            $"Generated name is {Encoding.UTF8.GetByteCount(name)} bytes: {name}");
        Assert.EndsWith(" [nw-1e7de1ff].mp3", name);
    }

    [Fact]
    public void Truncation_never_leaves_a_lone_surrogate()
    {
        for (int n = 0; n < 6; n++)
        {
            var title = new string('a', n) + string.Concat(Enumerable.Repeat("😀", 200));
            var name = FileNamer.Generate("A", title, Guid.NewGuid().ToString(), ".mp3");

            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsHighSurrogate(name[i]))
                {
                    Assert.True(i + 1 < name.Length && char.IsLowSurrogate(name[i + 1]),
                        $"Lone high surrogate at index {i} in: {name}");
                    i++;
                }
                else
                {
                    Assert.False(char.IsLowSurrogate(name[i]),
                        $"Lone low surrogate at index {i} in: {name}");
                }
            }
        }
    }

    [Fact]
    public void Output_has_no_FAT_invalid_chars()
    {
        var name = FileNamer.Generate(
            "AC/DC",
            "Why: me? *\"x\"|<y>",
            Guid.NewGuid().ToString(),
            ".mp3");

        var invalid = new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        foreach (var c in invalid)
        {
            Assert.DoesNotContain(c.ToString(), name);
        }
    }

    [Fact]
    public void Guid_id_is_used_as_suffix_prefix()
    {
        var id = Guid.Parse("1e7de1ff-0000-0000-0000-000000000000");
        var name = FileNamer.Generate("Artist", "Title", id.ToString(), ".mp3");
        Assert.Contains("[nw-1e7de1ff]", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_title_and_artist_fall_back_to_unknown()
    {
        var name = FileNamer.Generate("", "", Guid.NewGuid().ToString(), ".flac");
        Assert.StartsWith("Unknown - Unknown [nw-", name);
    }
}
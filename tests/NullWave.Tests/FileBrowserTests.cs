using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NullWave.Models.FileSystem;
using NullWave.Services.FileSystem;
using NullWave.ViewModels;
using Xunit;

namespace NullWave.Tests;

public class NaturalComparerTests
{
    [Theory]
    [InlineData("Track 2", "Track 10")]
    [InlineData("track 2", "TRACK 10")]
    [InlineData("01 Song", "2 Song")]
    public void Numeric_segments_order_naturally(string a, string b)
        => Assert.True(NaturalComparer.Instance.Compare(a, b) < 0);

    [Fact]
    public void Equal_strings_compare_zero()
        => Assert.Equal(0, NaturalComparer.Instance.Compare("Same", "Same"));
}

public class FileBrowserPathTests
{
    [Fact]
    public void Normalize_expands_tilde_to_profile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(home, FileBrowserService.NormalizeEnteredPath("~"));
    }

    [Fact]
    public void Normalize_file_returns_its_parent()
    {
        var file = Path.Combine(Path.GetTempPath(), "nullwave_probe.txt");
        File.WriteAllText(file, "x");
        try
        {
            Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(file)),
                         FileBrowserService.NormalizeEnteredPath(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Normalize_unusable_text_returns_null()
    {
        Assert.Null(FileBrowserService.NormalizeEnteredPath(""));
        Assert.Null(FileBrowserService.NormalizeEnteredPath("   "));
        Assert.Null(FileBrowserService.NormalizeEnteredPath(@"Z:\nullwave\does\not\exist"));
    }
}

public class StableIdTests
{
    [Fact]
    public void Same_path_produces_same_id()
        => Assert.Equal(FileBrowserViewModel.StableIdForPath(@"C:\Music\a.mp3"),
                        FileBrowserViewModel.StableIdForPath(@"C:\Music\a.mp3"));

    [Fact]
    public void Separators_and_case_are_ignored()
        => Assert.Equal(FileBrowserViewModel.StableIdForPath(@"C:\Music\a.mp3"),
                        FileBrowserViewModel.StableIdForPath("c:/music/a.mp3/".TrimEnd('/')));

    [Fact]
    public void Different_paths_produce_different_ids()
        => Assert.NotEqual(FileBrowserViewModel.StableIdForPath(@"C:\Music\a.mp3"),
                           FileBrowserViewModel.StableIdForPath(@"C:\Music\b.mp3"));
}

public class DriveDiffTests
{
    private static FileSystemNode Drive(string root) => new()
    {
        Path = root, Name = root, Type = FileSystemNodeType.Drive
    };

    [Fact]
    public void Detects_added_and_removed_volumes()
    {
        var current = new[] { Drive(@"C:\"), Drive(@"E:\") };
        var fresh = new[] { Drive(@"C:\"), Drive(@"F:\") };

        var (added, removed) = FileBrowserViewModel.ComputeDriveDiff(current, fresh);

        Assert.Equal(@"F:\", Assert.Single(added).Path);
        Assert.Equal(@"E:\", Assert.Single(removed));
    }

    [Fact]
    public void Unchanged_sets_produce_empty_diff()
    {
        var (added, removed) = FileBrowserViewModel.ComputeDriveDiff(
            new[] { Drive(@"C:\") }, new[] { Drive(@"C:\") });
        Assert.Empty(added);
        Assert.Empty(removed);
    }
}

public class LocaleSourceDuplicateTests
{
    [Theory]
    [InlineData("Helpers/Localization/Locales.cs")]
    [InlineData("Helpers/Localization/Locales.ru.cs")]
    public void Locale_sources_contain_no_duplicate_keys(string relative)
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relative));
        var keys = Regex.Matches(File.ReadAllText(path), @"\[""([A-Za-z0-9_]+)""\]\s*=")
                        .Select(m => m.Groups[1].Value).ToList();
        var dupes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void Natural_compare_survives_huge_numbers()
        => Assert.True(NaturalComparer.Instance.Compare("a" + new string('9', 30), "a1" + new string('9', 30)) < 0);

    [Fact]
    public void Natural_compare_orders_numbered_tracks()
    {
        var names = new[] { "10 - Outro", "02 - Song", "01 - Intro" };
        Assert.Equal(new[] { "01 - Intro", "02 - Song", "10 - Outro" },
            names.OrderBy(n => n, NaturalComparer.Instance).ToArray());
    }

    [Fact]
    public void Duration_over_an_hour_keeps_hours()
        => Assert.Equal("1:05:30", FileBrowserService.FormatDuration(TimeSpan.FromSeconds(3930)));

    [Fact]
    public void ParentOf_root_goes_to_this_pc()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(string.Empty, FileBrowserViewModel.ParentOf(@"C:\"));
        Assert.Equal(@"C:\", FileBrowserViewModel.ParentOf(@"C:\Users"));
    }
}
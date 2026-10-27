using System;
using System.IO;
using System.Linq;
using System.Text;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class M3uWriterTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "nw-m3u");
    private static string P(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    private static Track Song(string title, string artist, string path, int secs = 200) =>
        new() { Title = title, Artist = artist, FilePath = path, Duration = TimeSpan.FromSeconds(secs) };

    [Fact]
    public void Writer_produces_extm3u_extinf_and_relative_forward_slash_paths()
    {
        var r = M3uWriter.Build(new[] { Song("Звезда", "Кино", P("songs", "a.mp3")) },
            P("lists", "mix.m3u8"), new M3uWriteOptions(PlaylistName: "Mix"), _ => true);
        Assert.Equal("#EXTM3U\n#PLAYLIST:Mix\n#EXTINF:200,Кино - Звезда\n../songs/a.mp3\n", r.Text);
        Assert.DoesNotContain("\r", r.Text);
        Assert.Equal((1, 0), (r.Written, r.SkippedMissing));
    }

    [Fact]
    public void Writer_skips_missing_files_keeps_streams_and_flattens_line_breaks()
    {
        var radio = new Track { Title = "Groove", Artist = "Unknown", MediaType = MediaType.Radio, Url = "https://ice.somafm.com/groovesalad" };
        var broken = Song("Two\r\nLines", "Band", P("songs", "b.mp3"), secs: 0);
        var missing = Song("Gone", "Band", P("songs", "gone.mp3"));
        var r = M3uWriter.Build(new[] { radio, broken, missing }, P("mix.m3u8"), null, p => !p.EndsWith("gone.mp3"));
        Assert.Equal((2, 1), (r.Written, r.SkippedMissing));
        Assert.Contains("https://ice.somafm.com/groovesalad\n", r.Text);
        Assert.Contains("#EXTINF:-1,Band - Two Lines\n", r.Text);
    }

    [Fact]
    public void Writer_can_use_absolute_paths()
    {
        var r = M3uWriter.Build(new[] { Song("A", "B", P("songs", "a.mp3")) }, P("l", "m.m3u8"),
            new M3uWriteOptions(RelativePaths: false), _ => true);
        Assert.Contains(P("songs", "a.mp3").Replace('\\', '/') + "\n", r.Text);
    }

    [Fact]
    public void Round_trip_export_then_parse_gives_the_same_files_and_titles()
    {
        var tracks = new[] { Song("Звезда", "Кино", P("songs", "a.mp3")), Song("Two", "Band", P("songs", "sub", "b.mp3"), 61) };
        var m3u = P("lists", "mix.m3u8");
        var text = M3uWriter.Build(tracks, m3u, null, _ => true).Text;
        var parsed = M3uParser.ParseBytes(M3uWriter.Utf8NoBom.GetBytes(text), Path.GetDirectoryName(m3u));
        Assert.True(parsed.Ok);
        Assert.Equal(tracks.Select(t => Path.GetFullPath(t.FilePath!)), parsed.Entries.Select(e => e.Target));
        Assert.Equal(new[] { "Звезда", "Two" }, parsed.Entries.Select(e => e.Title));
        Assert.Equal(new int?[] { 200, 61 }, parsed.Entries.Select(e => e.DurationSeconds));
    }
}
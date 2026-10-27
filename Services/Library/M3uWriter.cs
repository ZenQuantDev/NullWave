using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NullWave.Helpers;
using NullWave.Models;

namespace NullWave.Services;

public sealed record M3uWriteOptions(bool RelativePaths = true, string? PlaylistName = null);
public sealed record M3uWriteResult(string Text, int Written, int SkippedMissing);

public static class M3uWriter
{
    public static readonly UTF8Encoding Utf8NoBom = new(false);

    public static M3uWriteResult Build(IEnumerable<Track> tracks, string m3uFilePath, M3uWriteOptions? options = null,
                                       Func<string, bool>? fileExists = null)
    {
        options ??= new M3uWriteOptions();
        fileExists ??= File.Exists;
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(m3uFilePath)) ?? string.Empty;

        var sb = new StringBuilder("#EXTM3U\n");
        if (!string.IsNullOrWhiteSpace(options.PlaylistName))
            sb.Append("#PLAYLIST:").Append(OneLine(options.PlaylistName)).Append('\n');

        int written = 0, skipped = 0;
        foreach (var t in tracks)
        {
            string? target;
            if (t.MediaType == MediaType.Radio && !string.IsNullOrWhiteSpace(t.Url))
                target = t.Url!.Trim();
            else if (!string.IsNullOrEmpty(t.FilePath) && fileExists(t.FilePath))
                target = FormatPath(t.FilePath, baseDir, options.RelativePaths);
            else { skipped++; continue; }

            var seconds = t.Duration.TotalSeconds > 0 ? (int)Math.Round(t.Duration.TotalSeconds) : -1;
            
            // TrackNormalizer is static in NullWave
            var title = OneLine(TrackNormalizer.CleanTitle(t.Title));
            var artist = TrackNormalizer.IsPlaceholderArtist(t.Artist) ? "" : OneLine(TrackNormalizer.CleanArtist(t.Artist));
            var display = artist.Length > 0 ? $"{artist} - {title}" : title;

            sb.Append("#EXTINF:").Append(seconds).Append(',').Append(display).Append('\n');
            sb.Append(target).Append('\n');
            written++;
        }
        return new M3uWriteResult(sb.ToString(), written, skipped);
    }

    public static void WriteFile(string path, string text) => File.WriteAllText(path, text, Utf8NoBom);

    private static string FormatPath(string filePath, string baseDir, bool relative)
    {
        var full = Path.GetFullPath(filePath);
        var chosen = relative && baseDir.Length > 0 ? Path.GetRelativePath(baseDir, full) : full;
        return chosen.Replace('\\', '/');
    }

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();
}
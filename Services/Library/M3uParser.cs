using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NullWave.Services;

public sealed record M3uEntry(string Target, bool IsUrl, string? Title, string? Artist, int? DurationSeconds);
public sealed record M3uParseResult(IReadOnlyList<M3uEntry> Entries, string? PlaylistName, string? Error)
{
    public bool Ok => Error == null;
}

public static class M3uParser
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxEntries = 20_000;
    private static readonly Regex UrlScheme = new(@"^[a-zA-Z][a-zA-Z0-9+.\-]+://", RegexOptions.Compiled);

    public static M3uParseResult ParseBytes(byte[] data, string? baseDir)
    {
        if (data.Length > MaxBytes) return Fail("Playlist file is too large.");
        return Parse(Decode(data), baseDir);
    }

    public static string Decode(byte[] d)
    {
        if (d.Length >= 3 && d[0] == 0xEF && d[1] == 0xBB && d[2] == 0xBF) return Encoding.UTF8.GetString(d, 3, d.Length - 3);
        if (d.Length >= 2 && d[0] == 0xFF && d[1] == 0xFE) return Encoding.Unicode.GetString(d, 2, d.Length - 2);
        if (d.Length >= 2 && d[0] == 0xFE && d[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(d, 2, d.Length - 2);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(d); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(d); }
    }

    public static M3uParseResult Parse(string text, string? baseDir)
    {
        var entries = new List<M3uEntry>();
        string? playlistName = null;
        string? pendingTitle = null, pendingArtist = null;
        int? pendingDuration = null;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim('\r', ' ', '\t', '\uFEFF');
            if (line.Length == 0) continue;

            if (line.StartsWith("#EXT-X-", StringComparison.OrdinalIgnoreCase))
                return Fail("This is a streaming (HLS) manifest, not a playlist of songs.");

            if (line.StartsWith('#'))
            {
                if (line.StartsWith("#PLAYLIST:", StringComparison.OrdinalIgnoreCase))
                    playlistName = line[10..].Trim();
                else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
                    (pendingDuration, pendingArtist, pendingTitle) = ParseExtInf(line[8..]);
                continue;
            }

            if (entries.Count >= MaxEntries) return Fail("Playlist has too many entries.");
            entries.Add(MakeEntry(line, baseDir, pendingTitle, pendingArtist, pendingDuration));
            pendingTitle = pendingArtist = null; pendingDuration = null;
        }
        return new M3uParseResult(entries, playlistName, null);
    }

    private static (int? Seconds, string? Artist, string? Title) ParseExtInf(string body)
    {
        var comma = body.IndexOf(',');
        var head = comma >= 0 ? body[..comma] : body;
        var display = comma >= 0 ? body[(comma + 1)..].Trim() : "";

        int? seconds = null;
        var m = Regex.Match(head.Trim(), @"^-?\d+");
        if (m.Success && int.TryParse(m.Value, out var s) && s >= 0) seconds = s;

        if (display.Length == 0) return (seconds, null, null);
        var sep = display.IndexOf(" - ", StringComparison.Ordinal);
        return sep > 0 ? (seconds, display[..sep].Trim(), display[(sep + 3)..].Trim()) : (seconds, null, display);
    }

    private static M3uEntry MakeEntry(string target, string? baseDir, string? title, string? artist, int? seconds)
    {
        if (target.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(target, UriKind.Absolute, out var fileUri))
            return new M3uEntry(fileUri.LocalPath, false, title, artist, seconds);

        if (UrlScheme.IsMatch(target))
            return new M3uEntry(target, true, title, artist, seconds);

        var path = target.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        if (!Path.IsPathRooted(path) && !string.IsNullOrEmpty(baseDir))
            path = Path.GetFullPath(Path.Combine(baseDir, path));
        return new M3uEntry(path, false, title, artist, seconds);
    }

    private static M3uParseResult Fail(string error) => new(Array.Empty<M3uEntry>(), null, error);
}
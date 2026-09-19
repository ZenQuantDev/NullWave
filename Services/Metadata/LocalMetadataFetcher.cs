using System;
using System.IO;
using Serilog;
using TagLib;
using NullWave.Models;

namespace NullWave.Services.Metadata;

public class LocalMetadataFetcher
{
    public MediaType DetectMediaType(string filePath)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".m4b") return MediaType.Audiobook;

            using var file = TagLib.File.Create(filePath);
            var genres = file.Tag.Genres ?? Array.Empty<string>();
            foreach (var genre in genres)
            {
                var g = genre.ToLowerInvariant();
                if (g.Contains("audiobook") || g.Contains("spoken word") || g.Contains("speech"))
                    return MediaType.Audiobook;
                if (g.Contains("podcast"))
                    return MediaType.Podcast;
            }
        }
        catch { }
        return MediaType.Music;
    }

    public (string? Album, int TrackNumber) FetchAlbumAndTrackNumber(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var album = file.Tag.Album;
            var trackNum = (int)file.Tag.Track;
            return (string.IsNullOrWhiteSpace(album) ? null : album, trackNum);
        }
        catch { return (null, 0); }
    }

    public (string Title, string Artist, TimeSpan Duration) Fetch(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var title = file.Tag.Title;
            var artist = file.Tag.FirstPerformer
                         ?? (file.Tag.Performers.Length > 0
                             ? string.Join(", ", file.Tag.Performers)
                             : null);
            var duration = file.Properties.Duration;

            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(artist))
                artist = "Unknown";

            Log.Information("Local file tags read: {Title} by {Artist}", title, artist);
            return (title, artist, duration);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TagLib failed for {Path}, falling back to filename", filePath);
            return (Path.GetFileNameWithoutExtension(filePath), "Unknown", TimeSpan.Zero);
        }
    }
}
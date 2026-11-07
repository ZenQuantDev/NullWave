using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;

namespace NullWave.Services;

public sealed record M3uImportMatch(
    M3uEntry Entry,
    Track? MatchedTrack,
    M3uMatchType MatchType
);

public enum M3uMatchType
{
    NotFound,
    PathMatch,
    UrlMatch,
    ArtistTitleMatch
}

public sealed record M3uImportPreview(
    IReadOnlyList<M3uImportMatch> Matches,
    int TotalEntries,
    int MatchedCount,
    int MissingCount,
    string? PlaylistName
);

public class M3uImporter
{
    private readonly LibraryService _library;

    public M3uImporter(LibraryService library)
    {
        _library = library;
    }

    public M3uImportPreview Preview(string m3uPath)
    {
        var bytes = File.ReadAllBytes(m3uPath);
        var baseDir = Path.GetDirectoryName(m3uPath);
        var parsed = M3uParser.ParseBytes(bytes, baseDir);

        if (!parsed.Ok)
            return new M3uImportPreview(Array.Empty<M3uImportMatch>(), 0, 0, 0, null);

        var matches = new List<M3uImportMatch>();
        var allTracks = _library.GetAll();

        foreach (var entry in parsed.Entries)
        {
            var match = FindMatch(entry, allTracks);
            matches.Add(match);
        }

        var matchedCount = matches.Count(m => m.MatchType != M3uMatchType.NotFound);
        var missingCount = matches.Count(m => m.MatchType == M3uMatchType.NotFound);

        return new M3uImportPreview(matches, parsed.Entries.Count, matchedCount, missingCount, parsed.PlaylistName);
    }

    private M3uImportMatch FindMatch(M3uEntry entry, IReadOnlyList<Track> allTracks)
    {
        if (!entry.IsUrl && !string.IsNullOrEmpty(entry.Target))
        {
            var normalizedTarget = Path.GetFullPath(entry.Target);
            var pathMatch = allTracks.FirstOrDefault(t => 
                !string.IsNullOrEmpty(t.FilePath) && 
                Path.GetFullPath(t.FilePath).Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase));
            
            if (pathMatch != null)
                return new M3uImportMatch(entry, pathMatch, M3uMatchType.PathMatch);
        }

        if (entry.IsUrl && !string.IsNullOrEmpty(entry.Target))
        {
            var normalizedUrl = NormalizeUrl(entry.Target);
            var urlMatch = allTracks.FirstOrDefault(t => 
                !string.IsNullOrEmpty(t.Url) && 
                NormalizeUrl(t.Url).Equals(normalizedUrl, StringComparison.OrdinalIgnoreCase));
            
            if (urlMatch != null)
                return new M3uImportMatch(entry, urlMatch, M3uMatchType.UrlMatch);
        }

        if (!string.IsNullOrEmpty(entry.Title) || !string.IsNullOrEmpty(entry.Artist))
        {
            // TrackNormalizer is static - call methods directly
            var titleNorm = entry.Title != null ? TrackNormalizer.NormalizeForCompare(entry.Title) : "";
            var artistNorm = entry.Artist != null ? TrackNormalizer.NormalizeForCompare(entry.Artist) : "";

            var fuzzyMatch = allTracks.FirstOrDefault(t =>
            {
                var tTitle = TrackNormalizer.NormalizeForCompare(t.Title);
                var tArtist = TrackNormalizer.NormalizeForCompare(t.Artist);
                
                var titleMatch = !string.IsNullOrEmpty(titleNorm) && tTitle.Contains(titleNorm, StringComparison.Ordinal);
                var artistMatch = !string.IsNullOrEmpty(artistNorm) && tArtist.Contains(artistNorm, StringComparison.Ordinal);
                
                return titleMatch && artistMatch;
            });

            if (fuzzyMatch != null)
                return new M3uImportMatch(entry, fuzzyMatch, M3uMatchType.ArtistTitleMatch);
        }

        return new M3uImportMatch(entry, null, M3uMatchType.NotFound);
    }

    private static string NormalizeUrl(string url)
    {
        if (url.Contains("youtube.com") || url.Contains("youtu.be"))
        {
            try
            {
                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var videoId = query["v"];
                
                if (string.IsNullOrEmpty(videoId) && url.Contains("youtu.be/"))
                {
                    var parts = url.Split("youtu.be/");
                    if (parts.Length > 1)
                        videoId = parts[1].Split('?')[0];
                }
                
                return videoId != null ? $"youtube:{videoId}" : url;
            }
            catch
            {
                return url;
            }
        }
        
        return url;
    }
}
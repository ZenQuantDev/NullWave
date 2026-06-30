using System;
using System.Linq;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services.Integration;

/// <summary>
/// Enriches tracks with Last.fm tags and album art after they are
/// added to the library. Album art resolution is delegated to
/// AlbumArtService so the fallback chain lives in one place. Title/artist
/// parsing for messy YouTube-style titles is shared with AlbumArtService
/// via TrackTitleParser, so both services resolve search terms identically.
/// </summary>
public class LastFmEnrichmentService
{
    private readonly LastFmService _lastFm;
    private readonly LibraryService _library;
    private readonly AlbumArtService _albumArt;

    /// <summary>
    /// Fires once the startup BackfillAsync() run finishes - either after
    /// processing every untagged track, or immediately if there was
    /// nothing to backfill. MainViewModel uses this to know when it's
    /// safe to generate a mood playlist with meaningfully-populated tags,
    /// instead of guessing with a fixed delay.
    /// </summary>
    public event Action? BackfillCompleted;

    public LastFmEnrichmentService(LastFmService lastFm, LibraryService library, AlbumArtService albumArt)
    {
        _lastFm   = lastFm;
        _library  = library;
        _albumArt = albumArt;
    }

    /// <summary>
    /// Enrich a single track in the background - safe to fire-and-forget.
    /// </summary>
    public void EnrichAsync(Track track)
    {
        if (!_lastFm.IsConfigured) return;
        _ = Task.Run(() => EnrichTrackAsync(track));
    }

    /// <summary>
    /// Backfill all existing tracks that have no tags yet.
    /// Runs on startup in the background. Fires BackfillCompleted when done
    /// (including the case where there was nothing to do).
    /// </summary>
    public void BackfillAsync()
    {
        if (!_lastFm.IsConfigured)
        {
            BackfillCompleted?.Invoke();
            return;
        }

        var untagged = _library.GetAll()
            .Where(t => t.Tags.Count == 0
                     && !string.IsNullOrWhiteSpace(t.Title)
                     && t.Title != t.Url)
            .ToList();

        if (untagged.Count == 0)
        {
            BackfillCompleted?.Invoke();
            return;
        }

        Log.Information("[LastFmEnrichment] Backfilling {Count} untagged tracks", untagged.Count);

        _ = Task.Run(async () =>
        {
            foreach (var track in untagged)
            {
                await EnrichTrackAsync(track);
                await Task.Delay(500);
            }

            Log.Information("[LastFmEnrichment] Backfill complete");
            BackfillCompleted?.Invoke();
        });
    }

    private async Task EnrichTrackAsync(Track track)
    {
        try
        {
            // Use the shared parser to resolve clean search terms - this
            // fixes the case where Artist is "Unknown" and Title is a messy
            // YouTube string like "Mariah Carey - Obsessed (Official Music
            // Video)". Previously this method sent that raw title straight
            // to Last.fm with an empty artist, which usually returned no
            // match at all (and therefore no tags) for mainstream tracks
            // imported without a separate artist field.
            var (title, artist) = TrackTitleParser.ResolveSearchTerms(track);

            var info = await _lastFm.GetTrackInfoAsync(title, artist);
            bool changed = false;

            if (info != null)
            {
                // Apply tags
                if (info.Tags.Count > 0 && track.Tags.Count == 0)
                {
                    track.Tags.Clear();
                    foreach (var tag in info.Tags)
                        track.Tags.Add(tag);
                    changed = true;
                    Log.Debug("[LastFmEnrichment] Tags added for {Title}: {Tags}",
                        track.Title, string.Join(", ", info.Tags));
                }

                // Apply corrected title if still showing the raw URL
                if (!string.IsNullOrWhiteSpace(info.Title)
                    && info.Title != track.Title
                    && track.Title == track.Url)
                {
                    track.Title = info.Title;
                    changed = true;
                }

                // Backfill artist if it was Unknown and Last.fm resolved one
                // via the parsed title (e.g. "Mariah Carey - Obsessed..." →
                // artist correctly identified as Mariah Carey)
                if ((track.Artist == "Unknown" || string.IsNullOrWhiteSpace(track.Artist))
                    && !string.IsNullOrWhiteSpace(info.Artist))
                {
                    track.Artist = info.Artist;
                    changed = true;
                }
            }

            // Album art resolution via the shared fallback chain
            if (string.IsNullOrEmpty(track.AlbumArtPath))
            {
                var artPath = await _albumArt.GetArtPathAsync(track);
                if (artPath != AlbumArtService.PlaceholderPath)
                {
                    track.AlbumArtPath = artPath;
                    changed = true;
                    Log.Information("[LastFmEnrichment] Album art resolved for {Title}", track.Title);
                }
            }

            if (changed)
            {
                _library.Update(track);
                Log.Information("[LastFmEnrichment] Enriched: {Title} - tags: [{Tags}]",
                    track.Title, string.Join(", ", track.Tags));
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LastFmEnrichment] Failed for {Title}", track.Title);
        }
    }
}
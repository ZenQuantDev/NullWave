using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Metadata;
using Serilog;

namespace NullWave.Services.Integration;

public class LastFmEnrichmentService
{
    private readonly LastFmService _lastFm;
    private readonly LibraryService _library;
    private readonly AlbumArtService _albumArt;

    public event Action? BackfillCompleted;

    public LastFmEnrichmentService(LastFmService lastFm, LibraryService library, AlbumArtService albumArt)
    {
        _lastFm   = lastFm;
        _library  = library;
        _albumArt = albumArt;
    }

    public void EnrichAsync(Track track, CancellationToken ct = default)
    {
        if (!_lastFm.IsConfiguredForRead) return;
        _ = Task.Run(() => EnrichTrackAsync(track, ct), ct);
    }

    public void BackfillAsync(CancellationToken externalToken = default)
    {
        if (!_lastFm.IsConfiguredForRead)
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

        Log.Information("[LastFmEnrichment] Starting batch backfill for {Count} untagged tracks", untagged.Count);

        _ = Task.Run(async () =>
        {
            int enrichedCount = 0;
            int skippedCount = 0;
            var timer = Stopwatch.StartNew();

            var options = new ParallelOptions 
            { 
                MaxDegreeOfParallelism = 3,
                CancellationToken = externalToken 
            };

            await Parallel.ForEachAsync(untagged, options, async (track, ct) =>
            {
                bool success = await EnrichTrackAsync(track, ct);
                if (success) 
                    Interlocked.Increment(ref enrichedCount); 
                else 
                    Interlocked.Increment(ref skippedCount);

                // Throttling safety valve: yields the thread for 250ms 
                // to preserve API key integrity across parallel workers
                await Task.Delay(250, ct); 
            });

            timer.Stop();
            
            Log.Information("[LastFmEnrichment] Batch backfill complete in {ElapsedMs}ms. Enriched: {Enriched} | Skipped: {Skipped} | Total: {Total}",
                timer.ElapsedMilliseconds, enrichedCount, skippedCount, untagged.Count);
            
            await Dispatcher.UIThread.InvokeAsync(() => BackfillCompleted?.Invoke());
        });
    }

    private async Task<bool> EnrichTrackAsync(Track track, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            string searchArtist = track.Artist;
            string searchTitle = track.Title;

            bool isArtistGeneric = string.IsNullOrWhiteSpace(searchArtist) || 
                                searchArtist.Equals("Unknown", StringComparison.OrdinalIgnoreCase) || 
                                searchArtist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase);

            if (isArtistGeneric)
            {
                var sanitized = TitleSanitizer.Sanitize(track.Title);
                searchArtist = sanitized.Artist;
                searchTitle = sanitized.Title;

                if (string.IsNullOrWhiteSpace(searchArtist) || searchArtist.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    var parsed = TrackTitleParser.TryParseArtistTitle(track.Title);
                    if (parsed != null)
                    {
                        searchArtist = parsed.Value.Artist;
                        searchTitle = parsed.Value.Title;
                    }
                }
            }
            else
            {
                var sanitizedTitle = TitleSanitizer.Sanitize(track.Title);
                searchTitle = !string.IsNullOrWhiteSpace(sanitizedTitle.Artist) 
                    ? sanitizedTitle.Title 
                    : TitleSanitizer.SanitizeSingle(track.Title);
            }

            if (string.IsNullOrWhiteSpace(searchArtist)) searchArtist = "Unknown";
            if (string.IsNullOrWhiteSpace(searchTitle)) searchTitle = track.Title;

            var info = await _lastFm.GetTrackInfoAsync(searchTitle, searchArtist);

            if (info == null && searchArtist != "Unknown")
            {
                ct.ThrowIfCancellationRequested();
                Log.Verbose("[LastFmEnrichment] No match for Artist={Artist} | Title={Title}. Retrying with reversed fields.", searchArtist, searchTitle);
                info = await _lastFm.GetTrackInfoAsync(searchArtist, searchTitle);
            }

            ct.ThrowIfCancellationRequested();

            List<string>? stagedTags = null;
            string stagedArtist = track.Artist;
            string stagedTitle = track.Title;
            string? stagedAlbumArt = track.AlbumArtPath;

            if (info != null)
            {
                if (info.Tags.Count > 0 && track.Tags.Count == 0)
                {
                    stagedTags = new List<string>();
                    foreach (var tag in info.Tags)
                    {
                        bool isTagJunk = tag.Length > 20 && tag.Contains(" ") || 
                                        Regex.IsMatch(tag, @"\b(focus|tdci|klima|szyby|swoje|fave|track|album|song)\b", RegexOptions.IgnoreCase);
                        
                        if (!isTagJunk) stagedTags.Add(tag);
                    }
                }

                string cleanArtist = TitleSanitizer.SanitizeSingle(info.Artist);
                if (string.IsNullOrWhiteSpace(cleanArtist)) cleanArtist = info.Artist.Trim();

                string cleanTitle = TitleSanitizer.SanitizeSingle(info.Title);
                if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = info.Title.Trim();

                if (!string.IsNullOrWhiteSpace(cleanArtist) && track.Artist != cleanArtist)
                {
                    stagedArtist = cleanArtist;
                }

                if (!string.IsNullOrWhiteSpace(cleanTitle) && track.Title != cleanTitle)
                {
                    bool isTruncatedGarbage = cleanTitle.EndsWith("ft", StringComparison.OrdinalIgnoreCase) || 
                                            cleanTitle.EndsWith("ft.", StringComparison.OrdinalIgnoreCase) ||
                                            cleanTitle.EndsWith("feat", StringComparison.OrdinalIgnoreCase);

                    if (!isTruncatedGarbage)
                    {
                        stagedTitle = cleanTitle;
                    }
                    else
                    {
                        Log.Warning("[LastFmEnrichment] Rejected truncated title overwrite: '{Incoming}' for '{Current}'", cleanTitle, track.Title);
                    }
                }
            }

            if (string.IsNullOrEmpty(stagedAlbumArt))
            {
                ct.ThrowIfCancellationRequested();
                
                var artPath = await _albumArt.GetArtPathAsync(track);
                if (artPath != AlbumArtService.PlaceholderPath)
                {
                    stagedAlbumArt = artPath;
                }
            }

            bool tagsChanged = stagedTags != null && stagedTags.Count > 0;
            bool artistChanged = stagedArtist != track.Artist;
            bool titleChanged = stagedTitle != track.Title;
            bool artChanged = stagedAlbumArt != track.AlbumArtPath;

            if (tagsChanged || artistChanged || titleChanged || artChanged)
            {
                ct.ThrowIfCancellationRequested();

                // Marshal ONLY the property updates to the UI
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (tagsChanged)
                    {
                        track.Tags.Clear();
                        foreach (var tag in stagedTags!) track.Tags.Add(tag);
                    }
                    if (artistChanged) track.Artist = stagedArtist;
                    if (titleChanged) track.Title = stagedTitle;
                    if (artChanged) track.AlbumArtPath = stagedAlbumArt;
                });

                // ✅ Back on the thread pool worker! Perfectly safe for disk I/O
                _library.Update(track);

                Log.Verbose("[LastFmEnrichment] Enriched: {Title} - tags: [{Tags}]", track.Title, string.Join(", ", track.Tags));
                return true;
            }

            Log.Verbose("[LastFmEnrichment] Skipped {Title}: Match found={HasInfo}, New Tags found={HasTags}", 
                track.Title, info != null, stagedTags?.Count > 0);

            return false;
        }
        catch (OperationCanceledException)
        {
            Log.Information("[LastFmEnrichment] Enrichment task was canceled for track: {Title}", track.Title);
            return false;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LastFmEnrichment] Failed for {Title}", track.Title);
            return false;
        }
    }
}
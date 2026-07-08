using System;
using System.Collections.Concurrent;
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
using NullWave.Services.SmartSorting;
using Serilog;

namespace NullWave.Services.Integration;

public partial class LastFmEnrichmentService
{
    private readonly LastFmService _lastFm;
    private readonly LibraryService _library;
    private readonly LocalAIService _localAi;
    private readonly PreferencesService _prefsService;
    private readonly SemaphoreSlim _enrichSemaphore = new(3);
    private int _isBackfilling = 0;

    [GeneratedRegex(@"\s*[\(\[].*?[\)\]]")]
    private static partial Regex ParenthesesRegex();

    [GeneratedRegex(@"\s+(ft\.|feat\.|featuring|Ft\.|Feat\.|Featuring).*", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturesRegex();

    [GeneratedRegex(@"\b(focus|tdci|klima|szyby|swoje|fave|track|album|song)\b", RegexOptions.IgnoreCase)]
    private static partial Regex JunkTagsRegex();

    public event Action? BackfillCompleted;

    public LastFmEnrichmentService(LastFmService lastFm, LibraryService library, LocalAIService localAi, PreferencesService prefsService)
    {
        _lastFm  = lastFm;
        _library = library;
        _localAi = localAi;
        _prefsService = prefsService;
    }

    public void EnrichAsync(Track track, CancellationToken ct = default)
    {
        _ = Task.Run(async () =>
        {
            await _enrichSemaphore.WaitAsync(ct);
            try
            {
                await EnrichTrackAsync(track, ct);
            }
            finally
            {
                _enrichSemaphore.Release();
            }
        }, ct);
    }

    public void BackfillAsync(CancellationToken externalToken = default)
    {
        if (Interlocked.Exchange(ref _isBackfilling, 1) == 1)
        {
            Log.Warning("[LastFmEnrichment] Batch backfill execution blocked; process is already running.");
            return;
        }

        var untagged = _library.GetAll()
            .Where(t => t.Tags.Count == 0 && !string.IsNullOrWhiteSpace(t.Title) && t.Title != t.Url)
            .ToList();

        if (untagged.Count == 0)
        {
            ResetBackfillAndNotify();
            return;
        }

        int totalTracks = untagged.Count;
        Log.Information("[LastFmEnrichment] Starting batch backfill for {Count} untagged tracks", totalTracks);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
        ToastService.Instance.Show($"Starting catalog synchronization for {totalTracks} tracks...", ToastType.Info, 4000);

        _ = Task.Run(async () =>
        {
            int enrichedCount = 0;
            var timer = Stopwatch.StartNew();
            var failedTracks = new ConcurrentBag<Track>();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = 3,
                CancellationToken = cts.Token
            };

            try
            {
                await Parallel.ForEachAsync(untagged, options, async (track, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    bool success = await EnrichTrackLastFmAsync(track, failedTracks, ct);
                    if (success) Interlocked.Increment(ref enrichedCount);
                    await Task.Delay(50, ct);
                });

                var failedList = failedTracks.ToList();
                if (failedList.Count > 0 && PowerStateService.ReadPowerState() != PowerState.Battery)
                {
                    Log.Information("[LastFmEnrichment] Routing {Count} failed tracks to bulk AI fallback.", failedList.Count);
                    var chunks = failedList.Chunk(10);
                    foreach (var chunk in chunks)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        var trackData = chunk.Select((t, idx) => (Index: idx + 1, t.Title, t.Artist, FilePath: t.FilePath ?? "")).ToList();
                        var aiResults = await _localAi.GenerateTagsBulkAsync(trackData, cts.Token);
                        for (int i = 0; i < chunk.Length; i++)
                        {
                            var track = chunk[i];
                            if (aiResults != null && i < aiResults.Count)
                            {
                                var tags = aiResults[i];
                                if (tags != null && tags.Length > 0)
                                {
                                    await ApplyTrackTagsAsync(track, tags);
                                    Interlocked.Increment(ref enrichedCount);
                                }
                            }
                        }
                    }
                }
                else if (failedList.Count > 0)
                {
                    Log.Information("[LastFmEnrichment] Skipping AI fallback for {Count} tracks to preserve battery life.", failedList.Count);
                }

                timer.Stop();
                Log.Information("[LastFmEnrichment] Batch backfill complete in {ElapsedMs}ms.", timer.ElapsedMilliseconds);
                ToastService.Instance.Show($"Sync complete! Enriched {enrichedCount} track profiles.", ToastType.Success, 5000);
            }
            catch (OperationCanceledException)
            {
                timer.Stop();
                Log.Warning("[LastFmEnrichment] Batch processing cancelled by user intercept.");
                ToastService.Instance.Show("Metadata synchronization aborted.", ToastType.Warning, 4000);
            }
            finally
            {
                cts.Dispose();
                Interlocked.Exchange(ref _isBackfilling, 0);
                await Dispatcher.UIThread.InvokeAsync(() => BackfillCompleted?.Invoke());
            }
        }, externalToken);
    }

    private async Task<bool> EnrichTrackLastFmAsync(Track track, ConcurrentBag<Track> failedTracks, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var (cleanTitle, cleanArtist) = _prefsService.Current.AutoCleanMetadata
                ? NormalizeMetadata(track.Title ?? string.Empty, track.Artist ?? string.Empty)
                : (track.Title ?? string.Empty, track.Artist ?? string.Empty);

            if (!_lastFm.IsConfiguredForRead)
            {
                failedTracks.Add(track);
                return false;
            }

            var info = await FetchLastFmInfoWithFallbackAsync(cleanTitle, cleanArtist, ct);
            if (info == null || info.Tags.Count == 0)
            {
                failedTracks.Add(track);
                return false;
            }

            var stagedTags = FilterJunkTags(info.Tags);
            if (stagedTags.Count > 0)
            {
                await ApplyTrackTagsAsync(track, stagedTags);
                return true;
            }

            failedTracks.Add(track);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LastFmEnrichment] Last.fm query failed for {Title}", track.Title);
            failedTracks.Add(track);
            return false;
        }
    }

    private async Task<bool> EnrichTrackAsync(Track track, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var (cleanTitle, cleanArtist) = _prefsService.Current.AutoCleanMetadata
                ? NormalizeMetadata(track.Title ?? string.Empty, track.Artist ?? string.Empty)
                : (track.Title ?? string.Empty, track.Artist ?? string.Empty);

            LastFmTrackInfo? info = null;
            if (_lastFm.IsConfiguredForRead)
            {
                info = await FetchLastFmInfoWithFallbackAsync(cleanTitle, cleanArtist, ct);
            }

            List<string>? stagedTags = null;
            if (info != null && info.Tags.Count > 0 && track.Tags.Count == 0)
            {
                stagedTags = FilterJunkTags(info.Tags);
            }

            if ((stagedTags == null || stagedTags.Count == 0) && track.Tags.Count == 0 && await _localAi.IsOllamaRunningAsync())
            {
                if (PowerStateService.ReadPowerState() == PowerState.Battery)
                {
                    Log.Debug("[LastFmEnrichment] Skipping AI fallback for '{Title}' to preserve battery life.", track.Title);
                }
                else
                {
                    Log.Debug("[LastFmEnrichment] Internet scraper found zero tags for '{Title}'. Routing to local AI fallback.", track.Title);
                    var aiTags = await _localAi.GenerateTagsForTrackAsync(cleanTitle, cleanArtist, track.FilePath ?? string.Empty, ct);
                    if (aiTags != null && aiTags.Length > 0)
                    {
                        stagedTags = aiTags.ToList();
                    }
                }
            }

            if (stagedTags != null && stagedTags.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                await ApplyTrackTagsAsync(track, stagedTags);
                Log.Verbose("[LastFmEnrichment] Enriched: {Title} - tags: [{Tags}]", track.Title, string.Join(", ", track.Tags));
                return true;
            }

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

    private async Task ApplyTrackTagsAsync(Track track, IEnumerable<string> tags)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            track.Tags.Clear();
            foreach (var tag in tags) track.Tags.Add(tag);
        });
        await _library.UpdateTrackMetadataAsync(track);
    }

    private async Task<LastFmTrackInfo?> FetchLastFmInfoWithFallbackAsync(string title, string artist, CancellationToken ct)
    {
        var info = await _lastFm.GetTrackInfoAsync(title, artist);
        if (info == null && artist != "Unknown")
        {
            ct.ThrowIfCancellationRequested();
            info = await _lastFm.GetTrackInfoAsync(artist, title);
        }
        return info;
    }

    private (string Title, string Artist) NormalizeMetadata(string rawTitle, string rawArtist)
    {
        string cleanTitle = ParenthesesRegex().Replace(rawTitle ?? string.Empty, "").Trim();
        cleanTitle = FeaturesRegex().Replace(cleanTitle, "").Trim();
        string cleanArtist = string.IsNullOrWhiteSpace(rawArtist) ? "Unknown" : rawArtist.Trim();
        if (string.IsNullOrWhiteSpace(cleanTitle)) cleanTitle = rawTitle ?? string.Empty;
        return (cleanTitle, cleanArtist);
    }

    private List<string> FilterJunkTags(IEnumerable<string> tags)
    {
        var filtered = new List<string>();
        foreach (var tag in tags)
        {
            bool isTagJunk = tag.Length > 20 && tag.Contains(" ") || JunkTagsRegex().IsMatch(tag);
            if (!isTagJunk) filtered.Add(tag);
        }
        return filtered;
    }

    private void ResetBackfillAndNotify()
    {
        Interlocked.Exchange(ref _isBackfilling, 0);
        _ = Dispatcher.UIThread.InvokeAsync(() => BackfillCompleted?.Invoke());
    }
}
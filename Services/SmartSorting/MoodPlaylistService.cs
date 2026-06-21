using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class MoodPlaylistResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public List<Track> Tracks { get; init; } = new();
    public string Mood { get; init; } = string.Empty;
    public string WeatherCondition { get; init; } = string.Empty;
    public double TemperatureC { get; init; }
    public bool UsedAI { get; init; }
}

/// <summary>
/// Orchestrates the full mood-playlist pipeline: fetch weather, map to mood
/// tags, filter the library by those tags, then either hand the filtered
/// list to LocalAIService for ranking (Tier 1) or use the tag-filtered list
/// directly (Tier 2 fallback — no AI, or AI unavailable/disabled).
/// </summary>
public class MoodPlaylistService
{
    private readonly WeatherService _weather;
    private readonly LocalAIService _ai;
    private readonly LibraryService _library;

    public MoodPlaylistService(WeatherService weather, LocalAIService ai, LibraryService library)
    {
        _weather = weather;
        _ai = ai;
        _library = library;
    }

    public async Task<MoodPlaylistResult> GenerateAsync(
        double latitude,
        double longitude,
        bool useLocalAI,
        bool forceWeatherRefresh = false,
        int maxResults = 25)
    {
        var weather = await _weather.GetWeatherAsync(latitude, longitude, forceWeatherRefresh);
        if (weather == null)
        {
            return new MoodPlaylistResult
            {
                Success = false,
                FailureReason = "Could not fetch weather. Check your OpenWeather API key and location."
            };
        }

        var hour = DateTime.Now.Hour;
        var moodTags = WeatherMoodMap.GetMoodTags(weather.Condition, weather.TemperatureC, hour);
        var moodLabel = string.Join("/", moodTags);

        Log.Information("[MoodPlaylist] Weather: {Condition} {Temp}°C → mood tags: [{Tags}]",
            weather.Condition, weather.TemperatureC, string.Join(", ", moodTags));

        var candidates = _library.GetAll()
            .Where(t => t.Tags.Any(tag => moodTags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            .ToList();

        Log.Information("[MoodPlaylist] {Count} tracks matched mood tags", candidates.Count);

        // Fallback: if no tagged tracks match, widen to the whole library so
        // the user still gets a result rather than an empty playlist.
        var usingFallbackPool = false;
        if (candidates.Count == 0)
        {
            candidates = _library.GetAll().ToList();
            usingFallbackPool = true;
            Log.Information("[MoodPlaylist] No tracks matched mood tags — falling back to full library");
        }

        if (candidates.Count == 0)
        {
            return new MoodPlaylistResult
            {
                Success = false,
                FailureReason = "Library is empty — add some tracks first.",
                WeatherCondition = weather.Condition,
                TemperatureC = weather.TemperatureC,
                Mood = moodLabel
            };
        }

        // Tier 1 — local AI ranking, only if requested and reachable
        if (useLocalAI && !usingFallbackPool)
        {
            var ollamaUp = await _ai.IsOllamaRunningAsync();
            if (ollamaUp)
            {
                var rankedIds = await _ai.RankTracksForMoodAsync(
                    moodLabel, weather.Condition, weather.TemperatureC,
                    candidates.ToArray(), maxResults);

                if (rankedIds.Length > 0)
                {
                    var byId = candidates.ToDictionary(t => t.Id.ToString());
                    var ranked = rankedIds
                        .Where(byId.ContainsKey)
                        .Select(id => byId[id])
                        .ToList();

                    if (ranked.Count > 0)
                    {
                        return new MoodPlaylistResult
                        {
                            Success = true,
                            Tracks = ranked,
                            Mood = moodLabel,
                            WeatherCondition = weather.Condition,
                            TemperatureC = weather.TemperatureC,
                            UsedAI = true
                        };
                    }
                }

                Log.Warning("[MoodPlaylist] AI ranking returned no usable results — falling back to tag-only ordering");
            }
            else
            {
                Log.Information("[MoodPlaylist] Ollama not reachable — falling back to tag-only ordering");
            }
        }

        // Tier 2 — tag-only fallback: most-played first within the matched mood pool
        var tagOnly = candidates
            .OrderByDescending(t => t.PlayCount)
            .ThenByDescending(t => t.IsFavorite)
            .Take(maxResults)
            .ToList();

        return new MoodPlaylistResult
        {
            Success = true,
            Tracks = tagOnly,
            Mood = moodLabel,
            WeatherCondition = weather.Condition,
            TemperatureC = weather.TemperatureC,
            UsedAI = false
        };
    }
}

/// <summary>
/// Maps weather condition + temperature + time of day to mood tag
/// candidates. Tags match against Track.Tags, which are populated by
/// LastFmEnrichmentService from real Last.fm community tags — genre and
/// descriptor words like "pop", "dance", "rnb", "electronic", "90s",
/// "female vocalists", "acoustic", "chill", "ambient", etc.
///
/// Earlier versions of this map used invented mood words (e.g. "mellow",
/// "night") that essentially never appear in real Last.fm tag data, so
/// matching almost always failed and fell back to the full library. This
/// version uses words that genuinely show up in Last.fm's actual tag
/// vocabulary, cross-referenced against tags observed in this project's
/// own enrichment runs (pop, dance, electronic, rnb, trap, Hip-Hop, 90s,
/// 2010s, female vocalists, sexy, acoustic, indie, chill, ambient).
/// </summary>
public static class WeatherMoodMap
{
    public static string[] GetMoodTags(string condition, double tempC, int hour)
    {
        string[] tags = condition switch
        {
            "Rain" or "Drizzle" =>
                new[] { "chill", "acoustic", "rnb", "indie", "melancholic", "sad", "soul" },

            "Thunderstorm" =>
                new[] { "electronic", "dark", "intense", "rock", "industrial", "trap" },

            "Snow" =>
                new[] { "acoustic", "ambient", "chill", "folk", "winter", "soul" },

            "Clear" when tempC > 22 =>
                new[] { "pop", "dance", "dance-pop", "summer", "party", "upbeat", "happy", "disco" },

            "Clear" =>
                new[] { "indie", "pop", "chill", "acoustic", "folk" },

            "Clouds" =>
                new[] { "indie", "chill", "rnb", "soul", "alternative" },

            "Mist" or "Fog" or "Haze" =>
                new[] { "ambient", "chill", "dreamy", "electronic", "downtempo" },

            _ => new[] { "pop", "chill" }
        };

        // Late night / early morning skews toward lower-energy descriptors —
        // these are common standalone Last.fm tags, not invented words.
        if (hour >= 22 || hour < 5)
            tags = tags.Concat(new[] { "chill", "ambient", "rnb", "soul" }).Distinct().ToArray();

        return tags;
    }
}
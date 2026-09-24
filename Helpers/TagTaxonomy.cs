using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Helpers;

public static class TagTaxonomy
{
    public static readonly Dictionary<string, string[]> GenreAxes = new()
    {
        ["Electronic"] = new[] { "electronic", "edm", "house", "techno", "dubstep", "synth" },
        ["Rock/Metal"] = new[] { "rock", "metal", "punk", "grunge", "alternative" },
        ["Hip-Hop"]    = new[] { "hip hop", "hip-hop", "rap", "trap", "r&b" },
        ["Pop"]        = new[] { "pop", "k-pop", "indie pop", "synthpop" },
        ["Jazz/Classical"] = new[] { "jazz", "classical", "orchestral", "blues" },
        ["Ambient/Chill"] = new[] { "ambient", "chill", "lofi", "downtempo", "acoustic" }
    };

    public static readonly Dictionary<string, string[]> MoodAxes = new()
    {
        ["Energetic"] = new[] { "energetic", "upbeat", "hype", "dance" },
        ["Melancholy"] = new[] { "sad", "melancholy", "moody", "emotional" },
        ["Chill"] = new[] { "chill", "relax", "calm", "mellow" },
        ["Dark"] = new[] { "dark", "aggressive", "intense", "heavy" },
        ["Happy"] = new[] { "happy", "feel good", "uplifting", "joy" },
        ["Focus"] = new[] { "focus", "study", "instrumental", "concentration" }
    };

    // FIX (C6): Centralized alias map for unifying Weather, External, and AI tags
    private static readonly Dictionary<string, string> AliasMap = BuildAliasMap();

    private static Dictionary<string, string> BuildAliasMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (canonical, keywords) in GenreAxes) foreach (var kw in keywords) map[kw] = canonical;
        foreach (var (canonical, keywords) in MoodAxes) foreach (var kw in keywords) map[kw] = canonical;
        
        map["rnb"] = "Hip-Hop"; map["r&b"] = "Hip-Hop";
        map["hip hop"] = "Hip-Hop"; map["hip-hop"] = "Hip-Hop";
        map["rap"] = "Hip-Hop"; map["trap"] = "Hip-Hop";
        map["lofi"] = "Ambient/Chill"; map["lo-fi"] = "Ambient/Chill";
        return map;
    }

    public static string? Normalize(string rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag)) return null;
        return AliasMap.TryGetValue(rawTag.Trim().ToLowerInvariant(), out var canonical) ? canonical : null;
    }

    public static List<string> NormalizeAll(IEnumerable<string> tags) =>
        tags.Select(Normalize).Where(t => t != null).Select(t => t!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // FIX (C6): Count each track AT MOST ONCE per axis, rather than breaking on the first axis match
    public static Dictionary<string, double> ComputeDistribution(IEnumerable<Track> tracks, Dictionary<string, string[]> axes)
    {
        var counts = axes.Keys.ToDictionary(k => k, _ => 0);
        int trackCount = 0;

        foreach (var t in tracks)
        {
            if (t.Tags == null || t.Tags.Count == 0) continue;
            trackCount++;

            foreach (var (axis, keywords) in axes)
            {
                bool matchedThisAxis = t.Tags.Any(tag => keywords.Any(k => string.Equals(tag.Trim(), k, StringComparison.OrdinalIgnoreCase)));
                if (matchedThisAxis) counts[axis]++;
            }
        }
        
        return trackCount == 0
            ? axes.Keys.ToDictionary(k => k, _ => 0.0)
            : counts.ToDictionary(kv => kv.Key, kv => (double)kv.Value / trackCount);
    }
}
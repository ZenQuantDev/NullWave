using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Helpers;

public static class TagTaxonomy
{
    public static readonly Dictionary<string, string[]> GenreAxes = new()
    {
        ["Electronic"] = new[] { "electronic", "edm", "house", "techno", "dubstep", "synth", "electronica", "electronic music" },
        ["Rock/Metal"] = new[] { "rock", "metal", "punk", "grunge", "alternative" },
        ["Hip-Hop"]    = new[] { "hip hop", "hip-hop", "rap", "trap", "r&b", "rnb", "r and b", "rhythm and blues", "hiphop" },
        ["Pop"]        = new[] { "pop", "k-pop", "indie pop", "synthpop" },
        ["Jazz/Classical"] = new[] { "jazz", "classical", "orchestral", "blues" },
        ["Ambient/Chill"] = new[] { "ambient", "chill", "lofi", "downtempo", "acoustic", "lo-fi", "lo fi", "chillout", "chill out", "chillwave", "mellow" },
        ["Dance"]      = new[] { "dance", "party", "danceable", "disco" },
        ["Synthwave"]  = new[] { "synthwave", "synth", "synth-wave", "retro" }
    };

    public static readonly Dictionary<string, string[]> MoodAxes = new()
    {
        ["Energetic"] = new[] { "energetic", "upbeat", "hype" },
        ["Melancholic"] = new[] { "sad", "melancholy", "moody", "emotional", "melancholic" },
        ["Chill"] = new[] { "chill", "relax", "calm" },
        ["Dark"] = new[] { "dark", "aggressive", "intense", "heavy" },
        ["Happy"] = new[] { "happy", "feel good", "uplifting", "joy" },
        ["Focus"] = new[] { "focus", "study", "instrumental", "concentration" },
        ["Romantic"] = new[] { "romantic", "soul", "funk" },
        ["Dreamy"] = new[] { "dreamy", "nostalgic" }
    };

    private static readonly Dictionary<string, string> AliasMap = BuildAliasMap();

    private static Dictionary<string, string> BuildAliasMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var canonicals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (canonical, keywords) in GenreAxes)
        {
            canonicals.Add(canonical);
            foreach (var kw in keywords) map[kw] = canonical;
        }
        
        foreach (var (canonical, keywords) in MoodAxes)
        {
            canonicals.Add(canonical);
            foreach (var kw in keywords) map[kw] = canonical;
        }
        
        // Map canonical names to themselves (lowercase key -> proper case value)
        // Using the HashSet avoids the "Collection was modified" bug on map.Values
        foreach (var canonical in canonicals)
        {
            map[canonical.ToLowerInvariant()] = canonical;
        }

        return map;
    }

    // FIX (C6): Expose approved tags for External AI prompts
    public static readonly string[] ApprovedTags = AliasMap.Values.Distinct().OrderBy(t => t).ToArray();

    public static string? Normalize(string rawTag)
    {
        if (string.IsNullOrWhiteSpace(rawTag)) return null;
        return AliasMap.TryGetValue(rawTag.Trim(), out var canonical) ? canonical : null;
    }

    public static List<string> NormalizeAll(IEnumerable<string> tags) =>
        tags.Select(Normalize).Where(t => t != null).Select(t => t!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
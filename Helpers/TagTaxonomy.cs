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

    public static Dictionary<string, double> ComputeDistribution(IEnumerable<Track> tracks, Dictionary<string, string[]> axes)
    {
        var counts = axes.Keys.ToDictionary(k => k, _ => 0);
        int matched = 0;
        foreach (var t in tracks)
        {
            if (t.Tags == null) continue;
            bool trackMatched = false;
            foreach (var tag in t.Tags)
            {
                foreach (var (axis, keywords) in axes)
                {
                    if (keywords.Any(k => tag.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    { 
                        counts[axis]++; 
                        trackMatched = true; 
                        break; 
                    }
                }
                if (trackMatched) break; // Count each track only once per axis mapping
            }
            if (trackMatched) matched++;
        }
        
        return matched == 0
            ? axes.Keys.ToDictionary(k => k, _ => 0.0)
            : counts.ToDictionary(kv => kv.Key, kv => (double)kv.Value / matched);
    }
}
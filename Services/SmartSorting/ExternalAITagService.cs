using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class ExternalAITagService
{
    private const int MaxTagsPerTrack = 8;
    private static readonly HashSet<string> TagDenylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "seen live", "favorite", "favourites", "awesome", "amazing"
    };

    //  Approved tag vocabulary (prevents fragmentation across exports) 
    // The AI is instructed to only use these. On import we fuzzy-normalise
    // anything that slipped through anyway.
    public static readonly string[] ApprovedTags =
    {
        "Pop", "Rock", "Hip-Hop", "R&B", "Electronic", "Dance", "Jazz",
        "Classical", "Country", "Metal", "Indie", "Ambient", "Soul", "Funk",
        "Reggae", "Latin", "Alternative", "Folk", "Blues", "Punk",
        "Chill", "Upbeat", "Melancholic", "Energetic", "Romantic",
        "Dark", "Happy", "Aggressive", "Dreamy", "Nostalgic",
        "Synthwave", "Lo-fi", "Trap", "House", "Techno", "Disco"
    };

    // Fuzzy normalisation map: common AI variants → canonical approved tag
    private static readonly Dictionary<string, string> _tagNormalMap =
        new(StringComparer.OrdinalIgnoreCase)
    {
        { "hip hop",        "Hip-Hop"     },
        { "hiphop",         "Hip-Hop"     },
        { "rnb",            "R&B"         },
        { "r and b",        "R&B"         },
        { "rhythm and blues","R&B"        },
        { "edm",            "Electronic"  },
        { "electronica",    "Electronic"  },
        { "electronic music","Electronic" },
        { "chillout",       "Chill"       },
        { "chill out",      "Chill"       },
        { "chillwave",      "Chill"       },
        { "lofi",           "Lo-fi"       },
        { "lo fi",          "Lo-fi"       },
        { "sad",            "Melancholic" },
        { "moody",          "Melancholic" },
        { "mellow",         "Chill"       },
        { "party",          "Dance"       },
        { "danceable",      "Dance"       },
        { "synth",          "Synthwave"   },
        { "synth-wave",     "Synthwave"   },
        { "retro",          "Synthwave"   },
    };

    // Max tracks per export chunk - anything over this gets split into
    // multiple files to stay within LLM output token limits (~4-8k tokens).
    private const int ChunkSize = 50;

    //  Prompt generation 

    public string GeneratePromptContent(IEnumerable<Track> tracksToTag)
    {
        var sb = new StringBuilder();
        AppendSharedInstructions(sb, "plain text");
        sb.AppendLine();
        sb.AppendLine("TRACKS TO ANALYZE:");

        int count = 0;
        foreach (var t in tracksToTag)
        {
            var artist = IsUnknownArtist(t.Artist)
                ? $"(infer from title: \"{t.Title}\")"
                : t.Artist;
            sb.AppendLine($"- ID: {t.Id} | Title: \"{t.Title}\" | Artist: \"{artist}\"");
            count++;
        }

        Log.Information("[ExternalAI] Generated TXT prompt for {Count} tracks", count);
        return sb.ToString();
    }

    public string GenerateMarkdownContent(IEnumerable<Track> tracksToTag)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# NullWave - External AI Tagging Request");
        sb.AppendLine();
        sb.AppendLine("## Instructions");
        AppendSharedInstructions(sb, "Markdown");
        sb.AppendLine();
        sb.AppendLine("## Tracks to Analyze");
        sb.AppendLine();

        int count = 0;
        foreach (var t in tracksToTag)
        {
            var artist = IsUnknownArtist(t.Artist)
                ? $"*(infer from title)*"
                : $"\"{t.Artist}\"";
            sb.AppendLine($"- **ID:** `{t.Id}`  |  **Title:** \"{t.Title}\"  |  **Artist:** {artist}");
            count++;
        }

        sb.AppendLine();
        sb.AppendLine($"*{count} tracks - paste your JSON response back into NullWave → Settings → Smart Features → Import AI JSON.*");

        Log.Information("[ExternalAI] Generated MD prompt for {Count} tracks", count);
        return sb.ToString();
    }

    public string GenerateJsonContent(IEnumerable<Track> tracksToTag)
    {
        var trackList = tracksToTag.Select(t => new
        {
            t.Id,
            Title  = t.Title,
            Artist = IsUnknownArtist(t.Artist) ? $"(infer from title: {t.Title})" : t.Artist
        }).ToList();

        var payload = new
        {
            instructions = new
            {
                role    = "Expert music curator AI",
                task    = "Assign exactly 8 tags to each track from the approved_tags list.",
                rules   = new[]
                {
                    "Reply ONLY with a valid JSON array. No markdown. No extra text.",
                    "Use the exact track IDs provided - do not invent or change them.",
                    "If Artist is Unknown or empty, infer it from the Title field.",
                    "Only use tags from the approved_tags list. If no tag fits, use 'Alternative'."
                },
                approved_tags   = ApprovedTags,
                response_format = new[] { new { Id = "uuid", Tags = new[] { "tag1", "tag2", "tag3" } } }
            },
            tracks = trackList
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        Log.Information("[ExternalAI] Generated JSON export for {Count} tracks", trackList.Count);
        return json;
    }

    /// <summary>
    /// If the track list exceeds ChunkSize, splits into multiple (content, filename) pairs.
    /// The caller saves each chunk with the given filename suggestion.
    /// </summary>
    public List<(string Content, string FileName)> GenerateChunked(
        IList<Track> tracks, string format, string baseFileName)
    {
        var results = new List<(string, string)>();

        if (tracks.Count <= ChunkSize)
        {
            results.Add((GenerateForFormat(tracks, format), baseFileName));
            return results;
        }

        int partNumber = 1;
        for (int i = 0; i < tracks.Count; i += ChunkSize)
        {
            var chunk = tracks.Skip(i).Take(ChunkSize).ToList();
            var ext   = System.IO.Path.GetExtension(baseFileName);
            var stem  = System.IO.Path.GetFileNameWithoutExtension(baseFileName);
            var name  = $"{stem}_part{partNumber}{ext}";
            results.Add((GenerateForFormat(chunk, format), name));
            partNumber++;
        }

        Log.Information("[ExternalAI] Split {Total} tracks into {Parts} export chunks",
            tracks.Count, results.Count);
        return results;
    }

    private string GenerateForFormat(IList<Track> tracks, string format) => format switch
    {
        "md"   => GenerateMarkdownContent(tracks),
        "json" => GenerateJsonContent(tracks),
        _      => GeneratePromptContent(tracks),
    };

    //  Import / Parsing 

    public List<ExternalTagResult> ParseImportedJson(string jsonContent)
    {
        // Step 1: strip markdown fences (LLMs add these despite instructions)
        var clean = StripMarkdownFences(jsonContent);

        // Step 2: if wrapped in an object, unwrap the array
        clean = UnwrapIfObject(clean);

        // Step 3: try full parse first
        var results = TryDeserialize(clean);

        // Step 4: if that failed, attempt truncation recovery
        if (results == null)
        {
            Log.Warning("[ExternalAI] Full parse failed - attempting truncation recovery");
            results = TryRecoverTruncated(clean);
        }

        if (results == null)
        {
            Log.Error("[ExternalAI] Could not parse imported JSON even after recovery");
            return new List<ExternalTagResult>();
        }

        // Step 5: normalise tags against approved vocabulary
        foreach (var r in results)
            r.Tags = r.Tags
                .Select(NormaliseTag)
                .Where(t => t != null && !TagDenylist.Contains(t))
                .Take(MaxTagsPerTrack)
                .Select(t => t!)
                .ToList();

        Log.Information("[ExternalAI] Parsed and normalised {Count} tag results", results.Count);
        return results;
    }

    //  Shared instruction block 

    private static void AppendSharedInstructions(StringBuilder sb, string context)
    {
        sb.AppendLine("You are an expert music curator AI. Assign exactly 8 tags to each track.");
        sb.AppendLine();
        sb.AppendLine("RULES:");
        sb.AppendLine("- Reply ONLY with a valid JSON array. No markdown fences. No extra text.");
        sb.AppendLine("- Use the exact track IDs provided - do not change them.");
        sb.AppendLine("- If the Artist field is 'Unknown' or empty, infer the artist from the Title.");
        sb.AppendLine($"- Only use tags from this approved list:");
        sb.AppendLine($"  {string.Join(", ", ApprovedTags)}");
        sb.AppendLine("- If no approved tag fits, use 'Alternative'.");
        sb.AppendLine();
        sb.AppendLine("EXPECTED FORMAT:");
        sb.AppendLine("[");
        sb.AppendLine("  { \"Id\": \"uuid-here\", \"Tags\": [\"tag1\", \"tag2\", \"tag3\"] }");
        sb.AppendLine("]");
    }

    //  Parsing helpers 

    private static string StripMarkdownFences(string input)
    {
        // Handles ```json ... ```, ``` ... ```, and stray backtick lines
        var stripped = Regex.Replace(input, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase);
        return stripped.Trim();
    }

    private static string UnwrapIfObject(string input)
    {
        var trimmed = input.TrimStart();
        if (!trimmed.StartsWith("{")) return input;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            foreach (var key in new[] { "data", "tags", "results", "tracks", "response" })
            {
                if (doc.RootElement.TryGetProperty(key, out var arr))
                    return arr.GetRawText();
            }
        }
        catch { /* not valid JSON object - fall through */ }

        return input;
    }

    private static List<ExternalTagResult>? TryDeserialize(string json)
    {
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<ExternalTagResult>>(json, opts);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// If the AI hit its token limit, the JSON array is truncated.
    /// We find the last complete object (last '}'), close the array,
    /// and try parsing the salvaged portion.
    /// </summary>
    private static List<ExternalTagResult>? TryRecoverTruncated(string json)
    {
        try
        {
            var lastBrace = json.LastIndexOf('}');
            if (lastBrace < 0) return null;

            var salvaged = json[..(lastBrace + 1)] + "]";
            // The salvaged string may start mid-array - find the opening [
            var firstBracket = salvaged.IndexOf('[');
            if (firstBracket < 0) return null;

            salvaged = salvaged[firstBracket..];
            var result = TryDeserialize(salvaged);

            if (result != null)
                Log.Warning("[ExternalAI] Recovered {Count} entries from truncated JSON", result.Count);

            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps an AI-returned tag to the nearest approved tag.
    /// Returns null if the tag is garbage (empty, too short, numeric).
    /// </summary>
    private static string? NormaliseTag(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 2) return null;

        var trimmed = raw.Trim();

        // Exact match (case-insensitive) against approved list
        var exact = ApprovedTags.FirstOrDefault(
            a => a.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Known alias map
        if (_tagNormalMap.TryGetValue(trimmed, out var mapped)) return mapped;

        // Partial match: if the raw tag contains an approved tag as a substring
        // e.g. "hip-hop music" → "Hip-Hop"
        var partial = ApprovedTags.FirstOrDefault(
            a => trimmed.Contains(a, StringComparison.OrdinalIgnoreCase)
              || a.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        if (partial != null) return partial;

        // Unknown tag - log it and fall back to Alternative
        Log.Debug("[ExternalAI] Unknown tag '{Tag}' → Alternative", trimmed);
        return "Alternative";
    }

    private static bool IsUnknownArtist(string artist) =>
        string.IsNullOrWhiteSpace(artist)
        || artist.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
        || artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase);
}

public class ExternalTagResult
{
    public Guid Id { get; set; }
    public List<string> Tags { get; set; } = new();
}
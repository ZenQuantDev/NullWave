using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.SmartSorting;

public partial class LocalAIService
{
    public async Task<string[]> RankTracksForMoodAsync(string mood, string weather, double temperature, Track[] candidateTracks, int maxResults = 20, CancellationToken ct = default)
    {
        if (candidateTracks == null || candidateTracks.Length == 0) return Array.Empty<string>();
        if (!_isReachable) return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);

        await _aiEngineLock.WaitAsync(ct);
        try
        {
            var activeModel = await ResolveActiveModelAsync();
            var map = candidateTracks.Select((t, i) => new { i, Id = t.Id.ToString() }).ToDictionary(x => x.i, x => x.Id);
            var prompt = BuildIndexedMoodPrompt(mood, weather, temperature, candidateTracks, maxResults);
            
            // FIX (C5): Explicit JSON schema added
            var fullPrompt = prompt + $"\n\nRespond ONLY with a valid JSON object: {{ \"indices\": [3, 0, 7] }}\nRules: integers from the list above, most relevant first, at most {maxResults} entries. No markdown, no explanations.";

            var requestBody = new
            {
                model = activeModel, prompt = fullPrompt, stream = false, format = "json", keep_alive = "5m",
                options = new { 
                    temperature = 0.2, top_p = 0.9, num_predict = 256, 
                    // FIX (C5): Cap num_ctx at 8192
                    num_ctx = Math.Min(8192, Math.Max(4096, 2048 + (120 * candidateTracks.Length))) 
                }
            };

            try
            {
                var response = await _genClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", requestBody, ct);
                if (!response.IsSuccessStatusCode) return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
                _isModelLoaded = true;
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var text = doc.RootElement.GetProperty("response").GetString() ?? "";
                return ParseTrackIndicesFromResponse(text).Where(map.ContainsKey).Select(i => map[i]).ToArray();
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase))
            {
                _isReachable = false;
                return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
            }
            catch { return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults); }
        }
        finally { _aiEngineLock.Release(); }
    }

    public async Task<string[]> GenerateTagsForTrackAsync(string title, string artist, string filePath, CancellationToken ct = default)
    {
        if (!_isReachable) return Array.Empty<string>();
        await _aiEngineLock.WaitAsync(ct);
        try
        {
            var activeModel = await ResolveActiveModelAsync();
            // FIX (C10): Removed File Path from the prompt
            var prompt = $$"""
            You are a deterministic music categorization engine.
            [TRACK METADATA]
            Artist: {{CleanForPrompt(artist)}}
            Title: {{CleanForPrompt(title)}}

            Respond ONLY with a valid JSON object matching this EXACT schema:
            { "tags": ["tag one", "tag two", "tag three"] }
            Rules: 3-8 tags, lowercase genre/mood words, no markdown, no extra keys.
            """;

            var requestBody = new { model = activeModel, prompt = prompt, stream = false, format = "json", keep_alive = "5m", options = new { temperature = 0.4, top_p = 0.9, num_predict = 2048, num_ctx = 4096 } };
            var response = await _genClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", requestBody, ct);
            if (!response.IsSuccessStatusCode) return Array.Empty<string>();
            _isModelLoaded = true;
            
            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var text = CleanMarkdown(doc.RootElement.GetProperty("response").GetString() ?? "");
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            
            using var jsonDoc = JsonDocument.Parse(text);
            return ExtractTagArray(jsonDoc.RootElement);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase)) { _isReachable = false; return Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
        finally { _aiEngineLock.Release(); }
    }

    public async Task<List<string[]>> GenerateTagsBulkAsync(List<(int Index, string Title, string Artist, string FilePath)> tracks, CancellationToken ct = default)
    {
        if (!_isReachable) return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList();
        await _aiEngineLock.WaitAsync(ct);
        try
        {
            var activeModel = await ResolveActiveModelAsync();
            var sb = new StringBuilder();
            foreach (var t in tracks) sb.AppendLine($"{t.Index}. {CleanForPrompt(t.Title)} - {CleanForPrompt(t.Artist)}");
            
            var prompt = $$"""
            You are a deterministic music categorization engine.
            Analyze these tracks:
            {{sb}}
            Respond ONLY with a valid JSON object matching this EXACT schema:
            { "results": [ { "id": 1, "tags": ["tag a", "tag b"] } ] }
            Rules: 3-8 tags per track, lowercase genre/mood words, no markdown, no extra keys.
            """;

            var requestBody = new { model = activeModel, prompt = prompt, stream = false, format = "json", keep_alive = "5m", options = new { temperature = 0.4, top_p = 0.9, num_predict = 4096, num_ctx = Math.Max(4096, 2048 + (160 * tracks.Count)) } };
            var response = await _genClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", requestBody, ct);
            if (!response.IsSuccessStatusCode) return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList();
            _isModelLoaded = true;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var text = CleanMarkdown(doc.RootElement.GetProperty("response").GetString() ?? "");
            using var jsonDoc = JsonDocument.Parse(text);
            
            var map = new Dictionary<int, string[]>();
            if (jsonDoc.RootElement.TryGetProperty("results", out var res) && res.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in res.EnumerateArray())
                    if (item.TryGetProperty("id", out var id)) map[id.GetInt32()] = ExtractTagArray(item);
            }
            return tracks.Select(t => map.TryGetValue(t.Index, out var tags) ? tags : Array.Empty<string>()).ToList();
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase)) { _isReachable = false; return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList(); }
        catch { return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList(); }
        finally { _aiEngineLock.Release(); }
    }

    private static string CleanMarkdown(string s)
    {
        s = s.Trim();
        if (s.StartsWith("```json")) s = s[7..]; else if (s.StartsWith("```")) s = s[3..];
        if (s.EndsWith("```")) s = s[..^3];
        return s.Trim();
    }

    private static string[] ExtractTagArray(JsonElement root)
    {
        var result = new List<string>();
        foreach (var key in new[] { "tags", "genre", "genres", "mood", "moods", "style", "styles" })
        {
            if (!root.TryGetProperty(key, out var el)) continue;
            if (el.ValueKind == JsonValueKind.Array) result.AddRange(el.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)));
            else if (el.ValueKind == JsonValueKind.String) result.AddRange((el.GetString() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return result.Select(t => t.Trim()).Where(t => t.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private string[] GetLocalFallbackRanking(string mood, string weather, Track[] candidates, int max)
    {
        var tokens = (mood + " " + weather).Split(new[] { ' ', ',', ';', '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (tokens.Length == 0) return candidates.Take(max).Select(t => t.Id.ToString()).ToArray();
        return candidates.Select(t => {
            int score = 0;
            foreach (var tag in t.Tags) if (tokens.Any(tok => tag.ToLowerInvariant().Contains(tok))) score += 3;
            foreach (var tok in tokens) { if (t.Title.ToLowerInvariant().Contains(tok)) score++; if (t.Artist.ToLowerInvariant().Contains(tok)) score++; }
            return new { t.Id, score };
        }).Where(x => x.score > 0).OrderByDescending(x => x.score).Select(x => x.Id.ToString()).Concat(candidates.Select(t => t.Id.ToString())).Distinct().Take(max).ToArray();
    }

    private string CleanForPrompt(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace("\"", "'").Replace("{", "[").Replace("}", "]");

    private string BuildIndexedMoodPrompt(string mood, string weather, double temp, Track[] tracks, int max)
    {
        var sb = new StringBuilder(tracks.Length * 120);
        for (int i = 0; i < tracks.Length; i++) sb.AppendLine($"[{i}] Title: \"{CleanForPrompt(tracks[i].Title)}\", Artist: \"{CleanForPrompt(tracks[i].Artist)}\", Tags: [{string.Join(", ", tracks[i].Tags.Take(3))}]");
        return $$"""
        You are a deterministic music recommendation engine. 
        Task: Select track indices from the list below that best fit the context.
        Tracks available:
        {{sb}}
        [DYNAMIC CONTEXT]
        Weather: {{CleanForPrompt(weather)}}
        Temperature: {{temp}}°C
        Target Moods: {{CleanForPrompt(mood)}}
        Max Results Requested: {{max}}
        """;
    }

    // FIX (C5): Tolerant parser that accepts strings, floats, and skips bad data
    private static int[] ParseTrackIndicesFromResponse(string response)
    {
        var clean = CleanMarkdown(response);
        if (string.IsNullOrEmpty(clean)) return Array.Empty<int>();
        try
        {
            using var doc = JsonDocument.Parse(clean);
            JsonElement arr;
            if (doc.RootElement.TryGetProperty("indices", out var p) && p.ValueKind == JsonValueKind.Array) arr = p;
            else if (doc.RootElement.ValueKind == JsonValueKind.Array) arr = doc.RootElement;
            else return Array.Empty<int>();

            return arr.EnumerateArray().Select(e => {
                if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var n)) return (int?)n;
                if (e.ValueKind == JsonValueKind.String && int.TryParse(e.GetString(), out var s)) return (int?)s;
                return (int?)null;
            }).Where(n => n.HasValue).Select(n => n!.Value).Distinct().ToArray();
        }
        catch { return Array.Empty<int>(); }
    }
}
// LocalAIService.cs
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

public class LocalAIService
{
    // Short-timeout client for health checks (ping, tags list)
    private static readonly HttpClient _pingClient = new()
    { Timeout = TimeSpan.FromSeconds(5) };
    
    // Generation client - extended timeout to accommodate CPU token generation cycles on larger footprints
    private static readonly HttpClient _genClient = new()
    { Timeout = TimeSpan.FromMinutes(3) };
    
    private readonly string _ollamaUrl = "http://localhost:11434";

    // Power-aware model switching infrastructure
    private string _batteryModel = "qwen2.5:3b";
    private string _preferredPerformanceModel = "gemma3:4b"; 
    private string _currentModel = "qwen2.5:3b";
    private PowerState _currentPowerState = PowerState.AC;
    private bool _autoPowerSwitch = true;

    /// <summary>
    /// Configure the two models used for power-aware switching.
    /// Call from SettingsViewModel when the user changes the dropdowns.
    /// </summary>
    public void ConfigurePowerModels(
        string batteryModel,
        string performanceModel,
        bool autoSwitch)
    {
        _batteryModel     = batteryModel;
        _preferredPerformanceModel = performanceModel;
        _autoPowerSwitch  = autoSwitch;
        
        // Re-evaluate current model based on new preferences and power state
        if (!_autoPowerSwitch || _currentPowerState == PowerState.AC)
        {
            if (!string.Equals(_currentModel, performanceModel, StringComparison.OrdinalIgnoreCase))
            {
                var old = _currentModel;
                _currentModel = performanceModel;
                if (!string.IsNullOrWhiteSpace(old)) _ = Task.Run(() => UnloadModelAsync(old));
            }
        }
    }

    public string CurrentModel 
    { 
        get => _currentModel; 
        set 
        {
            _preferredPerformanceModel = value;
            
            // If we are on battery and auto-switch is enabled, do NOT clobber the battery model
            if (_autoPowerSwitch && _currentPowerState == PowerState.Battery)
            {
                Log.Debug("[LocalAIService] Ignoring CurrentModel override to '{Requested}' because system is on Battery power.", value);
                return;
            }

            if (!string.Equals(_currentModel, value, StringComparison.OrdinalIgnoreCase))
            {
                var oldModel = _currentModel;
                _currentModel = value;
                if (!string.IsNullOrWhiteSpace(oldModel))
                {
                    _ = Task.Run(async () => await UnloadModelAsync(oldModel));
                }
            }
        } 
    }

    /// <summary>
    /// Called by PowerStateService when power state changes.
    /// Switches CurrentModel automatically if auto-switching is enabled.
    /// </summary>
    public void OnPowerStateChanged(PowerState state)
    {
        _currentPowerState = state;
        
        if (!_autoPowerSwitch) return;
        
        var target = (state == PowerState.Battery ? _batteryModel : _preferredPerformanceModel)?.Trim();
        if (string.IsNullOrEmpty(target) || string.Equals(target, _currentModel?.Trim(), StringComparison.OrdinalIgnoreCase)) 
            return;
        
        var oldModel = _currentModel; 
        _currentModel = target;

        Log.Warning("[LocalAIService] POWER COUPLING EVENT: System shifted to {State}. Swapping tracking target model from '{OldModel}' to '{NewModel}'", 
            state, oldModel, target);
            
        if (!string.IsNullOrWhiteSpace(oldModel))
        {
            _ = Task.Run(async () => await UnloadModelAsync(oldModel));
        }
    }

    // Health / status
    public async Task<bool> PingAsync()
    {
        try
        {
            var response = await _pingClient.GetAsync($"{_ollamaUrl}/");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Warning("[LocalAIService] Ping failed: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<bool> IsOllamaRunningAsync()
    {
        try
        {
            var response = await _pingClient.GetAsync($"{_ollamaUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> IsModelDownloadedAsync(string model)
    {
        try
        {
            var response = await _pingClient.GetAsync($"{_ollamaUrl}/api/tags");
            if (!response.IsSuccessStatusCode) return false;
            
            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            
            // OPTIMIZED: Hoisted split to eliminate micro-allocations inside the foreach loop
            var targetPrefix = model.Split(':')[0];
            
            var models = doc.RootElement.GetProperty("models");
            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetProperty("name").GetString();
                if (name?.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase) == true)
                    return true;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Forces Ollama to immediately evict the model from RAM/VRAM.
    /// Sending keep_alive=0 is the official Ollama API approach.
    /// </summary>
    public async Task UnloadModelAsync(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;
        try
        {
            var payload = new { model = modelName, keep_alive = 0 };
            var response = await _pingClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", payload);
            if (response.IsSuccessStatusCode)
                Log.Information("[LocalAIService] Evicted '{Model}' from RAM", modelName);
        }
        catch (Exception ex)
        {
            Log.Warning("[LocalAIService] Failed to unload {Model}: {Message}", modelName, ex.Message);
        }
    }

    // Model download
    public async Task DownloadModelAsync(
        string model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var payload = new { name = model, stream = true };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_ollamaUrl}/api/pull")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _genClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("total", out var totalProp) && 
                doc.RootElement.TryGetProperty("completed", out var completedProp))
            {
                double total = totalProp.GetInt64();
                double completed = completedProp.GetInt64();
                if (total > 0)
                {
                    progress?.Report(completed / total);
                }
            }
        }
        progress?.Report(1.0);
    }

    // Track ranking Suite
    public async Task<string[]> RankTracksForMoodAsync(
        string mood,
        string weather,
        double temperature,
        Track[] candidateTracks,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        if (candidateTracks == null || candidateTracks.Length == 0) return Array.Empty<string>();

        // Map track array positions to their source string IDs
        var indexToIdMap = candidateTracks
            .Select((track, idx) => new { idx, Id = track.Id.ToString() })
            .ToDictionary(x => x.idx, x => x.Id);

        var prompt = BuildIndexedMoodPrompt(mood, weather, temperature, candidateTracks, maxResults);
        var requestBody = new
        {
            model = _currentModel,
            prompt = prompt,
            stream = false,
            format = "json",
            options = new 
            { 
                temperature = 0.6, 
                top_p = 0.9, 
                num_predict = 150,
                num_thread = Math.Max(1, Environment.ProcessorCount / 2 - 1)
            }
        };
        
        try
        {
            Log.Debug("[LocalAI] Requesting fast indexed ranking using model: '{Model}' for {TrackCount} candidates", _currentModel, candidateTracks.Length);

            var response = await _genClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", requestBody, ct);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorResponse = await response.Content.ReadAsStringAsync(ct);
                Log.Error("[LocalAI] Ollama API returned HTTP {StatusCode}: {ErrorBody}. Falling back to local keyword sorting.", (int)response.StatusCode, errorResponse);
                return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
            }

            using var responseStream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);
            var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";
            
            var orderedIndices = ParseTrackIndicesFromResponse(responseText);
            
            return orderedIndices
                .Where(indexToIdMap.ContainsKey)
                .Select(idx => indexToIdMap[idx])
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LocalAI] Critical failure or timeout during local AI calculation. Falling back to local keyword sorting.");
            return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
        }
    }

    /// <summary>
    /// Resilient, low-overhead fallback engine that scores and matches tracks via 
    /// local keyword intersection when Ollama is unavailable.
    /// </summary>
    private string[] GetLocalFallbackRanking(string mood, string weather, Track[] candidateTracks, int maxResults)
    {
        Log.Information("[LocalAIService] Executing ultra-fast local memory fallback matching for requested context.");

        // Normalize string parameters into searchable atomic tokens
        var filterTokens = (mood + " " + weather)
            .Split(new[] { ' ', ',', ';', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (filterTokens.Length == 0)
        {
            return candidateTracks.Take(maxResults).Select(t => t.Id.ToString()).ToArray();
        }

        return candidateTracks
            .Select(track =>
            {
                int matchScore = 0;

                // Priority 1: Direct Tag matches (Heavy weight)
                foreach (var tag in track.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    var normalizedTag = tag.ToLowerInvariant();
                    if (filterTokens.Any(token => normalizedTag.Contains(token)))
                        matchScore += 3;
                }

                // Priority 2: Text inclusion in Title or Artist details (Lighter weight)
                var titleLower = track.Title?.ToLowerInvariant() ?? "";
                var artistLower = track.Artist?.ToLowerInvariant() ?? "";
                foreach (var token in filterTokens)
                {
                    if (titleLower.Contains(token)) matchScore += 1;
                    if (artistLower.Contains(token)) matchScore += 1;
                }

                return new { TrackId = track.Id.ToString(), Score = matchScore };
            })
            .Where(scoredTrack => scoredTrack.Score > 0)
            .OrderByDescending(scoredTrack => scoredTrack.Score)
            .Select(scoredTrack => scoredTrack.TrackId)
            // Safety Check: Concat all track IDs to ensure we fill maxResults even if keyword intersection is thin
            .Concat(candidateTracks.Select(t => t.Id.ToString()))
            .Distinct()
            .Take(maxResults)
            .ToArray();
    }

    private string BuildIndexedMoodPrompt(string mood, string weather, double temp, Track[] tracks, int maxResults)
    {
        int estimatedCapacity = tracks.Length * 120;
        var trackList = new StringBuilder(estimatedCapacity);
        
        for (int i = 0; i < tracks.Length; i++)
        {
            trackList.AppendLine($"[{i}] Title: \"{tracks[i].Title}\", Artist: \"{tracks[i].Artist}\", Tags: [{string.Join(", ", tracks[i].Tags.Take(3))}]");
        }

        return $$"""
                You are a professional music recommendation engine.
                Task: Select track indices from the list below that best fit the context provided at the end of this prompt.
                Response Format: Return ONLY a JSON object containing an array of integers assigned to an "indices" key. Example: { "indices": [4, 12, 0] }. Do not include markdown code wrappers.

                Tracks available:
                {{trackList}}

                [DYNAMIC CONTEXT]
                Weather: {{weather}}
                Temperature: {{temp}}°C
                Target Moods: {{mood}}
                Max Results Requested: {{maxResults}}
                """;
    }

    private static int[] ParseTrackIndicesFromResponse(string response)
    {
        try
        {
            using var doc = JsonDocument.Parse(response);
            
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("indices", out var indicesArray) && 
                indicesArray.ValueKind == JsonValueKind.Array)
            {
                return indicesArray.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Number)
                    .Select(e => e.GetInt32())
                    .ToArray();
            }
            
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.Number)
                    .Select(e => e.GetInt32())
                    .ToArray();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LocalAI] Resilient index parser failed to process response payload: {Response}", response);
        }
        return Array.Empty<int>();
    }
}
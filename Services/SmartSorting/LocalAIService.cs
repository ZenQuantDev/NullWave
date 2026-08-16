using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NullWave.Models;
using Serilog;

namespace NullWave.Services.SmartSorting;

public class LocalAIService : IDisposable
{
    private static readonly HttpClient _pingClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly HttpClient _genClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly string _ollamaUrl = "http://localhost:11434";
    private static readonly SemaphoreSlim _aiEngineLock = new(1, 1);

    private readonly Channel<Func<Task>> _stateQueue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions
    {
        SingleReader = true,
        AllowSynchronousContinuations = false
    });

    private string _batteryModel = "qwen2.5:3b";
    private string _preferredPerformanceModel = "gemma3:4b";

    private volatile string _currentModel = "qwen2.5:3b";
    private volatile PowerState _currentPowerState = PowerState.AC;
    private volatile bool _autoPowerSwitch = true;
    
    private volatile bool _isReachable = true;
    public bool IsReachable => _isReachable;

    private readonly CancellationTokenSource _cts = new();

    public event Action<string>? FallbackNotice;

    public LocalAIService()
    {
        _ = StartQueueProcessorAsync();
        _ = StartHealingPingAsync(_cts.Token);
    }

    public void Shutdown()
    {
        _cts.Cancel();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task StartQueueProcessorAsync()
    {
        var reader = _stateQueue.Reader;
        while (await reader.WaitToReadAsync())
        {
            while (reader.TryRead(out var stateMutationTask))
            {
                try
                {
                    await stateMutationTask();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[LocalAIService] Critical error during sequential state execution pipeline.");
                }
            }
        }
    }

    private async Task StartHealingPingAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (!_isReachable)
            {
                Log.Debug("[LocalAIService] Running background healing ping...");
                var isUp = await PingAsync();
                if (isUp)
                {
                    Log.Information("[LocalAIService] Healing ping succeeded. Local AI is back online.");
                    FallbackNotice?.Invoke("Local AI connection restored! AI features are back online.");
                }
            }
        }
    }

    public void ConfigurePowerModels(string batteryModel, string performanceModel, bool autoSwitch)
    {
        _stateQueue.Writer.TryWrite(async () =>
        {
            await UpdateStateAndApplyAsync("Configuration Update", () =>
            {
                _batteryModel = batteryModel;
                _preferredPerformanceModel = performanceModel;
                _autoPowerSwitch = autoSwitch;
            });
        });
    }

    public string CurrentModel
    {
        get => _currentModel;
        set
        {
            if (_autoPowerSwitch && _currentPowerState == PowerState.Battery)
            {
                Log.Debug("[LocalAIService] Ignoring CurrentModel override to '{Requested}' because system is on Battery power.", value);
                return;
            }

            var newValue = value?.Trim();
            if (string.IsNullOrEmpty(newValue)) return;

            if (!string.Equals(_currentModel, newValue, StringComparison.OrdinalIgnoreCase))
            {
                var oldModel = _currentModel;
                _currentModel = newValue; 
                _stateQueue.Writer.TryWrite(async () =>
                {
                    await _aiEngineLock.WaitAsync();
                    try
                    {
                        Log.Warning("[LocalAIService] [Manual Override] Swapping models safely from '{Old}' to '{New}'...", oldModel, newValue);
                        if (!string.IsNullOrWhiteSpace(oldModel))
                        {
                            await UnloadModelAsync(oldModel);
                        }
                    }
                    finally
                    {
                        _aiEngineLock.Release();
                    }
                });
            }
        }
    }

    public void OnPowerStateChanged(PowerState state)
    {
        _stateQueue.Writer.TryWrite(async () =>
        {
            await UpdateStateAndApplyAsync($"Power Shift to {state}", () => _currentPowerState = state);
        });
    }

    private async Task UpdateStateAndApplyAsync(string contextSource, Action stateMutation)
    {
        await _aiEngineLock.WaitAsync();
        try
        {
            stateMutation();

            string? targetModel = (_autoPowerSwitch && _currentPowerState == PowerState.Battery)
                ? _batteryModel
                : _preferredPerformanceModel;

            targetModel = targetModel?.Trim();
            if (string.IsNullOrEmpty(targetModel)) return;

            if (!string.Equals(_currentModel, targetModel, StringComparison.OrdinalIgnoreCase))
            {
                var oldModel = _currentModel;
                _currentModel = targetModel; 
                Log.Warning("[LocalAIService] [{Source}] Swapping models safely from '{Old}' to '{New}'...", contextSource, oldModel, targetModel);
                if (!string.IsNullOrWhiteSpace(oldModel))
                {
                    await UnloadModelAsync(oldModel);
                }
            }
        }
        finally
        {
            _aiEngineLock.Release();
        }
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            var response = await _pingClient.GetAsync($"{_ollamaUrl}/");
            if (response.IsSuccessStatusCode)
            {
                _isReachable = true; 
                return true;
            }
            return false;
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
            var targetModel = model.Contains(':') ? model : $"{model}:latest";
            var models = doc.RootElement.GetProperty("models");
            return models.EnumerateArray().Any(m =>
                string.Equals(m.GetProperty("name").GetString(), targetModel, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public async Task UnloadModelAsync(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return;
        try
        {
            var payload = new { model = modelName, prompt = "", keep_alive = 0 };
            var response = await _pingClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", payload);
            if (response.IsSuccessStatusCode)
                Log.Information("[LocalAIService] Evicted '{Model}' from RAM", modelName);
        }
        catch (Exception ex)
        {
            Log.Warning("[LocalAIService] Failed to unload {Model}: {Message}", modelName, ex.Message);
        }
    }

    public async Task DownloadModelAsync(string model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var payload = new { name = model, stream = true };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_ollamaUrl}/api/pull")
        {
            Content = JsonContent.Create(payload)
        };

        using var response = await _genClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

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

    public async Task<string[]> RankTracksForMoodAsync(
        string mood, string weather, double temperature,
        Track[] candidateTracks, int maxResults = 20, CancellationToken ct = default)
    {
        if (candidateTracks == null || candidateTracks.Length == 0) return Array.Empty<string>();
        
        if (!_isReachable)
        {
            Log.Debug("[LocalAI] Skipping AI ranking - circuit breaker open (Ollama unreachable).");
            return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
        }

        await _aiEngineLock.WaitAsync(ct);
        try
        {
            var indexToIdMap = candidateTracks
                .Select((track, idx) => new { idx, Id = track.Id.ToString() })
                .ToDictionary(x => x.idx, x => x.Id);

            var prompt = BuildIndexedMoodPrompt(mood, weather, temperature, candidateTracks, maxResults);

            var requestBody = new
            {
                model = _currentModel,
                prompt = prompt + "\n\nRespond ONLY with a valid JSON object matching the schema. Do not include markdown formatting or explanations.",
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0.2,
                    top_p = 0.9,
                    num_predict = 4096, 
                    num_ctx = Math.Max(4096, 2048 + (120 * candidateTracks.Length))
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
                    FallbackNotice?.Invoke($"Local AI request failed (HTTP {(int)response.StatusCode}) — used keyword-based sorting instead.");
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
            catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) || (ex.InnerException?.Message?.Contains("refused", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                _isReachable = false;
                Log.Warning("[LocalAI] Ollama connection refused. Circuit breaker activated - disabling AI fallback for this session.");
                FallbackNotice?.Invoke("Local AI is unreachable (Connection Refused). AI ranking disabled for this session.");
                return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[LocalAI] Critical failure or timeout during local AI calculation. Falling back to local keyword sorting.");
                FallbackNotice?.Invoke("Local AI timed out or was unreachable — used keyword-based sorting instead.");
                return GetLocalFallbackRanking(mood, weather, candidateTracks, maxResults);
            }
        }
        finally
        {
            _aiEngineLock.Release();
        }
    }

    public async Task<string[]> GenerateTagsForTrackAsync(string title, string artist, string filePath, CancellationToken ct = default)
    {
        if (!_isReachable) return Array.Empty<string>();

        await _aiEngineLock.WaitAsync(ct);
        try
        {
            var prompt = $$"""
            You are a deterministic music categorization engine. Your task is to analyze the provided track details and output tags.
            
            [TRACK METADATA]
            Artist: {{CleanForPrompt(artist)}}
            Title: {{CleanForPrompt(title)}}
            File Path: {{CleanForPrompt(filePath)}}
            
            Respond ONLY with a valid JSON object matching the schema. Do not include markdown formatting.
            """;

            var requestBody = new
            {
                model = _currentModel,
                prompt = prompt,
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0.4,
                    top_p = 0.9,
                    num_predict = 2048,
                    num_ctx = 4096
                }
            };

            try
            {
                Log.Debug("[LocalAI] Requesting single tag generation using model: '{Model}' for '{Title}'", _currentModel, title);
                var response = await _genClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", requestBody, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[LocalAI] Tag generation endpoint failed with HTTP status code: {StatusCode}", response.StatusCode);
                    return Array.Empty<string>();
                }

                using var responseStream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);
                var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";

                // FIX: Strip markdown code blocks that LLMs often add despite instructions
                var cleanResponse = responseText.Trim();
                if (cleanResponse.StartsWith("```json")) cleanResponse = cleanResponse.Substring(7);
                else if (cleanResponse.StartsWith("```")) cleanResponse = cleanResponse.Substring(3);
                if (cleanResponse.EndsWith("```")) cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                cleanResponse = cleanResponse.Trim();

                if (string.IsNullOrEmpty(cleanResponse))
                {
                    Log.Warning("[LocalAI] Empty response received for tag generation of '{Title}'", title);
                    return Array.Empty<string>();
                }

                using var jsonDoc = JsonDocument.Parse(cleanResponse);
                
                // FIX: Use TryGetProperty to prevent KeyNotFoundException if schema is unexpected
                if (jsonDoc.RootElement.TryGetProperty("tags", out var tagsProp) && tagsProp.ValueKind == JsonValueKind.Array)
                {
                    return tagsProp.EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToArray();
                }

                Log.Warning("[LocalAI] Unexpected JSON structure for tag generation of '{Title}': {Response}", title, cleanResponse);
                return Array.Empty<string>();
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) || (ex.InnerException?.Message?.Contains("refused", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                _isReachable = false;
                Log.Warning("[LocalAI] Ollama connection refused during tag generation. Circuit breaker activated.");
                FallbackNotice?.Invoke("Local AI is unreachable (Connection Refused). AI tagging disabled for this session.");
                return Array.Empty<string>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[LocalAI] Error executing local AI tag fallback generation for '{Title}'", title);
            }

            return Array.Empty<string>();
        }
        finally
        {
            _aiEngineLock.Release();
        }
    }

    public async Task<List<string[]>> GenerateTagsBulkAsync(List<(int Index, string Title, string Artist, string FilePath)> tracks, CancellationToken ct = default)
    {
        if (!_isReachable) return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList();

        await _aiEngineLock.WaitAsync(ct);
        try
        {
            var trackList = new StringBuilder();
            foreach (var track in tracks)
            {
                trackList.AppendLine($"{track.Index}. {CleanForPrompt(track.Title)} - {CleanForPrompt(track.Artist)}");
            }

            var prompt = $$"""
            You are a deterministic music categorization engine. Your task is to analyze the provided track details and output tags.
            
            Analyze these tracks:
            {{trackList}}
            
            Respond ONLY with a valid JSON object matching the schema. Do not include markdown formatting.
            """;

            var requestBody = new
            {
                model = _currentModel,
                prompt = prompt,
                stream = false,
                format = "json",
                options = new
                {
                    temperature = 0.4,
                    top_p = 0.9,
                    num_predict = 4096,
                    num_ctx = Math.Max(4096, 2048 + (160 * tracks.Count))
                }
            };

            try
            {
                Log.Debug("[LocalAI] Requesting bulk tag generation for {Count} tracks using model: '{Model}'", tracks.Count, _currentModel);
                var response = await _genClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", requestBody, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("[LocalAI] Bulk tag generation endpoint failed with HTTP status code: {StatusCode}", response.StatusCode);
                    return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList();
                }

                using var responseStream = await response.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct);
                var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";

                // FIX: Strip markdown code blocks
                var cleanResponse = responseText.Trim();
                if (cleanResponse.StartsWith("```json")) cleanResponse = cleanResponse.Substring(7);
                else if (cleanResponse.StartsWith("```")) cleanResponse = cleanResponse.Substring(3);
                if (cleanResponse.EndsWith("```")) cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                cleanResponse = cleanResponse.Trim();

                using var jsonDoc = JsonDocument.Parse(cleanResponse);
                var resultMap = new Dictionary<int, string[]>();

                // FIX: Safe property access
                if (jsonDoc.RootElement.TryGetProperty("results", out var resultsProp) && resultsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in resultsProp.EnumerateArray())
                    {
                        if (item.TryGetProperty("id", out var idProp) && 
                            item.TryGetProperty("tags", out var tagsProp) && 
                            tagsProp.ValueKind == JsonValueKind.Array)
                        {
                            int id = idProp.GetInt32();
                            var tags = tagsProp.EnumerateArray()
                                .Select(e => e.GetString() ?? string.Empty)
                                .Where(s => !string.IsNullOrEmpty(s))
                                .ToArray();
                            resultMap[id] = tags;
                        }
                    }
                }
                else
                {
                    Log.Warning("[LocalAI] Unexpected JSON structure for bulk tag generation: {Response}", cleanResponse);
                }

                var finalResults = new List<string[]>();
                foreach (var track in tracks)
                {
                    if (resultMap.TryGetValue(track.Index, out var tags))
                    {
                        finalResults.Add(tags);
                    }
                    else
                    {
                        finalResults.Add(Array.Empty<string>());
                    }
                }
                return finalResults;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase) || (ex.InnerException?.Message?.Contains("refused", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                _isReachable = false;
                Log.Warning("[LocalAI] Ollama connection refused during bulk tag generation. Circuit breaker activated.");
                FallbackNotice?.Invoke("Local AI is unreachable (Connection Refused). Bulk AI tagging disabled for this session.");
                return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[LocalAI] Error executing bulk local AI tag generation");
            }

            return Enumerable.Repeat(Array.Empty<string>(), tracks.Count).ToList();
        }
        finally
        {
            _aiEngineLock.Release();
        }
    }

    private string[] GetLocalFallbackRanking(string mood, string weather, Track[] candidateTracks, int maxResults)
    {
        Log.Information("[LocalAIService] Executing ultra-fast local memory fallback matching for requested context.");

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
                foreach (var tag in track.Tags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;
                    var normalizedTag = tag.ToLowerInvariant();
                    if (filterTokens.Any(token => normalizedTag.Contains(token)))
                        matchScore += 3;
                }

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
            .Concat(candidateTracks.Select(t => t.Id.ToString()))
            .Distinct()
            .Take(maxResults)
            .ToArray();
    }

    private string CleanForPrompt(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Replace("\"", "'").Replace("{", "[").Replace("}", "]");
    }

    private string BuildIndexedMoodPrompt(string mood, string weather, double temp, Track[] tracks, int maxResults)
    {
        int estimatedCapacity = tracks.Length * 120;
        var trackList = new StringBuilder(estimatedCapacity);

        for (int i = 0; i < tracks.Length; i++)
        {
            trackList.AppendLine($"[{i}] Title: \"{CleanForPrompt(tracks[i].Title)}\", Artist: \"{CleanForPrompt(tracks[i].Artist)}\", Tags: [{string.Join(", ", tracks[i].Tags.Take(3))}]");
        }

        return $$"""
        You are a deterministic music recommendation engine. 
        
        Task: Select track indices from the list below that best fit the context.
        
        Tracks available:
        {{trackList}}
        
        [DYNAMIC CONTEXT]
        Weather: {{CleanForPrompt(weather)}}
        Temperature: {{temp}}°C
        Target Moods: {{CleanForPrompt(mood)}}
        Max Results Requested: {{maxResults}}
        """;
    }

    private static int[] ParseTrackIndicesFromResponse(string response)
    {
        var cleanResponse = response.Trim();
        if (cleanResponse.StartsWith("```json")) cleanResponse = cleanResponse.Substring(7);
        else if (cleanResponse.StartsWith("```")) cleanResponse = cleanResponse.Substring(3);
        if (cleanResponse.EndsWith("```")) cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
        cleanResponse = cleanResponse.Trim();

        if (string.IsNullOrEmpty(cleanResponse)) return Array.Empty<int>();

        try
        {
            using var doc = JsonDocument.Parse(cleanResponse);
            
            if (doc.RootElement.TryGetProperty("indices", out var indicesProp) && indicesProp.ValueKind == JsonValueKind.Array)
            {
                return indicesProp.EnumerateArray().Select(e => e.GetInt32()).ToArray();
            }
            
            // Fallback if model wraps it in an object differently or returns a raw array
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                return doc.RootElement.EnumerateArray().Select(e => e.GetInt32()).ToArray();
            }
        }
        catch (JsonException)
        {
            // Invalid JSON, return empty
        }
        
        return Array.Empty<int>();
    }
}
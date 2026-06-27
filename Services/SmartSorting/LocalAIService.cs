using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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

    // Generation client - longer timeout, but bounded so we don't wait forever.
    // 60s is enough for a 7B model on a modern CPU; 100s was too generous and
    // caused the timeout errors shown in the logs.
    private static readonly HttpClient _genClient = new()
        { Timeout = TimeSpan.FromSeconds(60) };

    private readonly string _ollamaUrl = "http://localhost:11434";

    // ── Active model ─────────────────────────────────────────────────────────
    private string _currentModel = "qwen2.5:7b";
    public string CurrentModel
    {
        get => _currentModel;
        set => _currentModel = value;
    }

    // ── Power-aware model switching ──────────────────────────────────────────
    private string _batteryModel     = "qwen2.5:3b";
    private string _performanceModel = "qwen2.5:7b";
    private bool   _autoPowerSwitch  = false;

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
        _performanceModel = performanceModel;
        _autoPowerSwitch  = autoSwitch;
    }

    /// <summary>
    /// Called by PowerStateService when power state changes.
    /// Switches CurrentModel automatically if auto-switching is enabled.
    /// </summary>
    public void OnPowerStateChanged(PowerState state)
    {
        if (!_autoPowerSwitch) return;

        var target = state == PowerState.Battery ? _batteryModel : _performanceModel;
        if (target == _currentModel) return;

        _currentModel = target;
        Log.Information("[LocalAIService] Power state changed to {State} → switching model to {Model}",
            state, target);
    }

    // ── Health / status ──────────────────────────────────────────────────────

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

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var models = doc.RootElement.GetProperty("models");

            foreach (var m in models.EnumerateArray())
            {
                var name = m.GetProperty("name").GetString();
                if (name?.StartsWith(model.Split(':')[0]) == true)
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
            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            await _genClient.PostAsync($"{_ollamaUrl}/api/generate", content);
            Log.Information("[LocalAIService] Evicted '{Model}' from RAM", modelName);
        }
        catch (Exception ex)
        {
            Log.Warning("[LocalAIService] Failed to unload {Model}: {Message}", modelName, ex.Message);
        }
    }

    // ── Model download ────────────────────────────────────────────────────────

    public async Task DownloadModelAsync(
        string model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "ollama",
            Arguments              = $"pull {model}",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi)
            ?? throw new Exception("Failed to start ollama process");

        _ = Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync();
                if (line != null && line.Contains("pulling"))
                {
                    var percentStart = line.IndexOf('%');
                    if (percentStart > 0)
                    {
                        var numStart = line.LastIndexOf(' ', percentStart - 1) + 1;
                        if (double.TryParse(
                                line.Substring(numStart, percentStart - numStart),
                                out double pct))
                            progress?.Report(pct / 100.0);
                    }
                }
            }
        }, ct);

        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            var error = await proc.StandardError.ReadToEndAsync();
            throw new Exception($"Model download failed: {error}");
        }

        progress?.Report(1.0);
    }

    // ── Track ranking ─────────────────────────────────────────────────────────

    public async Task<string[]> RankTracksForMoodAsync(
        string mood,
        string weather,
        double temperature,
        Track[] candidateTracks,
        int maxResults = 20,
        CancellationToken ct = default)
    {
        var prompt = BuildMoodPrompt(mood, weather, temperature, candidateTracks, maxResults);

        var requestBody = new
        {
            model  = _currentModel,
            prompt = prompt,
            stream = false,
            options = new { temperature = 0.7, top_p = 0.9, num_predict = 500 }
        };

        var content = new StringContent(
            JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        try
        {
            var response = await _genClient.PostAsync($"{_ollamaUrl}/api/generate", content, ct);
            response.EnsureSuccessStatusCode();

            var resultJson = await response.Content.ReadAsStringAsync(ct);
            using var doc  = JsonDocument.Parse(resultJson);
            var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";

            return ParseTrackIdsFromResponse(responseText, candidateTracks);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LocalAI] Failed to rank tracks");
            return Array.Empty<string>();
        }
    }

    private string BuildMoodPrompt(
        string mood, string weather, double temp,
        Track[] tracks, int maxResults)
    {
        var trackList = new StringBuilder();
        foreach (var track in tracks)
            trackList.AppendLine(
                $"- ID: {track.Id}, Title: \"{track.Title}\", " +
                $"Artist: \"{track.Artist}\", Tags: [{string.Join(", ", track.Tags)}], " +
                $"Plays: {track.PlayCount}");

        return $"""
You are a music recommendation AI. Given the current weather and mood, rank the following tracks.

Weather: {weather}, {temp:F0}°C
Mood: {mood}

Tracks:
{trackList}
Return ONLY a JSON array of track IDs, most suitable first. Max {maxResults} IDs.
Example: ["id1","id2","id3"]

Response:
""";
    }

    private static string[] ParseTrackIdsFromResponse(string response, Track[] tracks)
    {
        try
        {
            var start = response.IndexOf('[');
            var end   = response.LastIndexOf(']');
            if (start < 0 || end <= start) return Array.Empty<string>();

            var ids = JsonSerializer.Deserialize<string[]>(
                response.Substring(start, end - start + 1));

            if (ids == null) return Array.Empty<string>();

            return ids
                .Where(id => Guid.TryParse(id, out var g) && tracks.Any(t => t.Id == g))
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LocalAI] Failed to parse AI response");
            return Array.Empty<string>();
        }
    }
}
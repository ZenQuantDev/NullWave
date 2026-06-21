using System;
using System.Diagnostics;
using System.IO;
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
    private readonly HttpClient _http = new();
    private readonly string _ollamaUrl = "http://localhost:11434";
    private string _currentModel = "qwen2.5:7b";

    public string CurrentModel
    {
        get => _currentModel;
        set => _currentModel = value;
    }

    public async Task<bool> IsOllamaRunningAsync()
    {
        try
        {
            var response = await _http.GetAsync($"{_ollamaUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsModelDownloadedAsync(string model)
    {
        try
        {
            var response = await _http.GetAsync($"{_ollamaUrl}/api/tags");
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
        catch
        {
            return false;
        }
    }

    public async Task DownloadModelAsync(string model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ollama",
            Arguments = $"pull {model}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc == null)
        {
            throw new Exception("Failed to start ollama process");
        }

        // Read output for progress
        _ = Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync();
                if (line != null && line.Contains("pulling"))
                {
                    // Parse progress from line like "pulling manifest... 50%"
                    var percentStart = line.IndexOf('%');
                    if (percentStart > 0)
                    {
                        var numStart = line.LastIndexOf(' ', percentStart - 1) + 1;
                        if (double.TryParse(line.Substring(numStart, percentStart - numStart), out double pct))
                        {
                            progress?.Report(pct / 100.0);
                        }
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
            model = _currentModel,
            prompt = prompt,
            stream = false,
            options = new
            {
                temperature = 0.7,
                top_p = 0.9,
                num_predict = 500
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await _http.PostAsync($"{_ollamaUrl}/api/generate", content, ct);
            response.EnsureSuccessStatusCode();

            var resultJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(resultJson);
            var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";

            return ParseTrackIdsFromResponse(responseText, candidateTracks);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[LocalAI] Failed to rank tracks");
            return Array.Empty<string>();
        }
    }

    private string BuildMoodPrompt(string mood, string weather, double temp, Track[] tracks, int maxResults)
    {
        var trackList = new StringBuilder();
        foreach (var track in tracks)
        {
            trackList.AppendLine($"- ID: {track.Id}, Title: \"{track.Title}\", Artist: \"{track.Artist}\", Tags: [{string.Join(", ", track.Tags)}], Plays: {track.PlayCount}");
        }

        return $@"You are a music recommendation AI. Given the current weather and mood, rank the following tracks from most to least suitable.

Current weather: {weather}
Temperature: {temp}°C
Desired mood: {mood}

Available tracks:
{trackList}

Return ONLY a JSON array of track IDs in order of suitability (most suitable first). Return at most {maxResults} IDs.
Example format: [""id1"", ""id2"", ""id3""]

Your response:";
    }

    private string[] ParseTrackIdsFromResponse(string response, Track[] tracks)
    {
        try
        {
            // Extract JSON array from response
            var start = response.IndexOf('[');
            var end = response.LastIndexOf(']');
            if (start < 0 || end < 0 || end <= start)
                return Array.Empty<string>();

            var jsonArray = response.Substring(start, end - start + 1);
            var ids = JsonSerializer.Deserialize<string[]>(jsonArray);
            
            if (ids == null) return Array.Empty<string>();

            // Validate IDs exist in candidate tracks
            var validIds = new System.Collections.Generic.List<string>();
            foreach (var id in ids)
            {
                if (Guid.TryParse(id, out var guid) && tracks.Any(t => t.Id == guid))
                {
                    validIds.Add(id);
                }
            }

            return validIds.ToArray();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[LocalAI] Failed to parse AI response");
            return Array.Empty<string>();
        }
    }
}
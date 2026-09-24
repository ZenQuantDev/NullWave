using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services.SmartSorting;

public partial class LocalAIService
{
    public async Task<bool> PingAsync()
    {
        try
        {
            var response = await _pingClient.GetAsync($"{_ollamaUrl}/");
            if (response.IsSuccessStatusCode) { _isReachable = true; return true; }
            return false;
        }
        catch (Exception ex) { Log.Warning("[LocalAIService] Ping failed: {Message}", ex.Message); return false; }
    }

    public async Task<bool> IsOllamaRunningAsync()
    {
        try { return (await _pingClient.GetAsync($"{_ollamaUrl}/api/tags")).IsSuccessStatusCode; }
        catch { return false; }
    }

    private async Task<List<string>> GetInstalledModelsAsync()
    {
        try
        {
            var response = await _pingClient.GetAsync($"{_ollamaUrl}/api/tags");
            if (!response.IsSuccessStatusCode) return new List<string>();
            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return new List<string>();
            return models.EnumerateArray().Where(m => m.TryGetProperty("name", out _)).Select(m => m.GetProperty("name").GetString() ?? "").Where(n => !string.IsNullOrEmpty(n)).ToList();
        }
        catch { return new List<string>(); }
    }

    public async Task<bool> IsModelDownloadedAsync(string model)
    {
        var installed = await GetInstalledModelsAsync();
        var targetModel = model.Contains(':') ? model : $"{model}:latest";
        return installed.Any(i => string.Equals(i, targetModel, StringComparison.OrdinalIgnoreCase) || string.Equals(i, model, StringComparison.OrdinalIgnoreCase) || i.StartsWith(model + ":", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> ResolveActiveModelAsync()
    {
        if (await IsModelDownloadedAsync(_currentModel)) return _currentModel;
        var installed = await GetInstalledModelsAsync();
        if (installed.Count == 0) { Log.Warning("[LocalAI] Configured model '{Model}' not found, no fallbacks.", _currentModel); return _currentModel; }

        var catalogMatches = AIModelCatalog.All.Where(m => installed.Any(i => i.StartsWith(m.OllamaId, StringComparison.OrdinalIgnoreCase) || string.Equals(i, m.OllamaId, StringComparison.OrdinalIgnoreCase))).ToList();
        if (catalogMatches.Count > 0)
        {
            var best = catalogMatches.OrderByDescending(m => m.ParametersBillions <= 8 ? 1 : 0).ThenByDescending(m => m.ParametersBillions).First();
            var match = installed.First(i => i.StartsWith(best.OllamaId, StringComparison.OrdinalIgnoreCase) || string.Equals(i, best.OllamaId, StringComparison.OrdinalIgnoreCase));
            Log.Information("[LocalAI] Auto-switching to safe installed model '{Fallback}' ({Params}B).", match, best.ParametersBillions);
            _currentModel = match; return match;
        }
        _currentModel = installed[0]; return installed[0];
    }

    public async Task UnloadModelAsync(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !_isModelLoaded) return;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var payload = new { model = modelName, prompt = "", keep_alive = 0 };
            var response = await _pingClient.PostAsJsonAsync($"{_ollamaUrl}/api/generate", payload, cts.Token);
            if (response.IsSuccessStatusCode) { Log.Information("[LocalAIService] Evicted '{Model}'", modelName); _isModelLoaded = false; }
        }
        catch (OperationCanceledException) { Log.Debug("[LocalAIService] Unload timed out; keep_alive will evict it.", modelName); }
        catch (Exception ex) { Log.Warning("[LocalAIService] Failed to unload {Model}: {Message}", modelName, ex.Message); }
    }

    public async Task DownloadModelAsync(string model, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var payload = new { name = model, stream = true };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_ollamaUrl}/api/pull") { Content = JsonContent.Create(payload) };
        using var response = await _genClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);
        while (!reader.EndOfStream && !ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("total", out var t) && doc.RootElement.TryGetProperty("completed", out var c) && t.GetInt64() > 0)
                progress?.Report((double)c.GetInt64() / t.GetInt64());
        }
        progress?.Report(1.0);
    }
}
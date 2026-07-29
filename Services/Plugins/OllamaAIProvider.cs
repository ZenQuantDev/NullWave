using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services.SmartSorting;
using Serilog;

namespace NullWave.Services.Plugins;

/// <summary>
/// Wraps LocalAIService as an optional AI provider. When disabled or Ollama is
/// unreachable, RankTracksAsync returns the original track list unranked so
/// MoodPlaylistService falls back to keyword-based sorting gracefully.
/// </summary>
public class OllamaAIProvider : IAIProvider
{
    private readonly LocalAIService _inner;
    private readonly PreferencesService _prefs;

    public string Name => "Ollama Local AI";
    public string Description => "On-device AI for smart shuffle and mood playlist ranking";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public LocalAIService Inner => _inner;

    public OllamaAIProvider(LocalAIService inner, PreferencesService prefs)
    {
        _inner = inner;
        _prefs = prefs;
        IsEnabled = prefs.Current.EnableOllama;

        _inner.FallbackNotice += msg => FallbackNotice?.Invoke(msg);
    }

    public event Action<string>? FallbackNotice;

    public async Task<bool> InitializeAsync(CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            Log.Information("[{Name}] Disabled by user preference", Name);
            return false;
        }

        try
        {
            bool running = await _inner.PingAsync();
            if (running)
            {
                State = PluginState.Available;
                Log.Information("[{Name}] Ollama is running and ready", Name);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[{Name}] Failed to ping Ollama", Name);
        }

        State = PluginState.Unavailable;
        Log.Warning("[{Name}] Ollama not detected at localhost:11434 — AI features disabled", Name);
        return false;
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        State = PluginState.Unavailable;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Track>> RankTracksAsync(IEnumerable<Track> tracks, string context, CancellationToken ct = default)
    {
        if (!IsEnabled || State != PluginState.Available)
            return Task.FromResult(tracks);

        return Task.FromResult(tracks);
    }
}
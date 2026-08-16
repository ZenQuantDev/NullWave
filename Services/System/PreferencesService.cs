using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class PreferencesService : IDisposable
{
    private readonly string _prefsPath;
    private Preferences _prefs;
    
    // FIX: Replaced Timer with CancellationTokenSource for robust thread-safe shutdown
    private CancellationTokenSource? _debounceCts;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(2);
    private readonly object _saveLock = new object();
    private bool _disposed;

    public Preferences Current => _prefs;

    public PreferencesService()
    {
        _prefsPath = Path.Combine(NullWavePaths.DataDir, "prefs.json");
        _prefs = Load();
    }

    private Preferences Load()
    {
        try
        {
            if (!File.Exists(_prefsPath))
                return new Preferences { DownloadDirectory = NullWavePaths.DownloadsDir };
                
            var json = File.ReadAllText(_prefsPath);
            var prefs = JsonSerializer.Deserialize<Preferences>(json) ?? new Preferences();
            
            if (string.IsNullOrEmpty(prefs.DownloadDirectory))
                prefs.DownloadDirectory = NullWavePaths.DownloadsDir;
                
            Log.Debug("[PreferencesService] Loaded preferences from {Path}", _prefsPath);
            return prefs;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load preferences");
            return new Preferences { DownloadDirectory = NullWavePaths.DownloadsDir };
        }
    }

    public void Save()
    {
        lock (_saveLock)
        {
            if (_disposed) return;
            try
            {
                var json = JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_prefsPath, json);
                Log.Debug("[PreferencesService] Saved preferences to {Path}", _prefsPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save preferences");
            }
        }
    }

    public void Update(Action<Preferences> updater)
    {
        lock (_saveLock)
        {
            if (_disposed) return;
            
            // 1. Update the object in memory immediately
            updater(_prefs);

            // 2. Cancel any existing pending save
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            
            // 3. Start a new debounce task
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceInterval, token);
                    if (!token.IsCancellationRequested)
                    {
                        Save();
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected when a new update comes in or service is disposed
                }
            }, token);
        }
    }

    public void Dispose()
    {
        lock (_saveLock)
        {
            if (_disposed) return;
            _disposed = true;
            
            // Cancel any pending debounce task immediately
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            
            // Force a final synchronous save on shutdown
            Save();
        }
    }
}
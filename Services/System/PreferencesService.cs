using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class PreferencesService : IDisposable
{
    private readonly string _prefsPath;
    private Preferences _prefs;
    
    // Debounce fields
    private Timer? _debounceTimer;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(2);
    private readonly object _saveLock = new object();

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
        // Ensure thread safety since the timer runs on a background thread
        lock (_saveLock)
        {
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
        // 1. Update the object in memory immediately
        updater(_prefs);

        // 2. Reset or start the debounce timer
        if (_debounceTimer == null)
        {
            // Timeout.InfiniteTimeSpan prevents the timer from firing more than once per trigger
            _debounceTimer = new Timer(_ => Save(), null, _debounceInterval, Timeout.InfiniteTimeSpan);
        }
        else
        {
            _debounceTimer.Change(_debounceInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        
        // Force a final save on shutdown if there are pending changes
        Save();
    }
}
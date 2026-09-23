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
        catch (JsonException ex)
        {
            Log.Error(ex, "prefs.json is corrupt; keeping a copy and using defaults");
            try { File.Move(_prefsPath, _prefsPath + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); }
            catch (Exception moveEx) { Log.Warning(moveEx, "Could not move the corrupt prefs.json aside"); }
            return new Preferences { DownloadDirectory = NullWavePaths.DownloadsDir };
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
            SaveCore();
        }
    }

    private void SaveCore()
    {
        try
        {
            var json = JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_prefsPath)!);
            var tmp = _prefsPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _prefsPath, overwrite: true);
            Log.Debug("[PreferencesService] Saved preferences to {Path}", _prefsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save preferences");
        }
    }

    public void Update(Action<Preferences> updater)
    {
        lock (_saveLock)
        {
            if (_disposed) return;
            
            updater(_prefs);
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_debounceInterval, token);
                    if (!token.IsCancellationRequested) Save();
                }
                catch (TaskCanceledException) { }
            }, token);
        }
    }

    public void Dispose()
    {
        lock (_saveLock)
        {
            if (_disposed) return;
            
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            
            SaveCore();          // final synchronous save; runs before the disposed flag is set
            _disposed = true;
        }
    }
}
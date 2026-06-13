using System;
using System.IO;
using System.Text.Json;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class PreferencesService
{
    private readonly string _prefsPath;
    private Preferences _prefs;

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

    public void Update(Action<Preferences> updater)
    {
        updater(_prefs);
        Save();
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections;
using System.Reflection;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public class PreferencesService : IDisposable
{
    private static readonly Lazy<PreferencesService> _shared = new(() => new PreferencesService());
    public static PreferencesService Shared => _shared.Value;

    private readonly string _prefsPath;
    private Preferences _prefs;

    private CancellationTokenSource? _debounceCts;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromSeconds(2);
    private readonly object _saveLock = new object();
    private bool _disposed;

    public event Action<HashSet<string>>? Changed;
    private static readonly PropertyInfo[] _prefProps = typeof(Preferences).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    public Preferences Current => _prefs;

    public PreferencesService()
    {
        _prefsPath = NullWavePaths.PrefsPath;
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

            // 3.3 FIX: Preferences versioning
            if (prefs.Version < 2)
            {
                Log.Information("[PreferencesService] Migrating preferences from v{Old} to v2", prefs.Version);
                try { File.Copy(_prefsPath, _prefsPath + $".v{prefs.Version}-backup", overwrite: true); } catch { }
                prefs.Version = 2;
            }

            Log.Debug("[PreferencesService] Loaded preferences from {Path}", _prefsPath);
            return prefs;
        }
        catch (JsonException ex)
        {
            Log.Error(ex, "prefs.json is corrupt; keeping a copy and using defaults");
            try { File.Move(_prefsPath, _prefsPath + ".bad-" + UtcTime.FileStamp()); }
            catch (Exception moveEx) { Log.Warning(moveEx, "Could not move the corrupt prefs.json aside"); }
            return new Preferences { DownloadDirectory = NullWavePaths.DownloadsDir };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load preferences");
            return new Preferences { DownloadDirectory = NullWavePaths.DownloadsDir };
        }
    }

    public bool Save()
    {
        lock (_saveLock)
        {
            if (_disposed) return false;
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
            return SaveCore();
        }
    }

    private bool SaveCore()
    {
        try
        {
            var json = JsonSerializer.Serialize(_prefs, new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(_prefsPath)!);
            var tmp = _prefsPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _prefsPath, overwrite: true);
            Log.Debug("[PreferencesService] Saved preferences to {Path}", _prefsPath);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save preferences");
            return false;
        }
    }

    public void Update(Action<Preferences> updater)
    {
        HashSet<string> changedNames;

        lock (_saveLock)
        {
            if (_disposed) return;

            // 1. Snapshot BEFORE state (serialize collections to detect in-place edits)
            var beforeSnapshot = new Dictionary<string, string>();
            foreach (var prop in _prefProps)
            {
                var val = prop.GetValue(_prefs);
                bool isCollection = val is IEnumerable && val is not string;

                beforeSnapshot[prop.Name] = isCollection
                    ? JsonSerializer.Serialize(val)
                    : val?.ToString() ?? "";
            }

            // 2. Apply changes
            updater(_prefs);

            // 3. Compare AFTER state
            changedNames = new HashSet<string>();
            foreach (var prop in _prefProps)
            {
                var afterVal = prop.GetValue(_prefs);
                bool isCollection = afterVal is IEnumerable && afterVal is not string;

                string afterStr = isCollection
                    ? JsonSerializer.Serialize(afterVal)
                    : afterVal?.ToString() ?? "";

                if (beforeSnapshot[prop.Name] != afterStr)
                    changedNames.Add(prop.Name);
            }

            if (changedNames.Count > 0)
            {
                // Schedule debounced save INSIDE lock to safely cancel previous pending saves
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
        } // End Lock

        // 4. Raise event OUTSIDE lock to prevent deadlocks if subscribers call back into Update/Save
        if (changedNames.Count > 0)
        {
            Changed?.Invoke(changedNames);
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
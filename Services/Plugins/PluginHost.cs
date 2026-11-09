using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Plugins.Contracts;
using NullWave.Services.Security;

namespace NullWave.Services.Plugins;

public class PluginHost : IPluginHost
{
    private readonly PreferencesService _prefs;
    private readonly KeyStoreService _keyStore;
    private readonly PluginManifest _manifest;

    public string HostVersion => "0.7.0";
    public PluginManifest Manifest => _manifest;

    public PluginHost(PreferencesService prefs, KeyStoreService keyStore, PluginManifest manifest)
    {
        _prefs = prefs;
        _keyStore = keyStore;
        _manifest = manifest;
    }

    public T? GetSetting<T>(string key)
    {
        return default;
    }

    public void SetSetting<T>(string key, T value)
    {
    }

    public string? GetApiKey(string keyName)
    {
        if (!_manifest.Permissions.AccessApiKeys)
        {
            Serilog.Log.Warning("[PluginHost] Plugin {PluginId} tried to access API key {Key} without permission",
                _manifest.Id, keyName);
            return null;
        }

        var key = _keyStore.GetKey(keyName);
        Serilog.Log.Information("[PluginHost] Plugin {PluginId} accessed API key {Key}", _manifest.Id, keyName);
        return key;
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        var prefix = $"[{_manifest.Id}]";
        switch (level)
        {
            case LogLevel.Debug:
                Serilog.Log.Debug(exception, "{Prefix} {Message}", prefix, message);
                break;
            case LogLevel.Info:
                Serilog.Log.Information(exception, "{Prefix} {Message}", prefix, message);
                break;
            case LogLevel.Warning:
                Serilog.Log.Warning(exception, "{Prefix} {Message}", prefix, message);
                break;
            case LogLevel.Error:
                Serilog.Log.Error(exception, "{Prefix} {Message}", prefix, message);
                break;
        }
    }

    public void ShowToast(string message, string type = "Info", int durationMs = 3000)
    {
        var toastType = type.ToLowerInvariant() switch
        {
            "success" => Models.ToastType.Success,
            "warning" => Models.ToastType.Warning,
            "error" => Models.ToastType.Error,
            _ => Models.ToastType.Info
        };

        ToastService.Instance.Show(message, toastType, durationMs, scope: _manifest.Id);
    }

    public Task<bool> ConfirmAsync(string title, string message)
    {
        return Task.FromResult(true);
    }

    public IReadOnlyList<TrackSnapshot> GetLibraryTracks()
    {
        return Array.Empty<TrackSnapshot>();
    }

    public Task<string> DownloadToTempAsync(string url, CancellationToken ct = default)
    {
        throw new NotImplementedException("DownloadToTempAsync not yet implemented");
    }

    public Task<ProcessResult> RunProcessAsync(string executable, string[] args, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!_manifest.Permissions.ExecuteProcesses)
        {
            Serilog.Log.Warning("[PluginHost] Plugin {PluginId} tried to execute process without permission", _manifest.Id);
            return Task.FromResult(new ProcessResult(-1, "", "Permission denied", false, false));
        }

        throw new NotImplementedException("RunProcessAsync not yet implemented");
    }
}
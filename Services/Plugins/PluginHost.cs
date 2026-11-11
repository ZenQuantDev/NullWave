using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Plugins.Contracts;
using NullWave.Services.Security;

namespace NullWave.Services.Plugins;

public class PluginHost : IPluginHost
{
    private readonly PreferencesService _prefs;
    private readonly KeyStoreService _keyStore;
    private readonly PluginManifest _manifest;

    public string HostVersion => AppVersionInfo.Current;
    public PluginManifest Manifest => _manifest;

    public PluginHost(PreferencesService prefs, KeyStoreService keyStore, PluginManifest manifest)
    {
        _prefs = prefs;
        _keyStore = keyStore;
        _manifest = manifest;
    }

    public T? GetSetting<T>(string key)
    {
        throw new NotImplementedException(
            "Plugin settings storage is not yet implemented. " +
            "This will be added when plugin settings UI is built.");
    }

    public void SetSetting<T>(string key, T value)
    {
        throw new NotImplementedException(
            "Plugin settings storage is not yet implemented. " +
            "This will be added when plugin settings UI is built.");
    }

    public string? GetApiKey(string keyName)
    {
        if (!_manifest.Permissions.AccessApiKeys)
        {
            Serilog.Log.Warning("[PluginHost] Plugin {PluginId} tried to access API key {Key} without permission",
                _manifest.Id, keyName);
            return null;
        }

        // TODO: Add scoped key access - only allow keys declared in manifest
        // For now, any key is accessible if AccessApiKeys permission is granted
        
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
        // Return false by default - plugins should not auto-approve destructive actions
        // TODO: Implement proper confirmation dialog
        Serilog.Log.Warning("[PluginHost] Plugin {PluginId} requested confirmation but no dialog is implemented yet",
            _manifest.Id);
        return Task.FromResult(false);
    }

    public IReadOnlyList<TrackSnapshot> GetLibraryTracks()
    {
        throw new NotImplementedException(
            "Library access is not yet implemented for plugins. " +
            "This will require permission checks and a read-only snapshot API.");
    }

    public Task<string> DownloadToTempAsync(string url, CancellationToken ct = default)
    {
        throw new NotImplementedException(
            "DownloadToTempAsync is not yet implemented. " +
            "This will require domain whitelist validation and secure temp file handling.");
    }

    public Task<NullWave.Plugins.Contracts.ProcessResult> RunProcessAsync(
        string executable, string[] args, TimeSpan timeout, CancellationToken ct = default)
    {
        if (!_manifest.Permissions.ExecuteProcesses)
        {
            Serilog.Log.Warning("[PluginHost] Plugin {PluginId} tried to execute process without permission", _manifest.Id);
            return Task.FromResult(new NullWave.Plugins.Contracts.ProcessResult(-1, "", "Permission denied", false, false));
        }

        throw new NotImplementedException(
            "RunProcessAsync is not yet implemented. " +
            "This will require sandboxing and resource limits.");
    }
}
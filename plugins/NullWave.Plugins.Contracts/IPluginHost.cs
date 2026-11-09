using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NullWave.Plugins.Contracts;

/// <summary>
/// The host interface provided to plugins at runtime.
/// Plugins use this to interact with NullWave's services and data.
/// </summary>
public interface IPluginHost
{
    /// <summary>NullWave version string</summary>
    string HostVersion { get; }

    /// <summary>Plugin's own manifest</summary>
    PluginManifest Manifest { get; }

    /// <summary>Read a plugin-specific setting value</summary>
    T? GetSetting<T>(string key);

    /// <summary>Write a plugin-specific setting value</summary>
    void SetSetting<T>(string key, T value);

    /// <summary>Read an API key from the keystore (requires AccessApiKeys permission)</summary>
    string? GetApiKey(string keyName);

    /// <summary>Log a message through NullWave's logging system</summary>
    void Log(LogLevel level, string message, Exception? exception = null);

    /// <summary>Show a toast notification to the user</summary>
    void ShowToast(string message, string type = "Info", int durationMs = 3000);

    /// <summary>Request user confirmation for a destructive or sensitive action</summary>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>Get the current user's library tracks (read-only snapshot)</summary>
    IReadOnlyList<TrackSnapshot> GetLibraryTracks();

    /// <summary>Download a file from a URL to a temporary location</summary>
    Task<string> DownloadToTempAsync(string url, CancellationToken ct = default);

    /// <summary>Execute an external process (requires ExecuteProcesses permission)</summary>
    Task<ProcessResult> RunProcessAsync(string executable, string[] args, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>Lightweight snapshot of a track for plugin consumption</summary>
public sealed record TrackSnapshot(
    Guid Id,
    string Title,
    string Artist,
    string? Album,
    TimeSpan? Duration,
    string? FilePath,
    string? Url,
    IReadOnlyList<string> Tags
);

/// <summary>Result of an external process execution</summary>
public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Canceled
);
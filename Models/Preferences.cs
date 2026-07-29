using System;
using System.Collections.Generic;

namespace NullWave.Models;

public class Preferences
{
    public int Version { get; set; } = 1;
    public string AudioQuality { get; set; } = "best";
    public string AudioFormat { get; set; } = "mp3";
    public string DownloadDirectory { get; set; } = string.Empty;
    public string YtDlpProxy { get; set; } = string.Empty;
    public string YtDlpGeoProxy { get; set; } = string.Empty;
    public string YtDlpBrowserCookies { get; set; } = string.Empty;
    public bool AutoFetchMetadata { get; set; } = true;
    public bool AutoPlayNext { get; set; } = true;
    public bool DownloadOnAdd { get; set; } = true;
    public bool ScrobbleToLastFm { get; set; } = true;
    public bool AutoCleanMetadata { get; set; } = true;
    public bool PreventDuplicateDownloads { get; set; } = true;
    public string AccentColor    { get; set; } = "Purple";
    public string TrackRowStyle  { get; set; } = "Comfortable";
    public string FontScale      { get; set; } = "Medium";
    public bool   CompactMode    { get; set; } = false;
    public string SidebarWidth   { get; set; } = "Normal";
    public string SelectedAIModel { get; set; } = "qwen2.5:7b";
    public bool   UseLocalAI      { get; set; } = true;
    public double Latitude        { get; set; } = 0.0;
    public double Longitude       { get; set; } = 0.0;
    public bool   AutoGenerateMoodPlaylist { get; set; } = false;
    public string MoodRefreshInterval      { get; set; } = "Never";
    public string AIConfidenceThreshold    { get; set; } = "70%";
    public string ExternalAIExportFormat { get; set; } = "txt";
    public float ScrobbleThreshold { get; set; } = 0.50f;
    public int SkipPenaltyWindowSeconds { get; set; } = 15;
    public int SkipPenaltyCap { get; set; } = 3;
    public int MaxConcurrentDownloads { get; set; } = 2;
    public string BatteryModel { get; set; } = "qwen2.5:3b";
    public string PerformanceModel { get; set; } = "qwen2.5:7b";
    public bool AutoPowerModelSwitch { get; set; } = false;
    public bool AIFeaturesEnabled { get; set; } = true;
    public bool FadeOnPauseEnabled { get; set; } = true;
    public int FadeOnPauseDurationMs { get; set; } = 300;
    public bool CrossfadeEnabled { get; set; } = true;
    public int CrossfadeDurationSeconds { get; set; } = 5;

    /// <summary>
    /// When true and aria2c is available on PATH, yt-dlp delegates downloading to it
    /// for multi-connection transfers. Opt-in because it's an external dependency —
    /// DownloadService checks availability at runtime and falls back silently if missing.
    /// </summary>
    public bool UseAria2c { get; set; } = false;

    /// <summary>
    /// When enabled, sets system log levels to Verbose/Debug. When disabled, filters down to Information.
    /// </summary>
    public bool VerboseLogging { get; set; } = false;

    // ------------------------------------------------------------------------
    // Phase 13 — Plugin Architecture toggles
    // ------------------------------------------------------------------------

    /// <summary>
    /// When false, all download features are hidden and NullWave operates
    /// strictly as a local file manager.
    /// </summary>
    public bool EnableYtDlp { get; set; } = true;

    /// <summary>
    /// When false, local AI features (Smart Shuffle, Mood Playlists) are disabled.
    /// </summary>
    public bool EnableOllama { get; set; } = true;

    /// <summary>
    /// When false, weather-based mood playlists fall back to generic defaults.
    /// </summary>
    public bool EnableOpenWeather { get; set; } = true;

    /// <summary>
    /// When false, Last.fm scrobbling and metadata enrichment are disabled.
    /// </summary>
    public bool EnableLastFm { get; set; } = true;

    /// <summary>
    /// When false, SoundCloud metadata fetching and playlist import are disabled.
    /// </summary>
    public bool EnableSoundCloud { get; set; } = true;

    /// <summary>
    /// Per-plugin advanced configuration (endpoint URLs, model names, etc.).
    /// </summary>
    public List<PluginConfig> PluginConfigs { get; set; } = new();
}
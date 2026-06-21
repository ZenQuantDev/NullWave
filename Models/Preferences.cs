using System;
using System.Collections.Generic;

namespace NullWave.Models;

public class Preferences
{
    // Audio
    public string AudioQuality { get; set; } = "best";
    public string AudioFormat { get; set; } = "mp3";
    public string DownloadDirectory { get; set; } = string.Empty;

    // Behavior
    public bool AutoFetchMetadata { get; set; } = true;
    public bool AutoPlayNext { get; set; } = true;
    public bool DownloadOnAdd { get; set; } = true;
    public bool ScrobbleToLastFm { get; set; } = true;

    // Appearance
    public string AccentColor    { get; set; } = "Purple";
    public string TrackRowStyle  { get; set; } = "Comfortable";
    public string FontScale      { get; set; } = "Medium";
    public bool   CompactMode    { get; set; } = false;
    public string SidebarWidth   { get; set; } = "Normal";

    // ── Smart Sorting ──────────────────────────────────────────────────────
    public string SelectedAIModel { get; set; } = "qwen2.5:7b";
    public bool   UseLocalAI      { get; set; } = true;
    public double Latitude        { get; set; } = 0.0;   // 0 = not set yet
    public double Longitude       { get; set; } = 0.0;
}
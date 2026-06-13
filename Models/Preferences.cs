namespace NullWave.Models;

public class Preferences
{
    public string AudioQuality { get; set; } = "best";
    public string AudioFormat { get; set; } = "mp3";
    public string DownloadDirectory { get; set; } = string.Empty;
    public bool AutoFetchMetadata { get; set; } = true;
    public bool AutoPlayNext { get; set; } = true;
    public bool DownloadOnAdd { get; set; } = true;
    public bool ScrobbleToLastFm { get; set; } = true;
    public string AccentColor    { get; set; } = "Purple";
    public string TrackRowStyle  { get; set; } = "Comfortable";
    public string FontScale      { get; set; } = "Medium";
    public bool   CompactMode    { get; set; } = false;
    public string SidebarWidth   { get; set; } = "Normal";
}
using System.Text;
using NullWave.ViewModels.Settings;

namespace NullWave.ViewModels;

public partial class SettingsViewModel
{
    /// <summary>Backs the in-app log viewer in the Help tab.</summary>
    public LogViewerViewModel LogViewer { get; private set; } = null!;

    // Settings snapshot for bug reports: deliberately excludes API keys and raw paths.
    private string BuildSettingsSummary()
    {
        var p = _prefsService.Current;
        var sb = new StringBuilder();
        sb.AppendLine($"App: {VersionLabel}");
        sb.AppendLine($"OS: {OsLabel} ({System.Runtime.InteropServices.RuntimeInformation.OSDescription})");
        sb.AppendLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"Theme: {p.ThemeMode} / {p.AccentColor}; Language: {p.Language}");
        sb.AppendLine($"VerboseLogging: {p.VerboseLogging}");
        sb.AppendLine($"Downloads: max={p.MaxConcurrentDownloads}, aria2c={p.UseAria2c}");
        sb.AppendLine($"Audio: {p.AudioFormat}/{p.AudioQuality}, crossfade={p.CrossfadeEnabled} ({p.CrossfadeDurationSeconds}s)");
        sb.AppendLine($"Plugins: yt-dlp={p.EnableYtDlp}, LastFm={p.EnableLastFm}, OpenWeather={p.EnableOpenWeather}, Ollama={p.EnableOllama}");
        return sb.ToString();
    }
}
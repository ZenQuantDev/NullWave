using System;
using Serilog;

namespace NullWave.Helpers.Logging;

/// <summary>
/// Structured user-action logger. Every call produces a single, consistently
/// formatted entry in UserActions.log and the main log.
/// </summary>
public static class NullActionLogger
{
    public static void User(string actionTemplate, string target, string source, params object[] args)
    => Log.ForContext("Channel", "UserAction")
          .ForContext("ActionSource", source)
          .Information($"[ACTION] {actionTemplate} | Target: {{Target}} | Source: {{ActionSource}}",
              [.. args, target ?? "None", source]);

    public static void TrackPlayed(string trackId, string title, string artist, string source)
    => User("TrackPlayed title=\"{Title}\" artist=\"{Artist}\"", trackId, source, title, artist);

    public static void TrackPaused(string trackId, string positionDisplay, string source)
    => User("TrackPaused position={Position}", trackId, source, positionDisplay);

    public static void TrackStopped(string trackId, string source)
    => User("TrackStopped", trackId, source);

    public static void TrackAdded(string trackId, string importSource, string callerSource)
    => User("TrackAdded importSource={ImportSource}", trackId, callerSource, importSource);

    public static void TrackRemoved(string trackId, string source)
    => User("TrackRemoved", trackId, source);

    public static void TrackEdited(string trackId, string changedFields, string source)
    => User("TrackEdited fields=[{ChangedFields}]", trackId, source, changedFields);

    public static void FavoriteToggled(string trackId, bool newValue, string source)
    => User("FavoriteToggled newValue={NewValue}", trackId, source, newValue);

    public static void ImportStarted(string url, string source)
    => User("ImportStarted", url, source);

    public static void ImportCompleted(string url, string trackId, long durationMs, string source)
    => User("ImportCompleted durationMs={DurationMs}", $"{url} -> {trackId}", source, durationMs);

    public static void ImportFailed(string url, string error, string source)
    => User("ImportFailed error=\"{Error}\"", url, source, error);

    public static void PlaylistCreated(string playlistId, string name, string source)
    => User("PlaylistCreated name=\"{Name}\"", playlistId, source, name);

    public static void PlaylistDeleted(string playlistId, string source)
    => User("PlaylistDeleted", playlistId, source);

    public static void PlaylistTrackAdded(string playlistId, string trackId, string source)
    => User("PlaylistTrackAdded", $"playlist={playlistId} track={trackId}", source);

    // Graceful fallback value for clean parsing instead of "(no value logged)"
    public static void SettingChanged(string key, string source)
    => User("SettingChanged key={Key}", "None", source, key);

    public static void SearchPerformed(string query, int resultCount, string source)
    => User("SearchPerformed results={ResultCount}", $"query=\"{query}\"", source, resultCount);

    public static void Error(string callerSource, string message, string? context = null)
    => Log.ForContext("Channel", "Error")
          .ForContext("ErrorSource", callerSource)
          .Error("[{ErrorSource}] {Message}{Context}",
              callerSource, message,
              context is null ? string.Empty : $" | {context}");

    public static void Error(string callerSource, Exception ex, string? context = null)
    => Log.ForContext("Channel", "Error")
          .ForContext("ErrorSource", callerSource)
          .Error(ex, "[{ErrorSource}] {Message}{Context}",
              callerSource, ex.Message,
              context is null ? string.Empty : $" | {context}");

    public static void StartupLine(string message)
    => Log.ForContext("Channel", "Startup")
          .Information("[STARTUP] {Message}", message);
}
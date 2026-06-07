using Serilog;
using Serilog.Events;

namespace NullWave.Helpers.Logging;

/// <summary>
/// Structured user-action logger. Every call produces a single, consistently
/// formatted entry in UserActions.log and the main log.
///
/// Format:
///   [ACTION] {Timestamp} | {Action} | {Target} | {Source}
///
/// Usage:
///   NullActionLogger.User("TrackPlayed", track.Id.ToString(), nameof(PlayerViewModel));
///   NullActionLogger.User("ImportStarted", url, nameof(ImportViewModel));
/// </summary>
public static class NullActionLogger
{
    // ─── User-action channel ──────────────────────────────────────────────────

    public static void User(string action, string target, string source)
        => Log.ForContext("Channel", "UserAction")
              .ForContext("ActionSource", source)
              .Information("[ACTION] {Action} | Target: {Target} | Source: {ActionSource}",
                  action, target, source);

    // ─── Convenience overloads ────────────────────────────────────────────────

    public static void TrackPlayed(string trackId, string title, string artist, string source)
        => User($"TrackPlayed title=\"{title}\" artist=\"{artist}\"", trackId, source);

    public static void TrackPaused(string trackId, string positionDisplay, string source)
        => User($"TrackPaused position={positionDisplay}", trackId, source);

    public static void TrackStopped(string trackId, string source)
        => User("TrackStopped", trackId, source);

    public static void TrackAdded(string trackId, string importSource, string callerSource)
        => User($"TrackAdded importSource={importSource}", trackId, callerSource);

    public static void TrackRemoved(string trackId, string source)
        => User("TrackRemoved", trackId, source);

    public static void TrackEdited(string trackId, string changedFields, string source)
        => User($"TrackEdited fields=[{changedFields}]", trackId, source);

    public static void FavoriteToggled(string trackId, bool newValue, string source)
        => User($"FavoriteToggled newValue={newValue}", trackId, source);

    public static void ImportStarted(string url, string source)
        => User("ImportStarted", url, source);

    public static void ImportCompleted(string url, string trackId, long durationMs, string source)
        => User($"ImportCompleted durationMs={durationMs}", $"{url} → {trackId}", source);

    public static void ImportFailed(string url, string error, string source)
        => User($"ImportFailed error=\"{error}\"", url, source);

    public static void PlaylistCreated(string playlistId, string name, string source)
        => User($"PlaylistCreated name=\"{name}\"", playlistId, source);

    public static void PlaylistDeleted(string playlistId, string source)
        => User("PlaylistDeleted", playlistId, source);

    public static void PlaylistTrackAdded(string playlistId, string trackId, string source)
        => User("PlaylistTrackAdded", $"playlist={playlistId} track={trackId}", source);

    public static void SettingChanged(string key, string source)
        => User($"SettingChanged key={key}", "(no value logged)", source);

    public static void SearchPerformed(string query, int resultCount, string source)
        => User($"SearchPerformed results={resultCount}", $"query=\"{query}\"", source);

    // ─── System / attributed errors ───────────────────────────────────────────

    /// <summary>
    /// Logs an error attributed to a specific ViewModel or Service.
    /// Produces: [ERROR] [Source] Message | context
    /// </summary>
    public static void Error(string callerSource, string message, string? context = null)
        => Log.ForContext("Channel", "Error")
              .ForContext("ErrorSource", callerSource)
              .Error("[{ErrorSource}] {Message}{Context}",
                  callerSource, message,
                  context != null ? $" | {context}" : string.Empty);

    public static void Error(string callerSource, Exception ex, string? context = null)
        => Log.ForContext("Channel", "Error")
              .ForContext("ErrorSource", callerSource)
              .Error(ex, "[{ErrorSource}] {Message}{Context}",
                  callerSource, ex.Message,
                  context != null ? $" | {context}" : string.Empty);

    // ─── Startup diagnostics ─────────────────────────────────────────────────

    public static void StartupLine(string message)
        => Log.ForContext("Channel", "Startup")
              .Information("[STARTUP] {Message}", message);
}
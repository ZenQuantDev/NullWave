namespace NullWave.Services.Plugins;

public enum PluginState
{
    /// <summary>
    /// Plugin binary/API is present and healthy.
    /// </summary>
    Available,

    /// <summary>
    /// Plugin binary or remote endpoint not found (e.g. yt-dlp not on PATH).
    /// </summary>
    Unavailable,

    /// <summary>
    /// User explicitly disabled this plugin.
    /// </summary>
    Disabled,

    /// <summary>
    /// Initialization failed or health check is failing.
    /// </summary>
    Error,

    /// <summary>
    /// Plugin is currently starting up.
    /// </summary>
    Loading
}
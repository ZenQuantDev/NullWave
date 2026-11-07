namespace NullWave.Services.Plugins;

/// <summary>Categories of provider. Each kind has its own activation rules.</summary>
public enum ProviderKind
{
    /// <summary>Provides audio files from URLs. Single-active.</summary>
    Download,
    /// <summary>Provides track/artist/album metadata. Single-active.</summary>
    Metadata,
    /// <summary>Searches for tracks by query. Multi-active (fan-out).</summary>
    Search,
    /// <summary>Imports playlists from files/URLs. Multi-active.</summary>
    PlaylistImport,
    /// <summary>Provides radio stations/streams. Multi-active.</summary>
    Radio,
    /// <summary>Ranks/transforms tracks (AI). Single-active.</summary>
    Mood,
    /// <summary>Provides weather context for mood playlists. Single-active.</summary>
    Weather,
}

public static class ProviderKindExtensions
{
    /// <summary>True if only one provider of this kind can be active at a time.</summary>
    public static bool IsSingleActive(this ProviderKind kind) => kind switch
    {
        ProviderKind.Download => true,
        ProviderKind.Metadata => true,
        ProviderKind.Mood => true,
        ProviderKind.Weather => true,
        _ => false,
    };
}
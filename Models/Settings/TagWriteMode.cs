namespace NullWave.Models;

/// <summary>
/// Controls when NullWave writes ID3/metadata tags to audio files.
/// </summary>
public enum TagWriteMode
{
    /// <summary>Never write tags to files. Metadata stays in the database only.</summary>
    Never = 0,
    
    /// <summary>Write tags only to files NullWave downloaded (default, safest).</summary>
    DownloadsOnly = 1,
    
    /// <summary>Write tags to all files, including user-imported music (requires explicit opt-in).</summary>
    Always = 2
}
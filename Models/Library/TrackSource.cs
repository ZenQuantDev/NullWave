namespace NullWave.Models;

public enum TrackSource 
{ 
    YouTube, 
    Spotify, 
    SoundCloud, 
    LastFm, 
    Local, 
    Unknown,
    // --- No-API-Key Sources (yt-dlp native) ---
    Bandcamp,
    Audiomack,
    ArchiveOrg,
    Mixcloud,
    // --- API-Backed Sources (future) ---
    // Jamendo,
    // FreeMusicArchive,
    // Deezer
}
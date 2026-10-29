using NullWave.Models;
using NullWave.ViewModels.Base;
namespace NullWave.ViewModels;

// Wraps a Track for UI-specific display logic
public class TrackViewModel : ViewModelBase
{
    public Track Track { get; }

    public TrackViewModel(Track track)
    {
        Track = track;
    }

    public string Title => Track.Title;
    public string Artist => Track.Artist;
    public string Source => Track.Source.ToString();
    public string DateAdded => Track.DateAdded.ToString("dd MMM yyyy");
    public string? Url => Track.Url;

    public string DisplayLabel => string.IsNullOrWhiteSpace(Track.Artist)
        ? Track.Title
        : $"{Track.Artist} - {Track.Title}";

    public string SourceBadge => Track.Source switch
    {
        TrackSource.YouTube => "▶ YouTube",
        TrackSource.Spotify => "♪ Spotify",
        TrackSource.SoundCloud => "☁ SoundCloud",
        TrackSource.LastFm => "◎ Last.fm",
        TrackSource.Bandcamp => "♫ Bandcamp",
        TrackSource.Audiomack => "▶ Audiomack",
        TrackSource.ArchiveOrg => "⌂ Archive.org",
        TrackSource.Mixcloud => "☁ Mixcloud",
        TrackSource.Local => "💾 Local",
        _ => "? Unknown"
    };
}

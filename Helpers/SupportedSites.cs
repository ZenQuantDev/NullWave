using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using Material.Icons;

namespace NullWave.Helpers;

public sealed record SupportedSite(
    string Id, 
    string DisplayName, 
    TrackSource Source, 
    string[] Hosts, 
    MaterialIconKind IconKind,
    string IconColorHex,
    bool IsYtDlpNative = true,
    string? YtDlpExtractorName = null)
{
    public bool MatchesHost(string host) => Hosts.Any(h => host == h || host.EndsWith("." + h, System.StringComparison.Ordinal));
}

public static class SupportedSites
{
    public static readonly SupportedSite[] All =
    {
        new("youtube",    "YouTube",    TrackSource.YouTube,    new[]{"youtube.com", "youtu.be", "music.youtube.com"}, 
            MaterialIconKind.Youtube, "#FF0000", true, "youtube"),
        new("soundcloud", "SoundCloud", TrackSource.SoundCloud, new[]{"soundcloud.com", "snd.sc", "on.soundcloud.com"}, 
            MaterialIconKind.Soundcloud, "#FF5500", true, "soundcloud"),
        new("spotify",    "Spotify",    TrackSource.Spotify,    new[]{"spotify.com", "open.spotify.com"}, 
            MaterialIconKind.Spotify, "#1DB954", false, null),
        new("lastfm",     "Last.fm",    TrackSource.LastFm,     new[]{"last.fm"}, 
            MaterialIconKind.MusicNote, "#D51007", false, null),
        new("bandcamp",   "Bandcamp",   TrackSource.Bandcamp,   new[]{"bandcamp.com"}, 
            MaterialIconKind.Album, "#1DA0C3", true, "Bandcamp"),
        new("audiomack",  "Audiomack",  TrackSource.Audiomack,  new[]{"audiomack.com"}, 
            MaterialIconKind.Music, "#FFA200", true, "audiomack"),
        new("archive",    "Archive.org",TrackSource.ArchiveOrg, new[]{"archive.org"}, 
            MaterialIconKind.Archive, "#333333", true, "archive.org"),
        new("mixcloud",   "Mixcloud",   TrackSource.Mixcloud,   new[]{"mixcloud.com"}, 
            MaterialIconKind.MusicBox, "#5000FF", true, "mixcloud"),
        new("local",      "Local",      TrackSource.Local,      System.Array.Empty<string>(), 
            MaterialIconKind.Folder, "#34D399", false, null),
    };

    public static SupportedSite? FindBySource(TrackSource source) => All.FirstOrDefault(s => s.Source == source);
    public static SupportedSite? FindByHost(string host) => All.FirstOrDefault(s => s.MatchesHost(host));
}
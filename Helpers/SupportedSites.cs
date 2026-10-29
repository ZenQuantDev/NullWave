using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

namespace NullWave.Helpers;

public sealed record SupportedSite(string Id, string DisplayName, TrackSource Source, string[] Hosts, bool IsYtDlpNative = true)
{
    public bool MatchesHost(string host) => Hosts.Any(h => host == h || host.EndsWith("." + h, System.StringComparison.Ordinal));
}

public static class SupportedSites
{
    public static readonly SupportedSite[] All =
    {
        new("youtube",    "YouTube",    TrackSource.YouTube,    new[]{"youtube.com", "youtu.be", "music.youtube.com"}),
        new("soundcloud", "SoundCloud", TrackSource.SoundCloud, new[]{"soundcloud.com", "snd.sc", "on.soundcloud.com"}),
        new("spotify",    "Spotify",    TrackSource.Spotify,    new[]{"spotify.com", "open.spotify.com"}, IsYtDlpNative: false),
        new("lastfm",     "Last.fm",    TrackSource.LastFm,     new[]{"last.fm"}, IsYtDlpNative: false),
        new("bandcamp",   "Bandcamp",   TrackSource.Bandcamp,   new[]{"bandcamp.com"}),
        new("audiomack",  "Audiomack",  TrackSource.Audiomack,  new[]{"audiomack.com"}),
        new("archive",    "Archive.org",TrackSource.ArchiveOrg, new[]{"archive.org"}),
        new("mixcloud",   "Mixcloud",   TrackSource.Mixcloud,   new[]{"mixcloud.com"}),
        new("local",      "Local",      TrackSource.Local,      System.Array.Empty<string>(), IsYtDlpNative: false),
    };

    public static SupportedSite? FindBySource(TrackSource source) => All.FirstOrDefault(s => s.Source == source);
    public static SupportedSite? FindByHost(string host) => All.FirstOrDefault(s => s.MatchesHost(host));
}
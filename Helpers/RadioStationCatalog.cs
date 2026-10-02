using System;
using System.Collections.Generic;

namespace NullWave.Helpers;

public class RadioChannel
{
    public string Name { get; }
    public string? StreamUrl { get; set; }
    public RadioChannel(string name, string? streamUrl = null) { Name = name; StreamUrl = streamUrl; }
}

public record CuratedStation(string Name, string Genre, string StreamUrl, string? Description = null)
{
    // Expose a boolean for XAML binding to avoid converter namespace risks
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
}

public static class RadioStationCatalog
{
    // Curated list for the "Browse Radio" UI dialog
    private static readonly List<CuratedStation> _curated = new()
    {
        new("Lofi Girl", "Lofi / Chill", "https://play.streamafrica.net/lofiradio", "Hip hop beats to relax/study to."),
        new("SomaFM: Groove Salad", "Ambient / Downtempo", "https://ice.somafm.com/groovesalad", "A nicely chilled plate of ambient/downtempo beats and grooves."),
        new("SomaFM: Drone Zone", "Ambient", "https://ice.somafm.com/dronezone", "Served best chilled — atmospheric textures with minimal beats."),
        new("SomaFM: Space Station", "Ambient / Space", "https://ice.somafm.com/spacestation", "Tune in, turn on, space out. Spaced-out ambient and mid-tempo electronica."),
        new("SomaFM: Deep Space One", "Ambient / Experimental", "https://ice.somafm.com/deepspaceone", "Deep ambient electronic, experimental, and space music."),
        new("SomaFM: Indie Pop Rocks!", "Indie Pop", "https://ice.somafm.com/indiepop", "New and classic favorite indie pop tracks."),
        new("SomaFM: Lush", "Vocal / Electronic", "https://ice.somafm.com/lush", "Sensuous and mellow female vocals, many with an electronic influence."),
        new("SomaFM: Groove Salad Classic", "Ambient / Downtempo", "https://ice.somafm.com/gsclassic", "The classic early-2000s Groove Salad mix."),
        new("Jazz24", "Jazz", "https://live.wostreaming.net/direct/ppm-jazz24mp3-ibc1", "24/7 Jazz from KNKX Seattle."),
        new("Radio Paradise (Main)", "Eclectic / Rock", "https://stream.radioparadise.com/mp3-128", "DJ-mixed eclectic rock, electronic, and world music."),
        new("FIP Main", "Eclectic", "https://icecast.radiofrance.fr/fip-midfi.mp3", "French public radio, legendary eclectic curation."),
        new("KEXP 90.3", "Indie / Alternative", "https://kexp-mp3-128.streamguys1.com/kexp128.mp3", "Seattle's premier indie and alternative station."),
        new("NTS 1", "Underground / Eclectic", "https://stream-relay-ico.ntslive.net/stream", "London-based underground and alternative radio."),
        new("WFMU", "Freeform", "http://stream0.wfmu.org/hifi", "The longest-running freeform radio station in the US.")
    };

    public static IReadOnlyList<CuratedStation> GetCuratedStations() => _curated;

    private static readonly Dictionary<string, (string Station, IReadOnlyList<RadioChannel> Channels)> KnownSites =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["somafm.com"] = ("SomaFM", new[]
        {
            new RadioChannel("Groove Salad", "https://ice.somafm.com/groovesalad"),
            new RadioChannel("Drone Zone", "https://ice.somafm.com/dronezone"),
            new RadioChannel("Secret Agent", "https://ice.somafm.com/secretagent"),
            new RadioChannel("Indie Pop Rocks", "https://ice.somafm.com/indiepop"),
            new RadioChannel("Space Station Soma", "https://ice.somafm.com/spacestation"),
        }),
        ["radioparadise.com"] = ("Radio Paradise", new[]
        {
            new RadioChannel("Main Mix", "https://stream.radioparadise.com/mp3-128"),
            new RadioChannel("Mellow Mix", "https://stream.radioparadise.com/mellow-128"),
            new RadioChannel("Rock Mix", "https://stream.radioparadise.com/rock-128"),
            new RadioChannel("World/Etc Mix", "https://stream.radioparadise.com/world-128"),
        }),
        ["kexp.org"] = ("KEXP Seattle", new[]
        {
            new RadioChannel("KEXP 90.3 FM", "https://kexp-mp3-128.streamguys1.com/kexp128.mp3"),
        }),
        ["nts.live"] = ("NTS Radio", new[]
        {
            new RadioChannel("NTS 1", "https://stream-relay-ico.ntslive.net/stream"),
            new RadioChannel("NTS 2", "https://stream-relay-ico.ntslive.net/stream2"),
        }),
        ["radiofrance.fr"] = ("FIP Radio", new[]
        {
            new RadioChannel("FIP Main", "https://icecast.radiofrance.fr/fip-midfi.mp3"),
            new RadioChannel("FIP Jazz", "https://icecast.radiofrance.fr/fipjazz-midfi.mp3"),
            new RadioChannel("FIP Groove", "https://icecast.radiofrance.fr/fipgroove-midfi.mp3"),
            new RadioChannel("FIP Reggae", "https://icecast.radiofrance.fr/fipreggae-midfi.mp3"),
        }),
        ["jazz24.org"] = ("Jazz24", new[]
        {
            new RadioChannel("Jazz24 Live", "https://live.wostreaming.net/direct/ppm-jazz24mp3-ibc1"),
        }),
        ["wfmu.org"] = ("WFMU", new[]
        {
            new RadioChannel("WFMU Freeform", "http://stream0.wfmu.org/hifi"),
        })
    };

    public static bool TryGetChannels(string url, out string stationName, out IReadOnlyList<RadioChannel> channels)
    {
        stationName = string.Empty; channels = Array.Empty<RadioChannel>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.Replace("www.", "");
        foreach (var (key, value) in KnownSites)
        {
            if (host.EndsWith(key, StringComparison.OrdinalIgnoreCase))
            {
                stationName = value.Station;
                channels = value.Channels;
                return true;
            }
        }
        return false;
    }
}
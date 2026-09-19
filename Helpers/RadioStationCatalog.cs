using System;
using System.Collections.Generic;

namespace NullWave.Helpers;

public class RadioChannel
{
    public string Name { get; }
    public string? StreamUrl { get; set; }
    public RadioChannel(string name, string? streamUrl = null) { Name = name; StreamUrl = streamUrl; }
}

public static class RadioStationCatalog
{
    private static readonly Dictionary<string, (string Station, IReadOnlyList<RadioChannel> Channels)> KnownSites =
        new(StringComparer.OrdinalIgnoreCase)
    {
        ["somafm.com"] = ("SomaFM", new[]
        {
            new RadioChannel("Groove Salad", "https://ice1.somafm.com/groovesalad-128-mp3"),
            new RadioChannel("Drone Zone", "https://ice1.somafm.com/dronezone-128-mp3"),
            new RadioChannel("DEF CON Radio", "https://ice1.somafm.com/defcon-128-mp3"),
            new RadioChannel("Secret Agent", "https://ice1.somafm.com/secretagent-128-mp3"),
            new RadioChannel("Indie Pop Rocks", "https://ice1.somafm.com/indiepop-128-mp3"),
            new RadioChannel("Space Station Soma", "https://ice1.somafm.com/spacestation-128-mp3"),
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
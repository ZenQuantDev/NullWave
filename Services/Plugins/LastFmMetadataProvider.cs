using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Services;
using Serilog;
using NullWave.Plugins.Contracts;

namespace NullWave.Services.Plugins;

/// <summary>
/// Wraps LastFmService as an optional metadata provider. When disabled, enrichment
/// and scrobbling silently skip instead of throwing.
/// </summary>
public class LastFmMetadataProvider : IMetadataProvider
{
    private readonly LastFmService _inner;
    private readonly PreferencesService _prefs;

    public string Id => Descriptor.Id;
    public string Name => "Last.fm";
    public string Description => "Metadata enrichment, artist bios, and scrobbling";
    public PluginState State { get; set; } = PluginState.Unavailable;
    public bool IsEnabled { get; set; } = true;

    public ProviderDescriptor Descriptor { get; } = new(
        Id: "lastfm",
        Kind: ProviderKind.Metadata,
        DisplayName: "Last.fm"
    );

    public LastFmService Inner => _inner;

    public LastFmMetadataProvider(LastFmService inner, PreferencesService prefs)
    {
        _inner = inner;
        _prefs = prefs;
        IsEnabled = prefs.Current.EnableLastFm;
    }

    public Task<bool> InitializeAsync(IPluginHost host, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            State = PluginState.Disabled;
            return Task.FromResult(false);
        }

        if (!_inner.IsConfiguredForRead)
        {
            State = PluginState.Unavailable;
            Log.Information("[{Name}] Unavailable (no API key configured)", Name);
            return Task.FromResult(false);
        }

        State = PluginState.Available;
        Log.Information("[{Name}] Ready - read={Read}, scrobble={Scrobble}",
            Name, _inner.IsConfiguredForRead, _inner.IsConfiguredForScrobbling);
        return Task.FromResult(true);
    }

    public Task ShutdownAsync(CancellationToken ct = default)
    {
        State = PluginState.Unavailable;
        return Task.CompletedTask;
    }

    public async Task<TrackMetadata?> FetchMetadataAsync(string identifier, TrackSource source, CancellationToken ct = default)
    {
        if (!IsEnabled || State != PluginState.Available)
            return null;

        var parts = identifier.Split('|', 2);
        if (parts.Length != 2) return null;

        var info = await _inner.GetTrackInfoAsync(parts[0], parts[1]);
        if (info == null) return null;

        return new TrackMetadata
        {
            Title = info.Title,
            Artist = info.Artist,
            Tags = info.Tags,
            AlbumArtUrl = info.AlbumArtUrl
        };
    }

    public Task<LastFmArtistInfo?> GetArtistInfoAsync(string artist)
        => _inner.GetArtistInfoAsync(artist);

    public Task<bool> ScrobbleAsync(string title, string artist, DateTime playedAt)
        => _inner.ScrobbleAsync(title, artist, playedAt);

    public bool IsConfiguredForRead => _inner.IsConfiguredForRead;
    public bool IsConfiguredForScrobbling => _inner.IsConfiguredForScrobbling;
}
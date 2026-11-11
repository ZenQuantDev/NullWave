using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Models;
using NullWave.Plugins.Contracts;
using NullWave.Services;
using NullWave.Services.Plugins;
using Serilog;

namespace NullWave.Services.Radio;

public enum RadioHealStatus { Healthy, Moved, Dead, Skipped }

public sealed record RadioHealPreview(Track Track, string? NewUrl, RadioHealStatus Status, string? StationName);

/// <summary>
/// Repairs radio streams whose directory URL changed, using the radiobrowser:&lt;uuid&gt; tag.
/// Dead stations are tagged, never deleted. Repair lookups are click-free.
/// </summary>
public sealed class RadioStreamHealer
{
    public const string TagPrefix = "radiobrowser:";
    public const string DeadTag = "radio:dead";

    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);
    private readonly Dictionary<Guid, DateTime> _lastAttempt = new();

    private readonly LibraryService _library;
    private readonly ProviderRegistry _registry;
    private readonly PreferencesService _prefs;

    public RadioStreamHealer(LibraryService library, ProviderRegistry registry, PreferencesService prefs)
    {
        _library = library;
        _registry = registry;
        _prefs = prefs;
    }

    private IRadioProvider? Provider =>
        _registry.GetActive(ProviderKind.Radio) as IRadioProvider is { State: PluginState.Available } provider
            ? provider
            : null;

    public bool IsProviderAvailable => Provider != null;

    public static string? StationUuid(Track track)
    {
        var tag = track.Tags?.FirstOrDefault(value => value.StartsWith(TagPrefix, StringComparison.Ordinal));
        if (tag == null || tag.Length <= TagPrefix.Length) return null;
        var uuid = tag[TagPrefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(uuid) ? null : uuid;
    }

    /// <summary>Pure classification — unit-testable without a database or network.</summary>
    public static RadioHealPreview Classify(Track track, RadioStationInfo? station)
    {
        if (StationUuid(track) == null)
            return new RadioHealPreview(track, null, RadioHealStatus.Skipped, null);
        if (station == null)
            return new RadioHealPreview(track, null, RadioHealStatus.Dead, null);

        var newUrl = station.StreamUrl?.Trim();
        if (string.IsNullOrWhiteSpace(newUrl))
            return new RadioHealPreview(track, null, RadioHealStatus.Dead, station.Name);

        var same = string.Equals(newUrl, track.Url?.Trim(), StringComparison.OrdinalIgnoreCase);
        return new RadioHealPreview(track, same ? null : newUrl,
            same ? RadioHealStatus.Healthy : RadioHealStatus.Moved, station.Name);
    }

    /// <summary>Dry run: classify every tagged radio track. Returns empty if no provider is available.</summary>
    public async Task<IReadOnlyList<RadioHealPreview>> PreviewAsync(CancellationToken ct = default)
    {
        var provider = Provider;
        if (provider == null) return Array.Empty<RadioHealPreview>();

        var results = new List<RadioHealPreview>();
        foreach (var track in _library.GetAll().Where(track =>
                     track.MediaType == MediaType.Radio && StationUuid(track) != null))
        {
            ct.ThrowIfCancellationRequested();
            var station = await provider.GetStationAsync(StationUuid(track)!, ct);
            results.Add(Classify(track, station));
        }
        return results;
    }

    /// <summary>Apply a confirmed preview. Returns number of tracks changed.</summary>
    public Task<int> ApplyAsync(IReadOnlyList<RadioHealPreview> preview, CancellationToken ct = default)
    {
        var changed = 0;
        foreach (var item in preview)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Status == RadioHealStatus.Moved && item.NewUrl != null)
            {
                item.Track.Url = item.NewUrl;
                item.Track.Tags?.Remove(DeadTag);
                _library.Update(item.Track);
                changed++;
                Log.Information("[RadioHealer] Repaired {Title} -> {Url}", item.Track.Title, item.NewUrl);
            }
            else if (item.Status == RadioHealStatus.Dead)
            {
                item.Track.Tags ??= new List<string>();
                if (!item.Track.Tags.Contains(DeadTag))
                {
                    item.Track.Tags.Add(DeadTag);
                    _library.Update(item.Track);
                    changed++;
                    Log.Information("[RadioHealer] Marked dead: {Title}", item.Track.Title);
                }
            }
        }
        return Task.FromResult(changed);
    }

    /// <summary>Auto mode: called on playback failure. Cooldown prevents retry loops.</summary>
    public async Task<bool> TryHealOnFailureAsync(Track track, CancellationToken ct = default)
    {
        if (!_prefs.Current.AutoHealRadioStreams || track.MediaType != MediaType.Radio)
            return false;

        var uuid = StationUuid(track);
        if (uuid == null) return false;

        lock (_lastAttempt)
        {
            if (_lastAttempt.TryGetValue(track.Id, out var last) && DateTime.UtcNow - last < Cooldown)
                return false;
            _lastAttempt[track.Id] = DateTime.UtcNow;
        }

        var provider = Provider;
        if (provider == null) return false;

        var station = await provider.GetStationAsync(uuid, ct);
        var newUrl = station?.StreamUrl?.Trim();
        if (string.IsNullOrWhiteSpace(newUrl) ||
            string.Equals(newUrl, track.Url?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        track.Url = newUrl;
        track.Tags?.Remove(DeadTag);
        _library.Update(track);
        Log.Information("[RadioHealer] Auto-repaired {Title} -> {Url}", track.Title, newUrl);
        return true;
    }
}

// PlaybackNavigator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public enum RepeatMode { None, One, All }

public class PlaybackNavigator
{
    private readonly LibraryService _library;
    private readonly Random _rng = new();
    
    private List<Guid> _shuffleDeck = new();
    private int _shuffleIndex = -1;
    private readonly Stack<Guid> _history = new();

    public bool IsShuffle { get; set; }
    public bool IsSmartShuffle { get; set; }
    public RepeatMode RepeatMode { get; set; } = RepeatMode.None;

    public int SkipPenaltyCap { get; set; } = 3;
    
    public PlaybackNavigator(LibraryService library)
    {
        _library = library;
    }
    
    public void CycleRepeat()
    {
        RepeatMode = RepeatMode switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            RepeatMode.One => RepeatMode.None,
            _ => RepeatMode.None
        };
    }

    private void BuildShuffleDeck()
    {
        var allTracks = _library.GetAll();
        _shuffleDeck = (IsSmartShuffle && SkipPenaltyCap > 0
                ? allTracks.Where(t => t.SkipCount < SkipPenaltyCap)
                : allTracks)
            .Select(t => t.Id)
            .ToList();

        for (int i = _shuffleDeck.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_shuffleDeck[i], _shuffleDeck[j]) = (_shuffleDeck[j], _shuffleDeck[i]);
        }
        _shuffleIndex = -1;

        Log.Debug("[PlaybackNavigator] Shuffle deck built: {Count} tracks " +
            "(cap={Cap}, smart={Smart})",
            _shuffleDeck.Count, SkipPenaltyCap, IsSmartShuffle);
    }

    public Track? GetNextTrack(Track? currentTrack)
    {
        var queue = _library.GetAll().ToList();
        if (queue.Count == 0) return null;

        if (currentTrack != null) _history.Push(currentTrack.Id);

        if (IsShuffle)
        {
            if (IsSmartShuffle && currentTrack != null && _rng.NextDouble() < 0.3)
            {
                var smart = GetSmartRecommendation(currentTrack, queue);
                if (smart != null) return smart;
            }

            if (_shuffleDeck.Count == 0 || _shuffleIndex >= _shuffleDeck.Count - 1)
                BuildShuffleDeck();

            _shuffleIndex++;
            var nextId = _shuffleDeck[_shuffleIndex];
            return queue.FirstOrDefault(t => t.Id == nextId);
        }
        
        if (currentTrack == null) return queue[0];
        var idx = queue.FindIndex(t => t.Id == currentTrack.Id);
        
        if (idx >= 0 && idx < queue.Count - 1)
            return queue[idx + 1];
            
        if (RepeatMode == RepeatMode.All)
            return queue[0];
            
        return null;
    }

    private Track? GetSmartRecommendation(Track current, List<Track> queue)
    {
        var candidates = queue.Where(t =>
            t.Id != current.Id &&
            !_history.Contains(t.Id) &&
            (SkipPenaltyCap <= 0 || t.SkipCount < SkipPenaltyCap))
            .ToList();
        if (candidates.Count == 0) return null;

        var scored = candidates.Select(t => new {
            Track = t,
            Score = (t.Artist == current.Artist && t.Artist != "Unknown" ? 5 : 0) +
                    t.Tags.Intersect(current.Tags).Count() * 2 +
                    (t.IsFavorite ? 1 : 0) -
                    t.SkipCount
        }).OrderByDescending(x => x.Score).ThenBy(_ => _rng.Next()).ToList();

        var top = scored.Take(Math.Max(5, scored.Count / 10)).ToList();
        return top.Any() ? top[_rng.Next(top.Count)].Track : null;
    }
    
    public Track? GetPreviousTrack(Track? currentTrack)
    {
        var queue = _library.GetAll().ToList();
        if (queue.Count == 0) return null;

        if (IsShuffle && _history.Count > 0)
        {
            var prevId = _history.Pop();
            return queue.FirstOrDefault(t => t.Id == prevId);
        }

        if (currentTrack == null) return null;
        var idx = queue.FindIndex(t => t.Id == currentTrack.Id);
        
        if (idx > 0)
            return queue[idx - 1];
            
        if (RepeatMode == RepeatMode.All)
            return queue[^1];
            
        return null;
    }
    
    public bool ShouldRepeatCurrent() => RepeatMode == RepeatMode.One;
}
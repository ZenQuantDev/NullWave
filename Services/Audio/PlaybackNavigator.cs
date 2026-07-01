using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public enum RepeatMode { None, One, All }

public partial class PlaybackNavigator : ObservableObject
{
    private readonly LibraryService _library;
    private readonly Random _rng = new();
    
    private List<Guid> _shuffleDeck = new();
    private int _shuffleIndex = -1;
    private readonly Stack<Guid> _history = new();

    // O(1) Lookup Cache
    private int _cachedLibraryVersion = -1;
    private IReadOnlyList<Track> _cachedQueue = new List<Track>();
    private Dictionary<Guid, int> _trackIndexMap = new();

    [ObservableProperty]
    private bool _isShuffle;

    [ObservableProperty]
    private bool _isSmartShuffle;

    [ObservableProperty]
    private RepeatMode _repeatMode = RepeatMode.None;

    [ObservableProperty]
    private Track? _currentTrack;
    
    /// <summary>
    /// Tracks with SkipCount >= this value are excluded from Smart Shuffle.
    /// </summary>
    public int SkipPenaltyCap { get; set; } = 3;
    
    public PlaybackNavigator(LibraryService library)
    {
        _library = library;
    }

    private void EnsureIndexMap(IReadOnlyList<Track> queue)
    {
        if (_cachedLibraryVersion != _library.StateVersion)
        {
            _cachedLibraryVersion = _library.StateVersion;
            _cachedQueue = queue;
            _trackIndexMap.Clear();
            for (int i = 0; i < queue.Count; i++)
            {
                _trackIndexMap[queue[i].Id] = i;
            }
        }
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

        Log.Debug("[PlaybackNavigator] Shuffle deck built: {Count} tracks (cap={Cap}, smart={Smart})",
            _shuffleDeck.Count, SkipPenaltyCap, IsSmartShuffle);
    }

    public Track? GetNextTrack(Track? currentTrack)
    {
        var queue = _library.GetAll();
        if (queue.Count == 0) return null;

        EnsureIndexMap(queue);

        if (currentTrack != null) _history.Push(currentTrack.Id);

        if (IsShuffle)
        {
            if (IsSmartShuffle && currentTrack != null && _rng.NextDouble() < 0.3)
            {
                var smart = GetSmartRecommendation(currentTrack, queue);
                if (smart != null) 
                {
                    CurrentTrack = smart;
                    return smart;
                }
            }

            if (_shuffleDeck.Count == 0 || _shuffleIndex >= _shuffleDeck.Count - 1)
                BuildShuffleDeck();

            _shuffleIndex++;
            var nextId = _shuffleDeck[_shuffleIndex];
            
            Track? nextTrack = null;
            if (_trackIndexMap.TryGetValue(nextId, out var idx))
                nextTrack = queue[idx];
            else
                nextTrack = queue.FirstOrDefault(t => t.Id == nextId);
                
            CurrentTrack = nextTrack;
            return nextTrack;
        }
        
        if (currentTrack == null) 
        {
            CurrentTrack = queue[0];
            return queue[0];
        }
        
        if (_trackIndexMap.TryGetValue(currentTrack.Id, out var currentIdx))
        {
            if (currentIdx >= 0 && currentIdx < queue.Count - 1)
            {
                CurrentTrack = queue[currentIdx + 1];
                return queue[currentIdx + 1];
            }
        }
        
        if (RepeatMode == RepeatMode.All)
        {
            CurrentTrack = queue[0];
            return queue[0];
        }
            
        CurrentTrack = null;
        return null;
    }

    private Track? GetSmartRecommendation(Track current, IReadOnlyList<Track> queue)
    {
        var historySet = _history.ToHashSet();
        var currentTags = current.Tags.ToHashSet();

        var scored = queue
            .Where(t => t.Id != current.Id &&
                        !historySet.Contains(t.Id) &&
                        (SkipPenaltyCap <= 0 || t.SkipCount < SkipPenaltyCap))
            .Select(t => 
            {
                int matchingTags = 0;
                foreach (var tag in t.Tags)
                {
                    if (currentTags.Contains(tag)) matchingTags++;
                }

                int score = (t.Artist == current.Artist && t.Artist != "Unknown" ? 5 : 0) +
                            (matchingTags * 2) +
                            (t.IsFavorite ? 1 : 0) -
                            t.SkipCount;

                return (Track: t, Score: score);
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(_ => _rng.Next())
            .ToList();

        var top = scored.Take(Math.Max(5, scored.Count / 10)).ToList();
        return top.Any() ? top[_rng.Next(top.Count)].Track : null;
    }
    
    public Track? GetPreviousTrack(Track? currentTrack)
    {
        var queue = _library.GetAll();
        if (queue.Count == 0) return null;

        EnsureIndexMap(queue);

        if (IsShuffle && _history.Count > 0)
        {
            var prevId = _history.Pop();
            Track? prevTrack = null;
            if (_trackIndexMap.TryGetValue(prevId, out var pIdx))
                prevTrack = queue[pIdx];
            else
                prevTrack = queue.FirstOrDefault(t => t.Id == prevId);
                
            CurrentTrack = prevTrack;
            return prevTrack;
        }

        if (currentTrack == null) return null;
        
        if (_trackIndexMap.TryGetValue(currentTrack.Id, out var idx))
        {
            if (idx > 0)
            {
                CurrentTrack = queue[idx - 1];
                return queue[idx - 1];
            }
        }
            
        if (RepeatMode == RepeatMode.All)
        {
            CurrentTrack = queue[^1];
            return queue[^1];
        }
            
        CurrentTrack = null;
        return null; 
    }
    
    public bool ShouldRepeatCurrent() => RepeatMode == RepeatMode.One;
}
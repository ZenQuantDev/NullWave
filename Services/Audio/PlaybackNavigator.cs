using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;

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
        _shuffleDeck = _library.GetAll().Select(t => t.Id).ToList();
        // Fisher-Yates shuffle guarantees every track plays exactly once before reshuffling
        for (int i = _shuffleDeck.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (_shuffleDeck[i], _shuffleDeck[j]) = (_shuffleDeck[j], _shuffleDeck[i]);
        }
        _shuffleIndex = -1;
    }

    public Track? GetNextTrack(Track? currentTrack)
    {
        var queue = _library.GetAll().ToList();
        if (queue.Count == 0) return null;

        if (currentTrack != null) _history.Push(currentTrack.Id);

        if (IsShuffle)
        {
            // Smart Shuffle: 30% chance to inject a contextually relevant track
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
        
        // Sequential mode
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
        var candidates = queue.Where(t => t.Id != current.Id && !_history.Contains(t.Id)).ToList();
        if (candidates.Count == 0) return null;

        // Score tracks based on artist match, shared tags, and favorite status
        var scored = candidates.Select(t => new {
            Track = t,
            Score = (t.Artist == current.Artist && t.Artist != "Unknown" ? 5 : 0) +
                    t.Tags.Intersect(current.Tags).Count() * 2 +
                    (t.IsFavorite ? 1 : 0)
        }).OrderByDescending(x => x.Score).ThenBy(_ => _rng.Next()).ToList();

        // Pick randomly from the top 10% (or top 5) to keep it feeling fresh but relevant
        var top = scored.Take(Math.Max(5, scored.Count / 10)).ToList();
        return top.Any() ? top[_rng.Next(top.Count)].Track : null;
    }
    
    public Track? GetPreviousTrack(Track? currentTrack)
    {
        var queue = _library.GetAll().ToList();
        if (queue.Count == 0) return null;

        // If shuffling, pop the actual last played track from history
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
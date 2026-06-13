using System;
using System.Collections.Generic;
using System.Linq;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.Services;

public enum RepeatMode { None, One, All }

/// <summary>
/// Manages playback navigation, shuffle/repeat state, and queue logic.
/// Extracted from PlayerViewModel to reduce complexity.
/// </summary>
public class PlaybackNavigator
{
    private readonly LibraryService _library;
    private readonly Random _rng = new();
    
    private bool _isShuffle;
    private RepeatMode _repeatMode = RepeatMode.None;
    
    public bool IsShuffle
    {
        get => _isShuffle;
        set => _isShuffle = value;
    }
    
    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set => _repeatMode = value;
    }
    
    public PlaybackNavigator(LibraryService library)
    {
        _library = library;
    }
    
    public void ToggleShuffle()
    {
        IsShuffle = !IsShuffle;
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
    
    /// <summary>
    /// Get the next track to play based on current state.
    /// Returns null if no next track available.
    /// </summary>
    public Track? GetNextTrack(Track? currentTrack)
    {
        var queue = _library.GetAll().ToList();
        if (queue.Count == 0) return null;
        
        // Shuffle mode: pick random track (not the same as current)
        if (IsShuffle)
        {
            if (queue.Count == 1) return queue[0];
            
            Track next;
            do
            {
                next = queue[_rng.Next(queue.Count)];
            } while (currentTrack != null && next.Id == currentTrack.Id);
            
            return next;
        }
        
        // Sequential mode
        if (currentTrack == null) return queue[0];
        
        var idx = queue.FindIndex(t => t.Id == currentTrack.Id);
        
        // Found current track in queue
        if (idx >= 0)
        {
            // More tracks available
            if (idx < queue.Count - 1)
                return queue[idx + 1];
            
            // End of queue - wrap if repeat all
            if (RepeatMode == RepeatMode.All)
                return queue[0];
            
            return null; // End of library
        }
        
        // Current track not in queue (shouldn't happen, but handle gracefully)
        return queue[0];
    }
    
    /// <summary>
    /// Get the previous track to play.
    /// Returns null if no previous track available.
    /// </summary>
    public Track? GetPreviousTrack(Track? currentTrack)
    {
        var queue = _library.GetAll().ToList();
        if (queue.Count == 0 || currentTrack == null) return null;
        
        var idx = queue.FindIndex(t => t.Id == currentTrack.Id);
        
        if (idx > 0)
            return queue[idx - 1];
        
        // At start of queue - wrap if repeat all
        if (RepeatMode == RepeatMode.All)
            return queue[^1];
        
        return null;
    }
    
    /// <summary>
    /// Check if we should repeat the current track.
    /// </summary>
    public bool ShouldRepeatCurrent()
    {
        return RepeatMode == RepeatMode.One;
    }
}
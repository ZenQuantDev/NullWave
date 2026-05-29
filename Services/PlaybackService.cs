using System.Linq;
using System;
using LibVLCSharp.Shared;
using Serilog;

namespace NullWave.Services;

public class PlaybackService : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private bool _disposed;

    public event Action<float>? PositionChanged;
    public event Action<PlaybackState>? StateChanged;
    public event Action? TrackFinished;

    public bool IsPlaying => _player.IsPlaying;
    public bool IsPaused => !_player.IsPlaying && _player.Media != null;
    public float Volume
    {
        get => _player.Volume / 100f;
        set => _player.Volume = (int)(value * 100);
    }

    public TimeSpan Position => TimeSpan.FromMilliseconds(_player.Time);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_player.Length);

    public PlaybackService()
    {
        Core.Initialize();
        _libVlc = new LibVLC();
        _player = new MediaPlayer(_libVlc);

        _player.PositionChanged += (_, e) =>
            PositionChanged?.Invoke(e.Position);

        _player.Playing += (_, _) =>
            StateChanged?.Invoke(PlaybackState.Playing);

        _player.Paused += (_, _) =>
            StateChanged?.Invoke(PlaybackState.Paused);

        _player.Stopped += (_, _) =>
            StateChanged?.Invoke(PlaybackState.Stopped);

        _player.EndReached += (_, _) =>
        {
            StateChanged?.Invoke(PlaybackState.Stopped);
            TrackFinished?.Invoke();
        };
    }

    public void Play(string path)
    {
        try
        {
            var isUrl = path.StartsWith("http://") || path.StartsWith("https://");
            using var media = isUrl
                ? new Media(_libVlc, new Uri(path))
                : new Media(_libVlc, path);

            _player.Media = media;
            _player.Play();
            Log.Information("Playback started: {Path}", path);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Playback failed for {Path}", path);
        }
    }

    public void Pause()
    {
        if (_player.IsPlaying)
        {
            _player.Pause();
            Log.Debug("Playback paused");
        }
    }

    public void Resume()
    {
        if (!_player.IsPlaying)
        {
            _player.Play();
            Log.Debug("Playback resumed");
        }
    }

    public void Stop()
    {
        _player.Stop();
        Log.Debug("Playback stopped");
    }

    public void Seek(float position)
    {
        _player.Position = Math.Clamp(position, 0f, 1f);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _player.Stop();
        _player.Dispose();
        _libVlc.Dispose();
        _disposed = true;
    }
}

public enum PlaybackState
{
    Stopped,
    Playing,
    Paused
}
using System;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

public class PlaybackService : IDisposable
{
    private readonly LibVLC _libVlc;
    private MediaPlayer _player;
    private Media? _currentMedia;
    private bool _disposed;
    private CancellationTokenSource? _fadeCts;

    private bool _isFading;

    public event Action<float>? PositionChanged;
    public event Action<PlaybackState>? StateChanged;
    public event Action? TrackFinished;

    public bool IsPlaying => _player.IsPlaying;
    public bool IsPaused => !_player.IsPlaying && _player.Media != null;
    
    public float Volume
    {
        get => _player.Volume / 100f;
        set => _player.Volume = (int)Math.Clamp(value * 100, 0, 100);
    }

    public TimeSpan Position => TimeSpan.FromMilliseconds(_player.Time);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_player.Length);

    public PlaybackService()
    {
        // Windows: Explicitly point to VLC install directory if found
        var vlcDir = NullWave.Helpers.PlatformHelper.ResolveVlcDirectory();
        if (vlcDir != null)
        {
            Log.Information("[PlaybackService] Initializing LibVLC from: {Path}", vlcDir);
            Core.Initialize(vlcDir);
        }
        else
        {
            // Linux/Mac or Windows with VLC in PATH
            Core.Initialize();
        }

        _libVlc = new LibVLC();
        _player = new MediaPlayer(_libVlc);
        AttachEvents(_player);
    }

    private void AttachEvents(MediaPlayer p)
    {
        p.PositionChanged += OnPositionChanged;
        p.Playing += OnPlaying;
        p.Paused += OnPaused;
        p.Stopped += OnStopped;
        p.EndReached += OnEndReached;
    }

    private void DetachEvents(MediaPlayer p)
    {
        p.PositionChanged -= OnPositionChanged;
        p.Playing -= OnPlaying;
        p.Paused -= OnPaused;
        p.Stopped -= OnStopped;
        p.EndReached -= OnEndReached;
    }

    private void OnPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e) 
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PositionChanged?.Invoke(e.Position));
    }
    
    private void OnPlaying(object? sender, EventArgs e)
    {
        if (!_isFading)
        {
            _player.Volume = _player.Volume; 
        }
        
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StateChanged?.Invoke(PlaybackState.Playing));
    }

    private void OnPaused(object? sender, EventArgs e) 
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StateChanged?.Invoke(PlaybackState.Paused));
    }
    
    private void OnStopped(object? sender, EventArgs e) 
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StateChanged?.Invoke(PlaybackState.Stopped));
    }
    
    private void OnEndReached(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
        {
            StateChanged?.Invoke(PlaybackState.Stopped);
            TrackFinished?.Invoke();
        });
    }

    public void Play(string path)
    {
        try
        {
            _fadeCts?.Cancel();
            _isFading = false;
            _currentMedia?.Dispose();
            
            var isUrl = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            
            _currentMedia = isUrl ? new Media(_libVlc, new Uri(path)) : new Media(_libVlc, path);
                
            _player.Media = _currentMedia;
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
            _fadeCts?.Cancel();
            _isFading = false;
            _player.Pause();
            Log.Debug("Playback paused");
        }
    }

    public void Resume()
    {
        if (!_player.IsPlaying && _player.Media != null)
        {
            _fadeCts?.Cancel();
            _isFading = false;
            _player.Play();
            Log.Debug("Playback resumed");
        }
    }

    public void Stop()
    {
        _fadeCts?.Cancel();
        _isFading = false;
        _player.Stop();
        Log.Debug("Playback stopped");
    }

    public void Seek(float position)
    {
        _player.Position = Math.Clamp(position, 0f, 1f);
    }

    public async Task FadeAndPauseAsync(int durationMs)
    {
        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();
        _isFading = true;

        try
        {
            float originalVolume = Volume;
            await FadeVolumeAsync(_player, originalVolume, 0f, durationMs, _fadeCts.Token);

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                _player.Pause();
                Volume = originalVolume;
            }
        }
        finally
        {
            _isFading = false;
        }
    }

    public async Task FadeAndResumeAsync(int durationMs)
    {
        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();
        _isFading = true;

        try
        {
            float targetVolume = Volume > 0 ? Volume : 0.8f;
            _player.Volume = 0;
            _player.Play();

            await FadeVolumeAsync(_player, 0f, targetVolume, durationMs, _fadeCts.Token);
        }
        finally
        {
            _isFading = false;
        }
    }

    public async Task CrossfadeToAsync(string nextPath, int durationMs, float targetVolume)
    {
        if (string.IsNullOrWhiteSpace(nextPath))
        {
            Log.Debug("[PlaybackService] Crossfade skipped: No next track path provided.");
            return;
        }

        var isUrl = nextPath.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        var nextMedia = isUrl ? new Media(_libVlc, new Uri(nextPath)) : new Media(_libVlc, nextPath);
        var nextPlayer = new MediaPlayer(_libVlc) { Media = nextMedia };

        var oldPlayer = _player;
        var oldMedia = _currentMedia;

        DetachEvents(oldPlayer);
        _player = nextPlayer;
        _currentMedia = nextMedia;
        AttachEvents(_player);

        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();
        _isFading = true;

        try
        {
            nextPlayer.Volume = 0;
            nextPlayer.Play();

            var fadeOutTask = FadeVolumeAsync(oldPlayer, oldPlayer.Volume / 100f, 0f, durationMs, _fadeCts.Token);
            var fadeInTask = FadeVolumeAsync(nextPlayer, 0f, targetVolume, durationMs, _fadeCts.Token);

            await Task.WhenAll(fadeOutTask, fadeInTask);

            // CRITICAL FIX: Stop the old player and give the native audio backend (PipeWire) 
            // a wider safety margin to release its resources before disposing the MediaPlayer.
            // Disposing a MediaPlayer while it's still actively tearing down its audio stream 
            // is a known cause of segfaults in libvlc/libpipewire on Linux.
            oldPlayer.Stop();
            
            // Increased delay to 400ms to account for concurrent system load (AI/downloads)
            await Task.Delay(400); 
            
            oldPlayer.Dispose();
            oldMedia?.Dispose();
        }
        catch (OperationCanceledException)
        {
            // If cancelled, we still need to clean up the old player safely
            oldPlayer.Stop();
            await Task.Delay(400);
            oldPlayer.Dispose();
            oldMedia?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlaybackService] Error during crossfade");
            oldPlayer.Stop();
            await Task.Delay(400);
            oldPlayer.Dispose();
            oldMedia?.Dispose();
        }
        finally
        {
            _isFading = false;
        }
    }

    private async Task FadeVolumeAsync(MediaPlayer p, float start, float end, int durationMs, CancellationToken ct)
    {
        int stepDelay = 32;
        int steps = durationMs / stepDelay;
        if (steps <= 0) steps = 1;
        
        for (int i = 1; i <= steps; i++)
        {
            if (ct.IsCancellationRequested) return;
            
            float progress = (float)i / steps;
            float ease = (float)Math.Pow(progress, 2);
            float current = start + (end - start) * ease;
            
            await Task.Delay(stepDelay, ct);
            
            if (ct.IsCancellationRequested) return;
            
            p.Volume = (int)Math.Clamp(current * 100, 0, 100);
        }
        
        if (ct.IsCancellationRequested) return;
        p.Volume = (int)Math.Clamp(end * 100, 0, 100);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _player.Stop();
        
        // Same widened safety delay for final disposal to prevent native segfaults
        Task.Delay(400).Wait(); 
        
        _currentMedia?.Dispose();
        _player.Dispose();
        _libVlc.Dispose();
        _disposed = true;
    }
}

public enum PlaybackState { Stopped, Playing, Paused }
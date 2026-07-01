using System;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Serilog;

namespace NullWave.Services;

public class PlaybackService : IDisposable
{
    private readonly LibVLC _libVlc;
    private MediaPlayer _player;
    private Media? _currentMedia;
    private bool _disposed;
    private CancellationTokenSource? _fadeCts;

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
        Core.Initialize();
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
        // Marshal to UI thread to prevent cross-thread exceptions in ViewModels
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PositionChanged?.Invoke(e.Position));
    }
    
    private void OnPlaying(object? sender, EventArgs e)
    {
        // Native volume fix must happen immediately on the native thread
        if (_fadeCts == null || _fadeCts.IsCancellationRequested)
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
            _player.Pause();
            Log.Debug("Playback paused");
        }
    }

    public void Resume()
    {
        if (!_player.IsPlaying && _player.Media != null)
        {
            _fadeCts?.Cancel();
            _player.Play();
            Log.Debug("Playback resumed");
        }
    }

    public void Stop()
    {
        _fadeCts?.Cancel();
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
        
        float originalVolume = Volume;
        await FadeVolumeAsync(_player, originalVolume, 0f, durationMs, _fadeCts.Token);
        
        if (!_fadeCts.Token.IsCancellationRequested)
        {
            _player.Pause();
            Volume = originalVolume;
        }
    }

    public async Task FadeAndResumeAsync(int durationMs)
    {
        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();
        
        float targetVolume = Volume > 0 ? Volume : 0.8f;
        _player.Volume = 0;
        _player.Play();
        
        await FadeVolumeAsync(_player, 0f, targetVolume, durationMs, _fadeCts.Token);
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
        
        nextPlayer.Volume = 0;
        nextPlayer.Play();

        var oldPlayer = _player;
        var oldMedia = _currentMedia;

        _player = nextPlayer;
        _currentMedia = nextMedia;
        
        DetachEvents(oldPlayer);
        AttachEvents(_player);

        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();
        
        var fadeOutTask = FadeVolumeAsync(oldPlayer, oldPlayer.Volume / 100f, 0f, durationMs, _fadeCts.Token);
        var fadeInTask = FadeVolumeAsync(nextPlayer, 0f, targetVolume, durationMs, _fadeCts.Token);
        
        await Task.WhenAll(fadeOutTask, fadeInTask);

        oldPlayer.Stop();
        oldPlayer.Dispose();
        oldMedia?.Dispose();
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
        _currentMedia?.Dispose();
        _player.Dispose();
        _libVlc.Dispose();
        _disposed = true;
    }
}

public enum PlaybackState { Stopped, Playing, Paused }
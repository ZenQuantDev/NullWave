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
    private readonly MediaPlayer _playerA;
    private readonly MediaPlayer _playerB;
    private MediaPlayer _activePlayer;
    private MediaPlayer _standbyPlayer;
    private readonly object _standbyLock = new();
    private Media? _currentMedia;
    private bool _disposed;

    // FIX: Separate CTS instances. Pause/Resume must NEVER abort an in-flight
    // crossfade (shared CTS previously left the incoming player silent at volume 0).
    private CancellationTokenSource? _fadeCts;
    private CancellationTokenSource? _crossfadeCts;
    private volatile bool _isCrossfading;
    private int _crossfadeGeneration;
    private int _targetVolume = 80;
    private int _playRequestGeneration;

    /// <summary>Current crossfade generation. Bumped whenever a crossfade is aborted.</summary>
    public int CrossfadeGeneration => Volatile.Read(ref _crossfadeGeneration);
    public bool IsCrossfading => _isCrossfading;

    // ICY Metadata Polling for Radio Streams
    private System.Threading.Timer? _icyTimer;
    private string _lastIcyTitle = string.Empty;
    private string _lastIcyArtist = string.Empty;

    public event Action<float>? PositionChanged;
    public event Action<PlaybackState>? StateChanged;
    public event Action? TrackFinished;
    public event Action<TimeSpan>? DurationDiscovered;
    public event Action<string, string>? RadioMetadataChanged;
    public event Action<string>? StreamFailed;

    public bool IsPlaying => _activePlayer.IsPlaying;
    public bool IsPaused => !_activePlayer.IsPlaying && _activePlayer.Media != null;

    public float Volume
    {
        get => _activePlayer.Volume / 100f;
        set
        {
            _targetVolume = (int)Math.Clamp(value * 100, 0, 100);
            _activePlayer.Volume = _targetVolume;
        }
    }

    public TimeSpan Position => TimeSpan.FromMilliseconds(_activePlayer.Time);
    public TimeSpan Duration => TimeSpan.FromMilliseconds(_activePlayer.Length);

    public PlaybackService()
    {
        var vlcDir = NullWave.Helpers.PlatformHelper.ResolveVlcDirectory();
        if (vlcDir != null)
        {
            Log.Information("[PlaybackService] Initializing LibVLC from: {Path}", vlcDir);
            Core.Initialize(vlcDir);
        }
        else
        {
            Core.Initialize();
        }

        _libVlc = new LibVLC("--aout=directsound", "--no-video", "--quiet");
        _playerA = new MediaPlayer(_libVlc);
        _playerB = new MediaPlayer(_libVlc);
        _activePlayer = _playerA;
        _standbyPlayer = _playerB;
        AttachEvents(_playerA);
        AttachEvents(_playerB);
    }

    private void AttachEvents(MediaPlayer player)
    {
        player.PositionChanged += (s, e) => OnPositionChanged(player, e);
        player.Playing += (s, e) => OnPlaying(player, e);
        player.Paused += (s, e) => OnPaused(player, e);
        player.Stopped += (s, e) => OnStopped(player, e);
        player.EndReached += (s, e) => OnEndReached(player, e);
        player.EncounteredError += (s, e) => OnEncounteredError(player, e);
    }

    private void OnPositionChanged(MediaPlayer player, MediaPlayerPositionChangedEventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PositionChanged?.Invoke(e.Position));
    }

    private void OnPlaying(MediaPlayer player, EventArgs e)
    {
        bool isActive = ReferenceEquals(player, _activePlayer);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (isActive && !_isCrossfading)
                player.Volume = _targetVolume;
            if (!isActive) return;
            StateChanged?.Invoke(PlaybackState.Playing);
            if (player.Length > 0)
                DurationDiscovered?.Invoke(TimeSpan.FromMilliseconds(player.Length));
        });
    }

    private void OnPaused(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StateChanged?.Invoke(PlaybackState.Paused));
    }

    private void OnStopped(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => StateChanged?.Invoke(PlaybackState.Stopped));
    }

    private void OnEndReached(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StateChanged?.Invoke(PlaybackState.Stopped);
            TrackFinished?.Invoke();
        });
    }

    private void OnEncounteredError(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        _icyTimer?.Dispose();
        _icyTimer = null;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            StreamFailed?.Invoke("Stream stopped or unreachable");
            StateChanged?.Invoke(PlaybackState.Stopped);
        });
    }

    public void Play(string path)
    {
        try
        {
            var playRequest = Interlocked.Increment(ref _playRequestGeneration);
            // Abort any in-flight crossfade before taking over the active player.
            AbortCrossfade();

            _fadeCts?.Cancel();

            _activePlayer.Stop();
            _currentMedia?.Dispose();
            _currentMedia = null;

            // Reset ICY Poller
            _icyTimer?.Dispose();
            _icyTimer = null;
            _lastIcyTitle = string.Empty;
            _lastIcyArtist = string.Empty;

            var isUrl = path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

            _currentMedia = isUrl ? new Media(_libVlc, new Uri(path)) : new Media(_libVlc, path);

            _activePlayer.Media = _currentMedia;
            _activePlayer.Volume = _targetVolume;
            _activePlayer.Play();
            Log.Information("Playback started: {Path}", path);

            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                var state = _activePlayer.State;
                if (playRequest != Volatile.Read(ref _playRequestGeneration)
                    || _currentMedia == null
                    || _activePlayer.IsPlaying
                    || (state != VLCState.Error && state != VLCState.Ended))
                    return;

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    var currentState = _activePlayer.State;
                    if (playRequest == Volatile.Read(ref _playRequestGeneration)
                        && _currentMedia != null
                        && !_activePlayer.IsPlaying
                        && (currentState == VLCState.Error || currentState == VLCState.Ended))
                        StateChanged?.Invoke(PlaybackState.Stopped);
                });
            });

            if (isUrl)
            {
                _icyTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        if (_currentMedia == null) return;

                        var nowPlaying = _currentMedia.Meta(MetadataType.NowPlaying);
                        var titleMeta = _currentMedia.Meta(MetadataType.Title);
                        var artistMeta = _currentMedia.Meta(MetadataType.Artist);

                        var title = nowPlaying ?? titleMeta ?? string.Empty;
                        var artist = artistMeta ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(artist) && title.Contains(" - "))
                        {
                            var parts = title.Split(" - ", StringSplitOptions.None);
                            if (parts.Length == 2)
                            {
                                artist = parts[0].Trim();
                                title = parts[1].Trim();
                            }
                        }

                        if (title != _lastIcyTitle || artist != _lastIcyArtist)
                        {
                            _lastIcyTitle = title;
                            _lastIcyArtist = artist;
                            Avalonia.Threading.Dispatcher.UIThread.Post(() => RadioMetadataChanged?.Invoke(title, artist));
                        }
                    }
                    catch { /* ignore native teardown exceptions */ }
                }, null, 1500, 3000);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Playback failed for {Path}", path);
        }
    }

    public void Pause()
    {
        if (_activePlayer.IsPlaying)
        {
            // FIX: Only cancel pause-fades. A crossfade must survive pausing:
            // the old player still fades out and is cleaned up by the crossfade itself.
            _fadeCts?.Cancel();
            _activePlayer.Pause();
            Log.Debug("Playback paused");
        }
    }

    public void Resume()
    {
        if (!_activePlayer.IsPlaying && _activePlayer.Media != null)
        {
            _fadeCts?.Cancel();
            _activePlayer.Volume = _targetVolume;
            _activePlayer.Play();
            Log.Debug("Playback resumed");
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _playRequestGeneration);
        _fadeCts?.Cancel();
        AbortCrossfade(); // Stop kills everything, including an in-flight crossfade
        _icyTimer?.Dispose();
        _icyTimer = null;
        _activePlayer.Stop();
        Log.Debug("Playback stopped");
    }

    private void AbortCrossfade()
    {
        _crossfadeCts?.Cancel();
        _crossfadeCts?.Dispose();
        _crossfadeCts = null;
        _isCrossfading = false;
        lock (_standbyLock)
        {
            try { _standbyPlayer.Stop(); _standbyPlayer.Volume = 0; } catch { }
        }
    }

    public void Seek(float position)
    {
        _activePlayer.Position = Math.Clamp(position, 0f, 1f);
    }

    public void SetRate(float rate)
    {
        try { _activePlayer.SetRate(rate); } catch { /* ignore native teardown */ }
    }

    public async Task FadeAndPauseAsync(int durationMs)
    {
        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();

        try
        {
            float originalVolume = Volume;
            await FadeVolumeAsync(_activePlayer, originalVolume, 0f, durationMs, _fadeCts.Token);

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                _activePlayer.Pause();
                Volume = originalVolume;
            }
        }
        catch (OperationCanceledException)
        {
            // A newer fade superseded this one.
        }
        finally
        {
        }
    }

    public async Task FadeAndResumeAsync(int durationMs)
    {
        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();

        try
        {
            float targetVolume = Volume > 0 ? Volume : 0.8f;
            _activePlayer.Volume = 0;
            _activePlayer.Play();

            await FadeVolumeAsync(_activePlayer, 0f, targetVolume, durationMs, _fadeCts.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer fade superseded this one.
        }
        finally
        {
        }
    }

    /// <summary>
    /// Returns the crossfade generation. If the crossfade is aborted mid-flight
    /// (Stop / new Play), the generation is bumped so the caller's continuation
    /// can detect the abort and discard itself.
    /// </summary>
    public async Task<int> CrossfadeToAsync(string nextPath, int durationMs, float targetVolume)
    {
        if (string.IsNullOrWhiteSpace(nextPath))
        {
            Log.Debug("[PlaybackService] Crossfade skipped: No next track path provided.");
            return CrossfadeGeneration;
        }

        AbortCrossfade();
        int generation = Interlocked.Increment(ref _crossfadeGeneration);

        MediaPlayer incoming;
        lock (_standbyLock)
        {
            incoming = _standbyPlayer;
            incoming.Stop();
            incoming.Media?.Dispose();
            var isUrl = nextPath.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            incoming.Media = isUrl ? new Media(_libVlc, new Uri(nextPath)) : new Media(_libVlc, nextPath);
        }

        var outgoing = _activePlayer;
        _crossfadeCts = new CancellationTokenSource();
        var ct = _crossfadeCts.Token;
        _isCrossfading = true;

        try
        {
            incoming.Volume = 0;
            incoming.Play();
            incoming.Volume = incoming.Volume;

            var fadeOutTask = FadeVolumeAsync(outgoing, outgoing.Volume / 100f, 0f, durationMs, ct);
            var fadeInTask = FadeVolumeAsync(incoming, 0f, targetVolume, durationMs, ct);

            await Task.WhenAll(fadeOutTask, fadeInTask);

            var promoted = false;
            lock (_standbyLock)
            {
                if (!ct.IsCancellationRequested)
                {
                    incoming.Volume = (int)Math.Clamp(targetVolume * 100, 0, 100);
                    _activePlayer = incoming;
                    _standbyPlayer = outgoing;
                    _currentMedia = incoming.Media;
                    promoted = true;
                }
            }

            if (promoted)
            {
                outgoing.Stop();
            }
        }
        catch (OperationCanceledException)
        {
            if (Volatile.Read(ref _crossfadeGeneration) == generation)
            {
                Interlocked.Increment(ref _crossfadeGeneration);
                _isCrossfading = false;
                lock (_standbyLock)
                {
                    try { incoming.Stop(); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[PlaybackService] Error during crossfade");
            if (Volatile.Read(ref _crossfadeGeneration) == generation)
            {
                Interlocked.Increment(ref _crossfadeGeneration);
                _isCrossfading = false;
                lock (_standbyLock)
                {
                    try { incoming.Stop(); } catch { }
                }
            }
        }
        finally
        {
            if (Volatile.Read(ref _crossfadeGeneration) == generation)
            {
                _isCrossfading = false;
            }
        }

        return generation;
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
        AbortCrossfade();
        _icyTimer?.Dispose();
        _icyTimer = null;
        _playerA.Stop();
        _playerB.Stop();

        _currentMedia?.Dispose();
        if (!ReferenceEquals(_playerA.Media, _currentMedia))
            _playerA.Media?.Dispose();
        if (!ReferenceEquals(_playerB.Media, _currentMedia))
            _playerB.Media?.Dispose();
        _playerA.Dispose();
        _playerB.Dispose();
        _libVlc.Dispose();
        _disposed = true;
    }
}

public enum PlaybackState { Stopped, Playing, Paused }
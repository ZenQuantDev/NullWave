using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services;

/// <summary>The native handles. Created on first use (or by WarmUpAsync), never in the constructor.</summary>
internal sealed record NativeEngine(LibVLC Vlc, MediaPlayer A, MediaPlayer B);

public class PlaybackService : IDisposable
{
    // --- Native engine (null until EnsureReady succeeds; every public member checks _ready first) ---
    private LibVLC _libVlc = null!;
    private MediaPlayer _playerA = null!;
    private MediaPlayer _playerB = null!;
    private MediaPlayer _activePlayer = null!;
    private MediaPlayer _standbyPlayer = null!;

    private readonly Func<NativeEngine> _engineFactory;
    private readonly Action<Action> _uiPost;
    private readonly object _initLock = new();
    private volatile bool _ready;          // written last, after every engine field is assigned
    private string? _engineError;          // guarded by _initLock
    private float? _pendingRate;           // guarded by _initLock; applied when the engine comes up
    private int _warmUpStarted;

    private readonly object _standbyLock = new();
    private Media? _currentMedia;
    private bool _disposed;                // guarded by _initLock

    private CancellationTokenSource? _fadeCts;
    private CancellationTokenSource? _crossfadeCts;
    private volatile bool _isCrossfading;
    private volatile bool _pausePending;
    private int _crossfadeGeneration;
    private int _targetVolume = 80;
    private int _playRequestGeneration;

    public int CrossfadeGeneration => Volatile.Read(ref _crossfadeGeneration);
    public bool IsCrossfading => _isCrossfading;

    /// <summary>True once LibVLC and both players exist.</summary>
    public bool IsEngineReady => _ready;

    /// <summary>Why the engine failed to start (for example VLC is not installed), or null.</summary>
    public string? EngineError
    {
        get { lock (_initLock) return _engineError; }
    }

    private System.Threading.Timer? _icyTimer;
    private string _lastIcyTitle = string.Empty;
    private string _lastIcyArtist = string.Empty;

    public event Action<float>? PositionChanged;
    public event Action<PlaybackState>? StateChanged;
    public event Action? TrackFinished;
    public event Action<TimeSpan>? DurationDiscovered;
    public event Action<string, string>? RadioMetadataChanged;
    public event Action<string>? StreamFailed;

    public bool IsPlaying => _ready && _activePlayer.IsPlaying;
    public bool IsPaused => _ready && !_activePlayer.IsPlaying && _activePlayer.Media != null;

    public float Volume
    {
        get
        {
            if (!_ready) return _targetVolume / 100f;
            return _isCrossfading ? _targetVolume / 100f : _activePlayer.Volume / 100f;
        }
        set
        {
            _targetVolume = (int)Math.Clamp(value * 100, 0, 100);
            if (!_ready) return;     // applied by Play(), which always sets the volume before starting

            _activePlayer.Volume = _targetVolume;

            if (_isCrossfading)
            {
                lock (_standbyLock)
                {
                    _standbyPlayer.Volume = _targetVolume;
                }
            }
        }
    }

    private MediaPlayer PositionSource => _isCrossfading ? _standbyPlayer : _activePlayer;

    public TimeSpan Position => _ready ? TimeSpan.FromMilliseconds(PositionSource.Time) : TimeSpan.Zero;
    public TimeSpan Duration => _ready ? TimeSpan.FromMilliseconds(PositionSource.Length) : TimeSpan.Zero;

    /// <summary>Cheap: no native code runs here. LibVLC starts in WarmUpAsync or on the first Play.</summary>
    public PlaybackService() : this(null, null) { }

    internal PlaybackService(Func<NativeEngine>? engineFactory, Action<Action>? uiPost)
    {
        _engineFactory = engineFactory ?? CreateNativeEngine;
        _uiPost = uiPost ?? (action => Dispatcher.UIThread.Post(action));
    }

    private static NativeEngine CreateNativeEngine()
    {
        var stopwatch = Stopwatch.StartNew();
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

        LibVLC? vlc = null;
        MediaPlayer? a = null;
        MediaPlayer? b = null;
        try
        {
            vlc = new LibVLC("--aout=directsound", "--no-video", "--quiet");
            a = new MediaPlayer(vlc);
            b = new MediaPlayer(vlc);
            Log.Information("[PlaybackService] LibVLC ready in {Ms} ms", stopwatch.ElapsedMilliseconds);
            return new NativeEngine(vlc, a, b);
        }
        catch
        {
            b?.Dispose();
            a?.Dispose();
            vlc?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts the engine on a pool thread. Safe to call more than once. Call it after the main
    /// window is visible so native loading does not compete with the first paint.
    /// </summary>
    public Task WarmUpAsync()
    {
        if (Interlocked.Exchange(ref _warmUpStarted, 1) == 1) return Task.CompletedTask;
        return Task.Run(() => { EnsureReady(); });
    }

    /// <summary>
    /// Creates the engine once. Blocks only if another thread is already creating it (the
    /// creation does not need the UI thread, so waiting on it cannot deadlock). A failure is
    /// remembered for the rest of the session instead of being retried on every click.
    /// </summary>
    private bool EnsureReady()
    {
        if (_ready) return true;

        lock (_initLock)
        {
            if (_ready) return true;
            if (_disposed || _engineError != null) return false;

            try
            {
                var engine = _engineFactory();
                _libVlc = engine.Vlc;
                _playerA = engine.A;
                _playerB = engine.B;
                _activePlayer = _playerA;
                _standbyPlayer = _playerB;
                AttachEvents(_playerA);
                AttachEvents(_playerB);

                if (_pendingRate is float rate)
                {
                    try { _activePlayer.SetRate(rate); } catch { /* ignore native teardown */ }
                    _pendingRate = null;
                }

                _ready = true;
                return true;
            }
            catch (Exception ex)
            {
                _engineError = ex.Message;
                Log.Error(ex, "[PlaybackService] Audio engine (LibVLC) failed to start");
                return false;
            }
        }
    }

    private void ReportEngineUnavailable()
    {
        string detail;
        lock (_initLock)
        {
            detail = _disposed ? "the audio service was shut down" : _engineError ?? "VLC could not be loaded";
        }

        var message = $"Audio engine unavailable: {detail}. Install VLC, then restart NullWave.";
        Log.Warning("[PlaybackService] Playback requested but the audio engine is unavailable: {Detail}", detail);
        _uiPost(() =>
        {
            StreamFailed?.Invoke(message);
            StateChanged?.Invoke(PlaybackState.Stopped);
        });
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
        bool isActive = ReferenceEquals(player, _activePlayer);
        bool isIncomingDuringFade = _isCrossfading && ReferenceEquals(player, _standbyPlayer);
        if (!isActive && !isIncomingDuringFade) return;
        _uiPost(() => PositionChanged?.Invoke(e.Position));
    }

    private void OnPlaying(MediaPlayer player, EventArgs e)
    {
        bool isActive = ReferenceEquals(player, _activePlayer);
        _uiPost(() =>
        {
            if (!isActive) return;
            if (_pausePending)
            {
                player.Pause();
                return;
            }
            if (!_isCrossfading)
                player.Volume = _targetVolume;
            StateChanged?.Invoke(PlaybackState.Playing);
            if (player.Length > 0)
                DurationDiscovered?.Invoke(TimeSpan.FromMilliseconds(player.Length));
        });
    }

    private void OnPaused(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        _uiPost(() => StateChanged?.Invoke(PlaybackState.Paused));
    }

    private void OnStopped(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        _uiPost(() => StateChanged?.Invoke(PlaybackState.Stopped));
    }

    private void OnEndReached(MediaPlayer player, EventArgs e)
    {
        if (!ReferenceEquals(player, _activePlayer)) return;
        _uiPost(() =>
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
        _uiPost(() =>
        {
            StreamFailed?.Invoke("Stream stopped or unreachable");
            StateChanged?.Invoke(PlaybackState.Stopped);
        });
    }

    public void Play(string path)
    {
        if (!EnsureReady())
        {
            ReportEngineUnavailable();
            return;
        }

        try
        {
            var playRequest = Interlocked.Increment(ref _playRequestGeneration);
            AbortCrossfade();
            _pausePending = false;

            _fadeCts?.Cancel();

            _activePlayer.Stop();
            _currentMedia?.Dispose();
            _currentMedia = null;

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

                _uiPost(() =>
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
                            _uiPost(() => RadioMetadataChanged?.Invoke(title, artist));
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
        if (!_ready) return;

        _pausePending = true;
        _fadeCts?.Cancel();

        if (_isCrossfading)
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _crossfadeCts = null;
            _isCrossfading = false;

            lock (_standbyLock)
            {
                var incoming = _standbyPlayer;
                var outgoing = _activePlayer;

                _activePlayer = incoming;
                _standbyPlayer = outgoing;
                _currentMedia = incoming.Media;

                try { outgoing.Stop(); } catch { }

                incoming.Volume = _targetVolume;
                incoming.Pause();
            }
            Log.Debug("Crossfade aborted and paused");
            return;
        }

        if (_activePlayer.Media != null)
        {
            _activePlayer.Pause();
            Log.Debug("Playback paused");
        }
    }

    public void Resume()
    {
        if (!_ready) return;

        _pausePending = false;
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
        if (!_ready) return;

        Interlocked.Increment(ref _playRequestGeneration);
        _fadeCts?.Cancel();
        _pausePending = false;
        AbortCrossfade();
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
        if (!_ready) return;
        PositionSource.Position = Math.Clamp(position, 0f, 1f);
    }

    public void SetRate(float rate)
    {
        lock (_initLock)
        {
            if (!_ready)
            {
                _pendingRate = rate;     // applied by EnsureReady once the engine exists
                return;
            }
        }

        try { _activePlayer.SetRate(rate); } catch { /* ignore native teardown */ }
    }

    public void ResyncState()
    {
        if (!_ready)
        {
            _uiPost(() => StateChanged?.Invoke(PlaybackState.Stopped));
            return;
        }

        var snapshot = _activePlayer.IsPlaying
            ? PlaybackState.Playing
            : (_activePlayer.Media != null && _activePlayer.State == VLCState.Paused ? PlaybackState.Paused : PlaybackState.Stopped);
        _uiPost(() => StateChanged?.Invoke(snapshot));
    }

    public async Task FadeAndPauseAsync(int durationMs)
    {
        if (!_ready) return;

        _pausePending = true;

        if (_isCrossfading)
        {
            _crossfadeCts?.Cancel();
            _crossfadeCts?.Dispose();
            _crossfadeCts = null;
            _isCrossfading = false;

            lock (_standbyLock)
            {
                var incoming = _standbyPlayer;
                var outgoing = _activePlayer;

                _activePlayer = incoming;
                _standbyPlayer = outgoing;
                _currentMedia = incoming.Media;

                try { outgoing.Stop(); } catch { }

                incoming.Volume = _targetVolume;
            }
        }

        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();

        try
        {
            float currentFadeVolume = _activePlayer.Volume / 100f;
            await FadeVolumeAsync(_activePlayer, currentFadeVolume, 0f, durationMs, _fadeCts.Token);

            if (!_fadeCts.Token.IsCancellationRequested)
            {
                _activePlayer.Pause();
                _activePlayer.Volume = _targetVolume;
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task FadeAndResumeAsync(int durationMs)
    {
        if (!_ready) return;

        _pausePending = false;
        _fadeCts?.Cancel();
        _fadeCts = new CancellationTokenSource();

        try
        {
            float targetVolume = _targetVolume / 100f;
            _activePlayer.Volume = 0;
            _activePlayer.Play();

            await FadeVolumeAsync(_activePlayer, 0f, targetVolume, durationMs, _fadeCts.Token);
        }
        catch (OperationCanceledException) { }
    }

    public async Task<int> CrossfadeToAsync(string nextPath, int durationMs, float targetVolume)
    {
        if (string.IsNullOrWhiteSpace(nextPath))
        {
            Log.Debug("[PlaybackService] Crossfade skipped: No next track path provided.");
            return CrossfadeGeneration;
        }

        if (!EnsureReady())
        {
            Log.Debug("[PlaybackService] Crossfade skipped: audio engine unavailable.");
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
                    if (ReferenceEquals(incoming, _standbyPlayer))
                    {
                        try { incoming.Stop(); } catch { }
                    }
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
                    if (ReferenceEquals(incoming, _standbyPlayer))
                    {
                        try { incoming.Stop(); } catch { }
                    }
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
        lock (_initLock)
        {
            if (_disposed) return;
            _disposed = true;   // EnsureReady refuses to start the engine after this
        }

        _fadeCts?.Cancel();
        _fadeCts?.Dispose();
        _icyTimer?.Dispose();
        _icyTimer = null;

        if (!_ready) return;    // the engine never started: nothing native to release

        AbortCrossfade();
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
    }
}

public enum PlaybackState { Stopped, Playing, Paused }
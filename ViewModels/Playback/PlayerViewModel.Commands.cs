using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers.Logging;

namespace NullWave.ViewModels;

public partial class PlayerViewModel
{
    private void InitializeCommands()
    {
        PlayPauseCommand = new RelayCommand(PlayPause);
        StopCommand = new RelayCommand(Stop);
        PlayTrackCommand = new RelayCommand<Track>(PlayTrack);
        DownloadTrackCommand = new RelayCommand<Track>(async t =>
        {
            if (t?.Url == null) return;
            IsDownloading = true;
            StatusText = L("Player_Status_StartingDownload");
            NullActionLogger.ImportStarted(t.Url, nameof(PlayerViewModel));
            await _download.DownloadAsync(t.Id.ToString(), t.Url, _settings.AudioFormat, _settings.AudioQuality);
        });

        SeekBackwardCommand = new RelayCommand(() => SeekRelative(-10));
        SeekForwardCommand = new RelayCommand(() => SeekRelative(10));
        SkipBack30Command = new RelayCommand(() => SeekRelative(-30));
        SkipForward30Command = new RelayCommand(() => SeekRelative(30));
        PreviousTrackCommand = new RelayCommand(PlayPrevious);
        NextTrackCommand = new RelayCommand(PlayNext);

        SetSleepTimerCommand = new RelayCommand<string>(s => { if (int.TryParse(s, out var m)) StartSleepTimer(m); });
        CancelSleepTimerCommand = new RelayCommand(CancelSleepTimer);
        SetEndOfChapterTimerCommand = new RelayCommand(() => { CancelSleepTimer(); SleepTimerEndOfChapter = true; ToastService.Instance.Show(L("MiniPlayer_SleepTimer_ChapterSet"), ToastType.Success, scope: "sleep"); });
        SetPlaybackRateCommand = new RelayCommand<string>(s => { if (float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var r)) PlaybackRate = r; });

        CycleShuffleCommand = new RelayCommand(() =>
        {
            var aiEnabled = _settings.AIFeaturesEnabled;
            ShuffleMode = (ShuffleMode, aiEnabled) switch
            {
                (ShuffleMode.Off, _) => ShuffleMode.Normal,
                (ShuffleMode.Normal, true) => ShuffleMode.Smart,
                (ShuffleMode.Normal, false) => ShuffleMode.Off,
                (ShuffleMode.Smart, _) => ShuffleMode.Off,
                _ => ShuffleMode.Off
            };
            _navigator.IsShuffle = IsShuffle;
            _navigator.IsSmartShuffle = _shuffleMode == ShuffleMode.Smart && aiEnabled;
            _library.ClearAutoQueue();
            RefillAutoQueue();
        });

        CycleRepeatCommand = new RelayCommand(() =>
        {
            _navigator.CycleRepeat();
            OnPropertyChanged(nameof(RepeatMode)); OnPropertyChanged(nameof(RepeatIconKind)); OnPropertyChanged(nameof(RepeatTooltip));
            OnPropertyChanged(nameof(IsRepeat)); OnPropertyChanged(nameof(RepeatForeground));
            _library.ClearAutoQueue();
            RefillAutoQueue();
        });

        ToggleMuteCommand = new RelayCommand(() =>
        {
            if (_isMuted) { _isMuted = false; Volume = _volumeBeforeMute; }
            else { _volumeBeforeMute = _volume > 0 ? _volume : 0.8f; _isMuted = true; Volume = 0; }
        });

        ToggleCurrentFavoriteCommand = new RelayCommand(() =>
        {
            if (_currentTrack == null) return;
            _library.ToggleFavorite(_currentTrack.Id);
            OnPropertyChanged(nameof(IsCurrentFavorite));
        });
    }

    public void UpdateSkipPenaltyCap(int cap) => _navigator.SkipPenaltyCap = cap;

    private void SeekRelative(int seconds)
    {
        var duration = _playback.Duration.TotalSeconds;
        if (duration <= 0) return;
        var newPosition = Math.Clamp(Position * duration + seconds, 0, duration);
        Position = (float)(newPosition / duration);
        _playback.Seek(Position);
    }

    public void SeekTo(float position)
    {
        Position = Math.Clamp(position, 0f, 1f);
        _playback.Seek(Position);
    }
}
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using NullWave.Models;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class MiniPlayerView : Border
{
    private bool _isSeeking;
    private CancellationTokenSource? _marqueeCts;
    private MainViewModel? _observedViewModel;
    private PlayerViewModel? _observedPlayer;

    public MiniPlayerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // PHASE-1 SAFE ATTACH: DataContext arrives before Player exists (paint-first
    // startup). We subscribe to MainViewModel immediately, but only subscribe to
    // Player once InitializeAsync publishes it via OnPropertyChanged(nameof(Player)).
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DetachAll();

        if (DataContext is MainViewModel vm)
        {
            _observedViewModel = vm;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            AttachPlayer(vm.Player);   // no-op in Phase 1, real attach in Phase 2
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Player))
        {
            AttachPlayer(_observedViewModel?.Player);
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.CurrentEffectsTier))
            RestartMarquee();
    }

    private void AttachPlayer(PlayerViewModel? player)
    {
        if (ReferenceEquals(_observedPlayer, player)) return;
        DetachPlayer();

        _observedPlayer = player;
        if (_observedPlayer != null)
        {
            _observedPlayer.PropertyChanged += OnPlayerPropertyChanged;
            RestartMarquee();
        }
    }

    private void DetachPlayer()
    {
        if (_observedPlayer != null)
        {
            _observedPlayer.PropertyChanged -= OnPlayerPropertyChanged;
            _observedPlayer = null;
        }
    }

    private void DetachAll()
    {
        DetachPlayer();
        if (_observedViewModel != null)
        {
            _observedViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
            _observedViewModel = null;
        }
    }

    private void OnPlayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.CurrentTrackDisplay))
            RestartMarquee();
    }

    private void OnSeekPressed(object? sender, PointerPressedEventArgs e)
    {
        _isSeeking = true;
    }

    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isSeeking) return;
        _isSeeking = false;

        // FIX: WaveSeekBar derives from RangeBase, not Slider - cast accordingly.
        // Null-guard: a drag released during the pre-init second must not NRE.
        if (sender is RangeBase bar && DataContext is MainViewModel vm && vm.Player != null)
            vm.Player.SeekTo((float)bar.Value);
    }

    /// <summary>
    /// Handles the edge case where the pointer is captured during a drag but then
    /// leaves the control before release (e.g. dragged off the mini-player).
    /// Without this, _isSeeking stays true forever and subsequent position updates
    /// are ignored until the user clicks the bar again.
    /// </summary>
    private void OnSeekCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_isSeeking) return;
        _isSeeking = false;

        if (sender is RangeBase bar && DataContext is MainViewModel vm && vm.Player != null)
            vm.Player.SeekTo((float)bar.Value);
    }

    private async void RestartMarquee()
    {
        _marqueeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _marqueeCts = cts;

        try
        {
            TitleTextBlock.RenderTransform = new TranslateTransform(0, 0);

            // Let layout settle after the text change before measuring widths.
            await Task.Delay(50, cts.Token);

            var overflow = TitleTextBlock.Bounds.Width - TitleClip.Bounds.Width;
            if (overflow <= 4) return; // fits fine, no scrolling needed
            if (_observedViewModel != null && _observedViewModel.CurrentEffectsTier == EffectsTier.Minimal) return;

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1500, cts.Token); // pause at start

                TitleTextBlock.RenderTransform = new TranslateTransform(0, 0);

                var duration = TimeSpan.FromSeconds(Math.Max(overflow / 30.0, 1.0)); // ~30px/sec
                var animation = new Animation
                {
                    Duration = duration,
                    Easing = new LinearEasing(),
                    Children =
                    {
                        new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.XProperty, 0d) } },
                        new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.XProperty, -overflow) } },
                    }
                };

                await animation.RunAsync(TitleTextBlock, cts.Token);
                await Task.Delay(1500, cts.Token); // pause at end
                TitleTextBlock.RenderTransform = new TranslateTransform(0, 0); // snap back to start
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when a new track arrives mid-scroll - the new call's
            // cts.Cancel() interrupts this one; nothing to clean up.
        }
    }
}
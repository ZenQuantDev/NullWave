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

    public MiniPlayerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observedViewModel != null)
        {
            _observedViewModel.Player.PropertyChanged -= OnPlayerPropertyChanged;
            _observedViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        }

        if (DataContext is MainViewModel vm)
        {
            _observedViewModel = vm;
            vm.Player.PropertyChanged += OnPlayerPropertyChanged;
            vm.PropertyChanged += OnMainViewModelPropertyChanged;
            RestartMarquee();
        }
        else
        {
            _observedViewModel = null;
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentEffectsTier))
            RestartMarquee();
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
        if (sender is RangeBase bar && DataContext is MainViewModel vm)
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

        if (sender is RangeBase bar && DataContext is MainViewModel vm)
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
            if (DataContext is MainViewModel vm && vm.CurrentEffectsTier == EffectsTier.Minimal) return;

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
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class MiniPlayerView : Border
{
    private bool _isSeeking;
    private CancellationTokenSource? _marqueeCts;

    public MiniPlayerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.Player.PropertyChanged += OnPlayerPropertyChanged;
            RestartMarquee();
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

        if (sender is Slider slider && DataContext is MainViewModel vm)
            vm.Player.SeekTo((float)slider.Value);
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
            // Expected when a new track arrives mid-scroll — the new call's
            // cts.Cancel() interrupts this one; nothing to clean up.
        }
    }
}
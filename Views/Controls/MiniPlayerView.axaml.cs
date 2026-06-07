using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using NullWave.ViewModels;

namespace NullWave.Views.Controls;

public partial class MiniPlayerView : Border
{
    private bool _isSeeking;

    public MiniPlayerView()
    {
        InitializeComponent();
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
}
using Avalonia.Controls;
using Avalonia.Input;
using NullWave.ViewModels;

namespace NullWave.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // Alt — toggle menu bar
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
        {
            vm.ToggleMenuBar();
            e.Handled = true;
            return;
        }

        // Don't fire shortcuts when typing in a TextBox
        if (e.Source is TextBox) return;

        switch (e.Key)
        {
            case Key.Space:
                vm.Player.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Left:
                vm.Player.SeekBackwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Right:
                vm.Player.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.M:
                vm.Player.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.N:
                vm.Player.NextTrackCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.P:
                vm.Player.PreviousTrackCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
using Avalonia.Controls;
using Avalonia.Input;
using NullWave.ViewModels;

namespace NullWave.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        
        // ✅ Set the DataContext so all bindings work
        DataContext = new MainViewModel();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.LeftAlt || e.Key == Key.RightAlt)
        {
            if (DataContext is MainViewModel vm)
                vm.ToggleMenuBar();
            e.Handled = true;
        }
    }
}
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NullWave.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() { InitializeComponent(); }

    public ConfirmDialog(string title, string message)
    {
        InitializeComponent();
        Title = $"NullWave — {title}";
        MessageText.Text = message;
    }

    private void OnYes(object? sender, RoutedEventArgs e) => Close(true);
    private void OnNo(object? sender, RoutedEventArgs e) => Close(false);
}
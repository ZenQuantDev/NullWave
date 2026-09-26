using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NullWave.Views;

public partial class WhatsNewWindow : Window
{
    // Required by the Avalonia XAML runtime loader
    public WhatsNewWindow() : this(string.Empty)
    {
    }

    public WhatsNewWindow(string version)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(version))
        {
            Title = $"What's New in v{version}";
            TitleText.Text = $"What's New in v{version}";
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
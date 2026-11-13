using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Services;

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
            var title = string.Format(LocalizationService.Instance["WhatsNew_Title_Version"], version);
            Title = title;
            TitleText.Text = title;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
using Avalonia.Controls;
using Avalonia.Interactivity;
using NullWave.Helpers;

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
            Title = $"What's New in v{version}";

        foreach (var block in MarkdownLite.Render(ChangelogParser.GetLatestReleaseNotes()))
            NotesPanel.Children.Add(block);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
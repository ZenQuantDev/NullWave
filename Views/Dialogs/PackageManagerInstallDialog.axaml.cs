using Avalonia.Controls;
using Avalonia.Interactivity;

namespace NullWave.Views.Dialogs;

public sealed record PackageInstallConsentChoice(bool Approved, bool RememberChoice);

public partial class PackageManagerInstallDialog : Window
{
    public PackageManagerInstallDialog() => InitializeComponent();

    public PackageManagerInstallDialog(string command)
    {
        InitializeComponent();
        CommandText.Text = command;
    }

    private void OnApprove(object? sender, RoutedEventArgs e)
        => Close(new PackageInstallConsentChoice(true, RememberChoiceCheckBox.IsChecked == true));

    private void OnDecline(object? sender, RoutedEventArgs e)
        => Close(new PackageInstallConsentChoice(false, RememberChoiceCheckBox.IsChecked == true));
}

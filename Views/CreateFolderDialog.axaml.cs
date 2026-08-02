using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace NullWave.Views;

public partial class CreateFolderDialog : Window
{
    public CreateFolderDialog()
    {
        InitializeComponent();
        NameInput.AttachedToVisualTree += (_, _) => NameInput.Focus();
    }

    private void OnCreate(object? sender, RoutedEventArgs e) => CloseWithResult();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnNameInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CloseWithResult();
        else if (e.Key == Key.Escape) Close(null);
    }

    private void CloseWithResult()
    {
        var name = NameInput.Text?.Trim();
        Close(string.IsNullOrWhiteSpace(name) ? null : name);
    }
}

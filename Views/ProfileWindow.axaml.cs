using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NullWave.ViewModels;
using Serilog;

namespace NullWave.Views;

public partial class ProfileWindow : Window
{
    public ProfileWindow()
    {
        InitializeComponent();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
        {
            ExportProfileCard();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnExportProfileClicked(object? sender, RoutedEventArgs e)
    {
        ExportProfileCard();
    }

    private async void ExportProfileCard()
    {
        if (DataContext is not UserProfileViewModel vm) return;

        try
        {
            var topLevel = GetTopLevel(this);
            if (topLevel == null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Profile Card",
                SuggestedFileName = $"{vm.Username}_ProfileCard.png",
                DefaultExtension = "png",
                FileTypeChoices = new[] { new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } } }
            });

            if (file == null) return;

            var pixelSize = new PixelSize((int)Width, (int)Height);
            using var bitmap = new RenderTargetBitmap(pixelSize);
            bitmap.Render(ProfileCard);

            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream);

            vm.TriggerExportSuccessToast();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export profile card PNG.");
        }
    }
}
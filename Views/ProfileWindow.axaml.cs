using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using NullWave.ViewModels;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace NullWave.Views;

public partial class ProfileWindow : Window
{
    public ProfileWindow()
    {
        InitializeComponent();
        
        // Close on Escape
        KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };

        // Avatar hover effect - FIXED
        SetupAvatarHover();
    }

    private void SetupAvatarHover()
    {
        var avatarButton = this.FindControl<Button>("AvatarHoverButton");
        if (avatarButton != null)
        {
            avatarButton.PointerEntered += (s, e) => 
            {
                avatarButton.Opacity = 1;
            };
            
            avatarButton.PointerExited += (s, e) => 
            {
                avatarButton.Opacity = 0;
            };
        }
    }

    /// <summary>
    /// Renders the window content to a PNG bitmap (for profile card export).
    /// </summary>
    public async Task<string?> ExportProfileCardAsync()
    {
        try
        {
            var storage = StorageProvider;
            if (!storage.CanSave) return null;

            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export Profile Card",
                DefaultExtension = "png",
                SuggestedFileName = $"nullwave-profile-{DateTime.Now:yyyy-MM-dd}.png",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                }
            });

            if (file == null) return null;

            // Render the window to a bitmap
            var pixelSize = new PixelSize((int)(Width * 2), (int)(Height * 2));
            var dpi = new Vector(192, 192);

            using var bitmap = new RenderTargetBitmap(pixelSize, dpi);
            bitmap.Render(Content as Visual ?? this);

            // Save to file
            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream);

            Log.Information("[ProfileWindow] Profile card exported to {Path}", file.Path.LocalPath);
            return file.Path.LocalPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProfileWindow] Export failed");
            return null;
        }
    }
}
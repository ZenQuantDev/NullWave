using System.ComponentModel;
using Avalonia.Controls;
using NullWave.Services;

namespace NullWave.Helpers;

/// <summary>
/// Activates translucent chrome only when a wallpaper can actually be rendered.
/// Dialogs, onboarding windows, and toast surfaces intentionally remain opaque for focus and contrast.
/// </summary>
public static class WallpaperChromeSync
{
    public static void Attach(Window window)
    {
        var service = WallpaperService.Instance;
        void Sync() => window.Classes.Set("wallpaper-active", service.HasActiveWallpaper);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName == nameof(WallpaperService.HasActiveWallpaper)) Sync();
        };
        service.PropertyChanged += handler;
        window.Closed += (_, _) => service.PropertyChanged -= handler;
        Sync();
    }
}
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;

namespace NullWave.ViewModels;

public partial class SettingsViewModel
{
    public string WallpaperStyle
    {
        get => _prefsService.Current.WallpaperStyle;
        set { _prefsService.Update(p => p.WallpaperStyle = value); OnPropertyChanged(); OnPropertyChanged(nameof(WallpaperStatusLabel)); OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning)); ScheduleSave(); WallpaperService.Instance.ApplyFrom(_prefsService.Current); }
    }
    public string WallpaperFit
    {
        get => _prefsService.Current.WallpaperFit;
        set { _prefsService.Update(p => p.WallpaperFit = value); OnPropertyChanged(); ScheduleSave(); WallpaperService.Instance.ApplyFrom(_prefsService.Current); }
    }
    public int WallpaperOpacity
    {
        get => _prefsService.Current.WallpaperOpacity;
        set { _prefsService.Update(p => p.WallpaperOpacity = value); OnPropertyChanged(); WallpaperService.Instance.ApplyFrom(_prefsService.Current); ScheduleSave(); }
    }
    public int WallpaperBlur
    {
        get => _prefsService.Current.WallpaperBlur;
        set { _prefsService.Update(p => p.WallpaperBlur = value); OnPropertyChanged(); OnPropertyChanged(nameof(WallpaperSoftness)); WallpaperService.Instance.ApplyFrom(_prefsService.Current); ScheduleSave(); }
    }
    public int WallpaperSoftness
    {
        get => WallpaperGuard.SoftnessForBlur(WallpaperBlur);
        set => WallpaperBlur = WallpaperGuard.BlurForSoftness(value);
    }
    public string WallpaperPath => _prefsService.Current.WallpaperPath;
    public string WallpaperSceneId
    {
        get => _prefsService.Current.WallpaperSceneId;
        set
        {
            _prefsService.Update(p => p.WallpaperSceneId = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning));
            WallpaperService.Instance.ApplyFrom(_prefsService.Current);
            ScheduleSave();
        }
    }

    public string WallpaperStatusLabel => WallpaperStyle == "None"
        ? L("Settings_Appearance_Wallpaper_Off")
        : string.Format(L("Settings_Appearance_Wallpaper_ActiveFmt"), WallpaperStyle);

    public bool ShowWallpaperTrueBlackWarning => WallpaperGuard.TrueBlackWarning(
        ThemeMode,
        WallpaperStyle,
        WallpaperScenes.Find(WallpaperSceneId)?.OledSafe == true);

    [RelayCommand] private void SetWallpaperStyle(string style) => WallpaperStyle = style;
    [RelayCommand] private void SetWallpaperFit(string fit) => WallpaperFit = fit;

    [RelayCommand]
    private void SelectWallpaperScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) return;
        
        // Atomically switch to Scene mode and set the ID
        _prefsService.Update(p =>
        {
            p.WallpaperStyle = "Scene";
            p.WallpaperSceneId = sceneId;
        });
        
        OnPropertyChanged(nameof(WallpaperStyle));
        OnPropertyChanged(nameof(WallpaperSceneId));
        OnPropertyChanged(nameof(WallpaperStatusLabel));
        OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning));
        ScheduleSave();
        WallpaperService.Instance.ApplyFrom(_prefsService.Current);
    }

    [RelayCommand]
    private async Task SelectWallpaperAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a background image",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } }
        });
        if (files.Count == 0) return;

        // Copy into the managed art cache so a deleted source file cannot break the background.
        var src = files[0];
        var ext = Path.GetExtension(src.Name);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var dest = WallpaperService.ReserveCachePath(ext);
        try
        {
            await using var inStream = await src.OpenReadAsync();
            await using var outStream = File.Create(dest);
            await inStream.CopyToAsync(outStream);
        }
        catch (Exception ex)
        {
            try { File.Delete(dest); } catch { }
            ToastService.Instance.Show($"Couldn't copy background image: {ex.Message}", ToastType.Error);
            return;
        }

        _prefsService.Update(p => { p.WallpaperPath = dest; p.WallpaperStyle = "Custom"; });
        if (_prefsService.Save())
            WallpaperService.PruneWallpaperCache(dest);
        OnPropertyChanged(nameof(WallpaperPath));
        OnPropertyChanged(nameof(WallpaperStyle));
        OnPropertyChanged(nameof(WallpaperStatusLabel));
        WallpaperService.Instance.ApplyFrom(_prefsService.Current);
        ToastService.Instance.Show(L("Settings_Appearance_Wallpaper_Set"), ToastType.Success);
    }
}
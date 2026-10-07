using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using Serilog;

namespace NullWave.ViewModels;

/// <summary>One card in the unified Background Scenes gallery.
/// AssetPath == null means "procedural preview keyed by Id".</summary>
public record WallpaperGalleryItem(string Id, string Name, string Description, string? AssetPath, bool OledSafe);

public partial class SettingsViewModel
{
    public string WallpaperStyle
    {
        get => _prefsService.Current.WallpaperStyle;
        set { _prefsService.Update(p => p.WallpaperStyle = value); OnPropertyChanged(); OnPropertyChanged(nameof(WallpaperSelectionKey)); OnPropertyChanged(nameof(WallpaperStatusLabel)); OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning)); ScheduleSave(); WallpaperService.Instance.ApplyFrom(_prefsService.Current); }
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
            OnPropertyChanged(nameof(WallpaperSelectionKey));
            OnPropertyChanged(nameof(WallpaperStatusLabel));
            OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning));
            WallpaperService.Instance.ApplyFrom(_prefsService.Current);
            ScheduleSave();
        }
    }
    public string WallpaperBuiltInId => _prefsService.Current.WallpaperBuiltInId;
    public string WallpaperSelectionKey =>
        WallpaperGuard.SelectionKey(WallpaperStyle, WallpaperSceneId, WallpaperBuiltInId);
    public IReadOnlyList<WallpaperGalleryItem> GalleryItems => BuildGalleryItems();
    public bool IsExclusiveWallpaperUnlocked =>
        WallpaperBuiltIns.IsUnlocked(WallpaperBuiltIns.StarryNight, _prefsService.Current.UnlockedExclusiveWallpapers);

    private IReadOnlyList<WallpaperGalleryItem> BuildGalleryItems()
    {
        var loc = LocalizationService.Instance;
        var items = new List<WallpaperGalleryItem>(WallpaperScenes.All.Count + WallpaperBuiltIns.All.Count);
        foreach (var scene in WallpaperScenes.All)
            items.Add(new WallpaperGalleryItem(scene.Id, loc[scene.NameKey], loc[scene.DescriptionKey], null, scene.OledSafe));
        foreach (var builtIn in WallpaperBuiltIns.Visible(_prefsService.Current.UnlockedExclusiveWallpapers))
            items.Add(new WallpaperGalleryItem(builtIn.Id, loc[builtIn.NameKey], loc[builtIn.DescriptionKey], builtIn.AssetPath, builtIn.OledSafe));
        return items;
    }

    public string WallpaperStatusLabel
    {
        get
        {
            var status = WallpaperStatus.Describe(
                WallpaperStyle,
                WallpaperScenes.Find(WallpaperSceneId)?.Name,
                WallpaperBuiltIns.Find(WallpaperBuiltInId)?.Name);
            return status.Arg == null ? L(status.FormatKey) : string.Format(L(status.FormatKey), status.Arg);
        }
    }

    private bool ActiveWallpaperOledSafe => WallpaperStyle switch
    {
        "Scene" => WallpaperScenes.Find(WallpaperSceneId)?.OledSafe == true,
        "BuiltIn" => WallpaperBuiltIns.Find(WallpaperBuiltInId)?.OledSafe == true,
        _ => false
    };

    public bool ShowWallpaperTrueBlackWarning => WallpaperGuard.TrueBlackWarning(
        ThemeMode,
        WallpaperStyle,
        ActiveWallpaperOledSafe);

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
        OnPropertyChanged(nameof(WallpaperSelectionKey));
        OnPropertyChanged(nameof(WallpaperStatusLabel));
        OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning));
        ScheduleSave();
        WallpaperService.Instance.ApplyFrom(_prefsService.Current);
    }

    [RelayCommand]
    private void SelectWallpaperBuiltIn(string id)
    {
        var definition = WallpaperBuiltIns.Find(id);
        if (definition == null) return;
        if (!WallpaperBuiltIns.IsUnlocked(definition, _prefsService.Current.UnlockedExclusiveWallpapers))
        {
            ToastService.Instance.Show(L("Settings_Appearance_BuiltIn_Locked"), ToastType.Info, durationMs: 6000);
            return;
        }

        _prefsService.Update(p =>
        {
            p.WallpaperStyle = "BuiltIn";
            p.WallpaperPath = string.Empty;
            p.WallpaperBuiltInId = definition.Id;
        });
        OnPropertyChanged(nameof(WallpaperStyle));
        OnPropertyChanged(nameof(WallpaperPath));
        OnPropertyChanged(nameof(WallpaperBuiltInId));
        OnPropertyChanged(nameof(WallpaperSelectionKey));
        OnPropertyChanged(nameof(WallpaperStatusLabel));
        OnPropertyChanged(nameof(ShowWallpaperTrueBlackWarning));
        ScheduleSave();
        WallpaperService.Instance.ApplyFrom(_prefsService.Current);
    }

    [RelayCommand]
    private void SelectWallpaperGallery(string id)
    {
        if (WallpaperScenes.Find(id) != null) { SelectWallpaperScene(id); return; }
        if (WallpaperBuiltIns.Find(id) != null) { SelectWallpaperBuiltIn(id); return; }
    }

    public void UnlockExclusiveWallpaper()
    {
        if (IsExclusiveWallpaperUnlocked)
        {
            ToastService.Instance.Show(L("Settings_Appearance_BuiltIn_UnlockToast"), ToastType.Info);
            return;
        }

        _prefsService.Update(p => p.UnlockedExclusiveWallpapers.Add(WallpaperBuiltIns.StarryNight.Id));
        OnPropertyChanged(nameof(IsExclusiveWallpaperUnlocked));
        OnPropertyChanged(nameof(GalleryItems));
        ScheduleSave();
        ToastService.Instance.Show(L("Settings_Appearance_BuiltIn_UnlockToast"), ToastType.Success, durationMs: 8000);
    }

    [RelayCommand]
    private async Task SelectWallpaperAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Settings_Appearance_Wallpaper_Browse"),
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" } } }
        });
        if (files.Count == 0) return;

        try
        {
            await using var source = await files[0].OpenReadAsync();
            var result = await WallpaperImport.ImportAsync(
                source,
                WallpaperService.ReserveCachePath,
                stream =>
                {
                    using var bitmap = Bitmap.DecodeToWidth(stream, 64);
                    return bitmap.PixelSize.Width > 0 && bitmap.PixelSize.Height > 0;
                });

            if (!result.Ok || result.Path == null)
            {
                var errorKey = result.Status switch
                {
                    WallpaperImportStatus.TooLarge => "Settings_Appearance_Wallpaper_Error_TooLarge",
                    WallpaperImportStatus.UnsupportedFormat => "Settings_Appearance_Wallpaper_Error_Format",
                    WallpaperImportStatus.DecodeFailed => "Settings_Appearance_Wallpaper_Error_Decode",
                    _ => "Settings_Appearance_Wallpaper_Error_Unknown"
                };
                ToastService.Instance.Show(L(errorKey), ToastType.Error);
                return;
            }

            _prefsService.Update(p => { p.WallpaperPath = result.Path; p.WallpaperStyle = "Custom"; });
            if (_prefsService.Save())
                WallpaperService.PruneWallpaperCache(result.Path);
            OnPropertyChanged(nameof(WallpaperPath));
            OnPropertyChanged(nameof(WallpaperStyle));
            OnPropertyChanged(nameof(WallpaperSelectionKey));
            OnPropertyChanged(nameof(WallpaperStatusLabel));
            WallpaperService.Instance.ApplyFrom(_prefsService.Current);
            ToastService.Instance.Show(L("Settings_Appearance_Wallpaper_Set"), ToastType.Success);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Settings] Wallpaper picker failed");
            ToastService.Instance.Show(L("Settings_Appearance_Wallpaper_Error_Unknown"), ToastType.Error);
        }
    }
}
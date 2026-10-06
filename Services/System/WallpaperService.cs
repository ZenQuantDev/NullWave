using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Singleton state for the MainWindow background layer, mirroring the ThemeService
/// pattern: prefs are the source of truth, views bind via x:Static.
/// Renders None / AccentGlow / Custom-image with decode-baked blur. AlbumArt sync
/// remains deferred until player coupling, crossfade, and OLED/tier gates exist.
/// </summary>
public partial class WallpaperService : ObservableObject
{
    public static WallpaperService Instance { get; } = new();

    // Cap decode width so a 4K photo doesn't melt the i3 380M floor machine.
    private const int MaxDecodeWidth = 1920;

    [ObservableProperty] private string _style = "None";
    [ObservableProperty] private string _path = string.Empty;
    [ObservableProperty] private int _opacity = 40;
    [ObservableProperty] private int _blur;
    [ObservableProperty] private string _fit = "Fill";
    [ObservableProperty] private string _sceneId = WallpaperScenes.DefaultId;
    [ObservableProperty] private Bitmap? _image;
    private CancellationTokenSource? _reloadCts;
    private int _decodeGeneration;
    private bool _customSourceReady;
    private string? _lastDecodedPath;
    private int _lastDecodedWidth;
    private bool _missingCustomWarningShown;

    public bool ShowGlow => Style == "AccentGlow";
    public bool ShowImage => Style == "Custom" && Image != null;
    public bool ShowScene => Style == "Scene" && WallpaperScenes.Find(SceneId) != null;
    public bool HasActiveWallpaper => ShowGlow || ShowImage || ShowScene || _customSourceReady;
    public bool MinimalTier { get; set; }
    public double OpacityFraction => Opacity / 100.0;
    public Stretch Stretch => Fit switch
    {
        "Fit"     => Stretch.Uniform,
        "Stretch" => Stretch.Fill,
        _         => Stretch.UniformToFill,   // "Fill" = cover
    };

    public void ApplyFrom(Preferences p)
    {
        var normalizedStyle = WallpaperGuard.NormalizeStyle(p.WallpaperStyle, p.WallpaperPath, File.Exists(p.WallpaperPath));
        var scene = WallpaperScenes.Find(p.WallpaperSceneId);
        if (string.Equals(normalizedStyle, "Scene", StringComparison.OrdinalIgnoreCase) && scene == null)
            normalizedStyle = "None";

        p.WallpaperSceneId = scene?.Id ?? WallpaperScenes.DefaultId;
        if (!string.Equals(normalizedStyle, p.WallpaperStyle, StringComparison.Ordinal))
        {
            var missingCustom = string.Equals(p.WallpaperStyle, "Custom", StringComparison.OrdinalIgnoreCase) &&
                                !File.Exists(p.WallpaperPath);
            p.WallpaperStyle = normalizedStyle;
            p.WallpaperPath = string.Empty;
            if (missingCustom && !_missingCustomWarningShown)
            {
                _missingCustomWarningShown = true;
                Log.Warning("[Wallpaper] Custom wallpaper is missing; disabling wallpaper");
                ToastService.Instance.Show(LocalizationService.Instance["Settings_Appearance_Wallpaper_Missing"], ToastType.Warning);
            }
            else
            {
                Log.Debug("[Wallpaper] Unknown wallpaper style or scene normalized to {Style}", normalizedStyle);
            }
        }

        bool sourceChanged = p.WallpaperPath != Path || p.WallpaperStyle != Style;
        bool blurChanged = p.WallpaperBlur != Blur;
        bool sceneChanged = p.WallpaperSceneId != SceneId;
        Opacity = p.WallpaperOpacity;
        Blur = p.WallpaperBlur;
        Fit = p.WallpaperFit;
        if (sceneChanged) SceneId = p.WallpaperSceneId;
        if (sourceChanged)
        {
            _reloadCts?.Cancel();
            Path = p.WallpaperPath;
            Style = p.WallpaperStyle;
            _customSourceReady = Style == "Custom" && !string.IsNullOrEmpty(Path) && File.Exists(Path);
            BeginDecode();
        }
        else if (blurChanged && (ShowImage || _customSourceReady))
        {
            ScheduleReload();
        }
        OnPropertyChanged(nameof(OpacityFraction));
        OnPropertyChanged(nameof(Stretch));
        OnPropertyChanged(nameof(ShowGlow));
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(ShowScene));
        OnPropertyChanged(nameof(HasActiveWallpaper));
    }

    private void ScheduleReload()
    {
        _reloadCts?.Cancel();
        _reloadCts?.Dispose();
        _reloadCts = new CancellationTokenSource();
        var token = _reloadCts.Token;
        _ = DebounceReloadAsync(token);
    }

    private async Task DebounceReloadAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(150, token);
            Dispatcher.UIThread.Post(() =>
            {
                if (!token.IsCancellationRequested) BeginDecode();
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
    }

    internal static int DecodeWidthForBlur(int blur) =>
        blur <= 0 ? MaxDecodeWidth : Math.Max(160, MaxDecodeWidth / (1 + blur / 10));

    private void BeginDecode()
    {
        if (Style != "Custom" || string.IsNullOrEmpty(Path) || !File.Exists(Path))
        {
            _decodeGeneration++;
            _customSourceReady = false;
            var dropped = Image;
            Image = null;
            NotifyComputed();
            DisposeLater(dropped);
            return;
        }

        var generation = ++_decodeGeneration;
        var path = Path;
        var width = DecodeWidthForBlur(Blur);
        if (Image != null && WallpaperGuard.ShouldSkipDecode(_lastDecodedPath, _lastDecodedWidth, path, width))
        {
            _customSourceReady = false;
            NotifyComputed();
            return;
        }

        _ = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            Bitmap? decoded = null;
            try
            {
                using var stream = File.OpenRead(path);
                decoded = Bitmap.DecodeToWidth(stream, width);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Wallpaper] Failed to decode {Path}", path);
            }
            stopwatch.Stop();

            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _decodeGeneration)
                {
                    decoded?.Dispose();
                    return;
                }

                if (decoded == null)
                {
                    _customSourceReady = false;
                    NotifyComputed();
                    return;
                }

                var old = Image;
                Image = decoded;
                _lastDecodedPath = path;
                _lastDecodedWidth = width;
                _customSourceReady = false;
                NotifyComputed();
                Log.Debug("[Wallpaper] decoded {Path} at {Width}px in {Ms}ms", path, width, stopwatch.ElapsedMilliseconds);
                DisposeLater(old);
            });
        });
    }

    private void NotifyComputed()
    {
        OnPropertyChanged(nameof(OpacityFraction));
        OnPropertyChanged(nameof(Stretch));
        OnPropertyChanged(nameof(ShowGlow));
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(ShowScene));
        OnPropertyChanged(nameof(HasActiveWallpaper));
    }

    private static void DisposeLater(Bitmap? bitmap)
    {
        if (bitmap != null)
            Dispatcher.UIThread.Post(bitmap.Dispose, DispatcherPriority.Background);
    }

    internal static string ReserveCachePath(string extension)
    {
        var directory = NullWavePaths.ArtCacheDir;
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string candidate;
        do
        {
            candidate = System.IO.Path.Combine(directory, $"wallpaper-{stamp}{extension}");
            stamp++;
        }
        while (File.Exists(candidate));
        return candidate;
    }

    internal static void PruneWallpaperCache(string keepPath)
    {
        try
        {
            foreach (var file in Directory.GetFiles(NullWavePaths.ArtCacheDir, "wallpaper*.*"))
            {
                if (string.Equals(file, keepPath, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Wallpaper] cache prune skipped");
        }
    }
}
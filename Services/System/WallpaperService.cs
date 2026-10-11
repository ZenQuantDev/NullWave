using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

/// <summary>
/// Singleton state for the MainWindow background layer, mirroring the ThemeService
/// pattern: prefs are the source of truth, views bind via x:Static.
/// Renders None / AccentGlow / Scene / BuiltIn / Custom with decode-baked blur.
/// Built-in assets use the same generation-guarded decode path as Custom files.
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
    [ObservableProperty] private string _builtInId = string.Empty;
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private bool _minimalTier;
    private CancellationTokenSource? _reloadCts;
    private int _decodeGeneration;
    private bool _customSourceReady;
    private bool _builtInSourceReady;
    private string? _lastDecodedPath;
    private int _lastDecodedWidth;
    private bool _missingCustomWarningShown;

    public bool ShowGlow => Style == "AccentGlow";
    public bool ShowImage => (Style == "Custom" || Style == "BuiltIn") && Image != null;
    public bool ShowScene => Style == "Scene" && WallpaperScenes.Find(SceneId) != null;
    public bool HasActiveWallpaper => ShowGlow || ShowImage || ShowScene || _customSourceReady || _builtInSourceReady;
    public bool ShowFullScenes => !MinimalTier;
    public double OpacityFraction => Opacity / 100.0;

    partial void OnMinimalTierChanged(bool value) => OnPropertyChanged(nameof(ShowFullScenes));
    public Stretch Stretch => Fit switch
    {
        "Fit"     => Stretch.Uniform,
        "Stretch" => Stretch.Fill,
        _         => Stretch.UniformToFill,   // "Fill" = cover
    };

    public void ApplyFrom(Preferences p)
    {
        p.UnlockedExclusiveWallpapers ??= new();
        var normalizedStyle = WallpaperGuard.NormalizeStyle(p.WallpaperStyle, p.WallpaperPath, File.Exists(p.WallpaperPath));
        var scene = WallpaperScenes.Find(p.WallpaperSceneId);
        if (string.Equals(normalizedStyle, "Scene", StringComparison.OrdinalIgnoreCase) && scene == null)
            normalizedStyle = "None";

        p.WallpaperSceneId = scene?.Id ?? WallpaperScenes.DefaultId;
        var builtIn = WallpaperBuiltIns.Find(p.WallpaperBuiltInId);
        if (string.Equals(normalizedStyle, "BuiltIn", StringComparison.OrdinalIgnoreCase) &&
            (builtIn == null || !WallpaperBuiltIns.IsUnlocked(builtIn, p.UnlockedExclusiveWallpapers)))
            normalizedStyle = "None";
        if (string.Equals(normalizedStyle, "BuiltIn", StringComparison.OrdinalIgnoreCase))
            p.WallpaperPath = string.Empty;
        p.WallpaperBuiltInId = builtIn?.Id ?? string.Empty;
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

        bool sourceChanged = p.WallpaperPath != Path || p.WallpaperStyle != Style || p.WallpaperBuiltInId != BuiltInId;
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
            BuiltInId = p.WallpaperBuiltInId;
            _customSourceReady = Style == "Custom" && !string.IsNullOrEmpty(Path) && File.Exists(Path);
            _builtInSourceReady = Style == "BuiltIn" && WallpaperBuiltIns.Find(BuiltInId) != null;
            BeginDecode();
        }
        else if (blurChanged && (ShowImage || _customSourceReady || _builtInSourceReady))
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

    private string? ResolveSourceKey()
    {
        if (Style == "Custom" && !string.IsNullOrEmpty(Path) && File.Exists(Path)) return Path;
        if (Style == "BuiltIn") return WallpaperBuiltIns.Find(BuiltInId)?.AssetPath;
        return null;
    }

    private static Stream OpenSource(string key) =>
        key.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)
            ? AssetLoader.Open(new Uri(key))
            : File.OpenRead(key);

    private void BeginDecode()
    {
        var sourceKey = ResolveSourceKey();
        if (sourceKey == null)
        {
            _decodeGeneration++;
            _customSourceReady = false;
            _builtInSourceReady = false;
            var dropped = Image;
            Image = null;
            NotifyComputed();
            DisposeLater(dropped);
            return;
        }

        var generation = ++_decodeGeneration;
        var key = sourceKey;
        var width = DecodeWidthForBlur(Blur);
        if (Image != null && WallpaperGuard.ShouldSkipDecode(_lastDecodedPath, _lastDecodedWidth, key, width))
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
                using var stream = OpenSource(key);
                decoded = Bitmap.DecodeToWidth(stream, width);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Wallpaper] Failed to decode {Path}", key);
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
                    _builtInSourceReady = false;
                    NotifyComputed();
                    return;
                }

                var old = Image;
                Image = decoded;
                _lastDecodedPath = key;
                _lastDecodedWidth = width;
                _customSourceReady = false;
                _builtInSourceReady = false;
                NotifyComputed();
                Log.Debug("[Wallpaper] decoded {Path} at {Width}px in {Ms}ms", key, width, stopwatch.ElapsedMilliseconds);
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
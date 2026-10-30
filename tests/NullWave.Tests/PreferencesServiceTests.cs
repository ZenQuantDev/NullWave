using System;
using System.IO;
using System.Linq;
using NullWave.Helpers;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

[Collection("Database")]   // Ensures it uses the safe temp data folder
public class PreferencesServiceTests
{
    private readonly string _testPrefsPath;

    public PreferencesServiceTests()
    {
        if (!NullWavePaths.DataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run: data folder is not a temp folder.");
            
        Directory.CreateDirectory(NullWavePaths.DataDir);
        
        // FIX: Use a unique file for each test to prevent race conditions with 
        // PreferencesService.Shared or other parallel tests triggering debounced saves
        _testPrefsPath = Path.Combine(NullWavePaths.DataDir, $"prefs_test_{Guid.NewGuid():N}.json");
    }

    [Fact]
    public void A_change_made_just_before_shutdown_is_saved()
    {
        using (var first = new PreferencesService(_testPrefsPath))
            first.Update(p => p.DownloadDirectory = "Test_Dir_Shutdown");     // Dispose() must save it
            
        using var second = new PreferencesService(_testPrefsPath);
        Assert.Equal("Test_Dir_Shutdown", second.Current.DownloadDirectory);
    }

    [Fact]
    public void A_corrupt_file_is_kept_aside_and_defaults_are_used()
    {
        File.WriteAllText(_testPrefsPath, "{ this is not json");
        using var service = new PreferencesService(_testPrefsPath);
        
        Assert.False(string.IsNullOrEmpty(service.Current.DownloadDirectory));   // defaults were applied
        Assert.Single(Directory.GetFiles(NullWavePaths.DataDir, Path.GetFileName(_testPrefsPath) + ".bad-*"));
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        using (var service = new PreferencesService(_testPrefsPath))
            service.Update(p => p.DownloadDirectory = "X");
            
        Assert.Empty(Directory.GetFiles(NullWavePaths.DataDir, Path.GetFileName(_testPrefsPath) + ".tmp"));
    }

    [Fact]
    public void Wallpaper_preferences_round_trip()
    {
        using (var first = new PreferencesService(_testPrefsPath))
        {
            first.Update(p =>
            {
                p.WallpaperStyle = "Custom";
                p.WallpaperPath = "wallpaper-test.png";
                p.WallpaperOpacity = 73;
                p.WallpaperBlur = 12;
                p.WallpaperFit = "Fit";
            });
        }

        using var second = new PreferencesService(_testPrefsPath);
        Assert.Equal("Custom", second.Current.WallpaperStyle);
        Assert.Equal("wallpaper-test.png", second.Current.WallpaperPath);
        Assert.Equal(73, second.Current.WallpaperOpacity);
        Assert.Equal(12, second.Current.WallpaperBlur);
        Assert.Equal("Fit", second.Current.WallpaperFit);
    }

    [Fact]
    public void Shared_instance_is_a_singleton()
        => Assert.Same(PreferencesService.Shared, PreferencesService.Shared);
}
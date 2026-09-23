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
    private static string PrefsPath => Path.Combine(NullWavePaths.DataDir, "prefs.json");

    public PreferencesServiceTests()
    {
        if (!NullWavePaths.DataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to run: data folder is not a temp folder.");
            
        Directory.CreateDirectory(NullWavePaths.DataDir);
        foreach (var file in Directory.GetFiles(NullWavePaths.DataDir, "prefs.json*"))
            File.Delete(file);
    }

    [Fact]
    public void A_change_made_just_before_shutdown_is_saved()
    {
        using (var first = new PreferencesService())
            first.Update(p => p.DownloadDirectory = "Test_Dir_Shutdown");     // Dispose() must save it
            
        using var second = new PreferencesService();
        Assert.Equal("Test_Dir_Shutdown", second.Current.DownloadDirectory);
    }

    [Fact]
    public void A_corrupt_file_is_kept_aside_and_defaults_are_used()
    {
        File.WriteAllText(PrefsPath, "{ this is not json");
        using var service = new PreferencesService();
        
        Assert.False(string.IsNullOrEmpty(service.Current.DownloadDirectory));   // defaults were applied
        Assert.Single(Directory.GetFiles(NullWavePaths.DataDir, "prefs.json.bad-*"));
    }

    [Fact]
    public void Saving_leaves_no_temp_file_behind()
    {
        using (var service = new PreferencesService())
            service.Update(p => p.DownloadDirectory = "X");
            
        Assert.Empty(Directory.GetFiles(NullWavePaths.DataDir, "prefs.json.tmp"));
    }
}
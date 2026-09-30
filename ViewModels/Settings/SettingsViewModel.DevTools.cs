using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services;
using NullWave.Views;
using Serilog;

namespace NullWave.ViewModels;

/// <summary>One row in the DevTools binary health grid.</summary>
public sealed record DevBinaryInfo(string Name, string Status, bool IsOk);

public partial class SettingsViewModel
{
    /// <summary>
    /// Gates the Developer Tab. Unlocked via NULLWAVE_DEV=1 env var, --dev CLI arg,
    /// or (in the future) a verified P-256 Developer badge.
    /// </summary>
    public bool IsDevMode { get; }

    [ObservableProperty]
    private ObservableCollection<DevBinaryInfo> _devBinaries = new();

    [ObservableProperty]
    private string _devProbeResult = "Not run yet.";

    // Routed through MainViewModel (same pattern as SweepOrphanedFilesRequested etc.)
    public event Action<int>? SeedLibraryRequested;
    public event Action? RemoveSeededRequested;
    
    // DevTools performance toggles
    public event Action<bool>? HoverTaggingChanged;
    public event Action<bool>? StormDetectorChanged;
    public event Action? TogglePerfOverlayRequested;
    public event Action? RefetchYouTubeThumbsRequested;

    private static bool CheckDevAccess()
    {
        if (Environment.GetEnvironmentVariable("NULLWAVE_DEV") == "1") return true;
        if (Environment.GetCommandLineArgs().Any(a => a == "--dev")) return true;
        return false;
    }

    #region DevTools Commands

    [RelayCommand]
    private void DevTriggerToast(string type)
    {
        switch (type)
        {
            case "Success": ToastService.Instance.Show("Dev: Success toast fired.", ToastType.Success); break;
            case "Error": ToastService.Instance.Show("Dev: Error toast fired.", ToastType.Error); break;
            case "Warning": ToastService.Instance.Show("Dev: Warning toast fired.", ToastType.Warning); break;
            case "Info": ToastService.Instance.Show("Dev: Info toast fired.", ToastType.Info); break;
        }
    }

    [RelayCommand]
    private void DevFireHighPriorityToast()
    {
        ToastService.Instance.Show("Dev: HIGH priority - must never be evicted.", ToastType.Error,
            priority: ToastPriority.High);
    }

    [RelayCommand]
    private void DevFireToastStorm()
    {
        for (int i = 1; i <= 6; i++)
            ToastService.Instance.Show($"Dev storm toast {i}/6 - extras queue, never drop.", ToastType.Info, durationMs: 3000);
    }

    [RelayCommand]
    private async Task DevTestLiveActivityAsync()
    {
        var activity = ToastService.Instance.StartLiveActivity("Dev Live Activity", "Simulating work...", scope: "dev-live");
        for (int i = 0; i <= 100; i += 20)
        {
            await Task.Delay(400);
            ToastService.Instance.UpdateLiveActivity(activity, $"Processing... {i}%", i,
                isIndeterminate: i == 100 ? false : (bool?)null);
        }
        ToastService.Instance.CompleteLiveActivity(activity, "Dev live activity completed.", lingerMs: 2500);
    }

    [RelayCommand]
    private void DevResetWhatsNew()
    {
        _prefsService.Update(p => p.LastSeenVersion = string.Empty);
        _prefsService.Save();
        ToastService.Instance.Show("What's New reset - popup will show on next launch.", ToastType.Success);
    }

    /// <summary>Probes every external binary via ProcessRunner (3s cap each) + the Ollama HTTP endpoint.</summary>
    [RelayCommand]
    private async Task DevRefreshBinariesAsync()
    {
        var rows = new List<DevBinaryInfo>();

        foreach (var tool in new[] { "yt-dlp", "aria2c", "ffmpeg", "vlc" })
        {
            var result = await ProcessRunner.RunAsync(tool, "--version", TimeSpan.FromSeconds(3));
            var firstLine = result.StandardOutput
                .Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
            bool ok = result.ExitCode == 0 && !string.IsNullOrWhiteSpace(firstLine);
            var status = ok ? firstLine! : result.TimedOut ? "timed out" : "not found on PATH";
            rows.Add(new DevBinaryInfo(tool, status, ok));
        }

        bool ollamaOk = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var resp = await http.GetAsync("http://localhost:11434/api/version");
            ollamaOk = resp.IsSuccessStatusCode;
        }
        catch { /* refused / unreachable */ }
        rows.Add(new DevBinaryInfo("ollama", ollamaOk ? "reachable @ localhost:11434" : "connection refused @ :11434", ollamaOk));

        DevBinaries = new ObservableCollection<DevBinaryInfo>(rows);
    }

    [RelayCommand]
    private async Task DevRelaunchOnboarding()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            _prefsService.Update(p => p.HasCompletedOnboarding = false);
            await new OnboardingWindow(this).ShowDialog(desktop.MainWindow);
            _prefsService.Update(p => p.HasCompletedOnboarding = true);
        }
    }

    [RelayCommand]
    private void DevShowWhatsNew()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            var wn = new WhatsNewWindow("DEV");
            wn.Show(desktop.MainWindow);
        }
    }

    /// <summary>Injects 100 synthetic tracks (tagged "dev-seed") to exercise list scrolling, sorting, search.</summary>
    [RelayCommand]
    private void DevSeedLibrary() => SeedLibraryRequested?.Invoke(100);

    /// <summary>Removes exactly the tracks injected by DevSeedLibrary.</summary>
    [RelayCommand]
    private void DevRemoveSeeded() => RemoveSeededRequested?.Invoke();

    /// <summary>Exports a sanitized zip (logs + diagnostics + prefs, no API keys) for bug reports.</summary>
    [RelayCommand]
    private async Task DevExportSupportBundleAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow == null) return;
        var file = await desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Support Bundle",
            SuggestedFileName = $"nullwave-support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
            FileTypeChoices = new[] { new FilePickerFileType("Zip Archive") { Patterns = new[] { "*.zip" } } }
        });
        if (file == null) return;
        try
        {
            var tmp = Path.Combine(Path.GetTempPath(), $"nw-bundle-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmp);
            
            await File.WriteAllTextAsync(Path.Combine(tmp, "diagnostics.txt"), BuildDiagnosticsText());

            var prefsSrc = Path.Combine(NullWavePaths.DataDir, "prefs.json");
            if (File.Exists(prefsSrc)) File.Copy(prefsSrc, Path.Combine(tmp, "prefs.json"), true);

            if (Directory.Exists(NullWavePaths.LogsDir))
            {
                var logsTmp = Path.Combine(tmp, "logs");
                Directory.CreateDirectory(logsTmp);
                foreach (var f in Directory.GetFiles(NullWavePaths.LogsDir, "*.log").OrderByDescending(x => x).Take(6))
                    File.Copy(f, Path.Combine(logsTmp, Path.GetFileName(f)), true);
            }

            if (File.Exists(file.Path.LocalPath)) File.Delete(file.Path.LocalPath);
            ZipFile.CreateFromDirectory(tmp, file.Path.LocalPath);
            Directory.Delete(tmp, true);
            ToastService.Instance.Show($"Support bundle saved to {file.Name}.", ToastType.Success);
            Log.Information("[DevTools] Support bundle exported to {Path}", file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            ToastService.Instance.Show($"Bundle export failed: {ex.Message}", ToastType.Error);
            Log.Error(ex, "[DevTools] Support bundle export failed");
        }
    }

    /// <summary>Measures UI thread round-trip, managed heap, and working set.</summary>
    [RelayCommand]
    private async Task DevRunProbeAsync()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Dispatcher.UIThread.InvokeAsync(() => { });
        sw.Stop();
        var uiMs = sw.ElapsedMilliseconds;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var managedMb = GC.GetTotalMemory(false) / 1048576.0;
        var workingMb = Environment.WorkingSet / 1048576.0;

        DevProbeResult = $"UI thread round-trip: {uiMs} ms | Managed heap: {managedMb:F1} MB | Working set: {workingMb:F1} MB";
    }

    /// <summary>
    /// SAFE purge: raw file deletion alone orphans every AlbumArtPath (gray rows,
    /// black detail art) and leaves stale bitmaps in memory. This clears the decode
    /// cache and routes through the Maintenance pipeline, which deletes files, nulls
    /// paths in DB+memory, and kicks the refetch backfill.
    /// </summary>
    [RelayCommand]
    private void DevPurgeArtCache()
    {
        BitmapCacheService.Clear();
        ClearThumbnailsRequested?.Invoke();
        Log.Information("[DevTools] Art cache purge routed through Maintenance pipeline.");
    }

    /// <summary>Re-runs bar trimming over the whole art cache (dark-flat detector).</summary>
    [RelayCommand]
    private async Task DevRecropArtCacheAsync()
    {
        var (changed, total) = await Task.Run(() =>
        {
            var dir = Path.Combine(NullWavePaths.DataDir, "art");
            if (!Directory.Exists(dir)) return (0, 0);
            var files = Directory.GetFiles(dir, "*.jpg");
            int n = 0;
            foreach (var f in files)
                if (ThumbnailCropper.TrimLetterboxInPlace(f)) n++;
            return (n, files.Length);
        });

        // Rewritten files must not keep serving pre-crop bitmaps from memory.
        if (changed > 0) BitmapCacheService.Clear();

        ToastService.Instance.Show(
            changed > 0
                ? $"Re-cropped {changed} of {total} cached art files; memory cache refreshed."
                : $"Re-crop found nothing to trim in {total} file(s).",
            ToastType.Success);
        Log.Information("[DevTools] Art cache re-crop: {Changed}/{Total} files rewritten", changed, total);
    }

    /// <summary>Deletes legacy square-cropped YouTube thumbs and re-fetches them aspect-preserved.</summary>
    [RelayCommand]
    private void DevRefetchYouTubeThumbs()
    {
        ToastService.Instance.Show("Healing YouTube thumbnails in background - art may blink while re-fetching.",
            ToastType.Info, durationMs: 6000, scope: "maintenance");
        RefetchYouTubeThumbsRequested?.Invoke();
    }

    public void ReportYouTubeThumbsRefetched(int count)
    {
        ToastService.Instance.Show(count == 0
            ? "No YouTube thumbnails needed healing."
            : $"Healed {count} YouTube thumbnail(s) with aspect-preserving crop.",
            ToastType.Success, scope: "maintenance");
    }

    [ObservableProperty]
    private bool _devHoverTagEnabled;

    [ObservableProperty]
    private bool _devStormDetectorEnabled;

    [ObservableProperty]
    private int _devRealizationCount;

    [ObservableProperty]
    private string _devSelectedComposeMode = "lowlatency";

    public string[] DevComposeOptions => new[] { "lowlatency", "direct", "winui" };

    public void IncrementRealizationCount()
    {
        if (IsDevMode) DevRealizationCount++;
    }

    [RelayCommand]
    private void DevTogglePerfOverlay() => TogglePerfOverlayRequested?.Invoke();

    [RelayCommand]
    private void DevResetRealization() => DevRealizationCount = 0;

    partial void OnDevSelectedComposeModeChanged(string value)
    {
        Environment.SetEnvironmentVariable("NULLWAVE_COMPOSE", value);
        _prefsService.Update(p => p.ComposeMode = value);
        ScheduleSave();
        ToastService.Instance.Show($"Compose mode set to '{value}'. Restart app to apply.", ToastType.Info);
    }

    partial void OnDevHoverTagEnabledChanged(bool value)
    {
        HoverTaggingChanged?.Invoke(value);
        Log.Information("[DevTools] Hover tagging {State}", value ? "enabled" : "disabled");
    }

    partial void OnDevStormDetectorEnabledChanged(bool value)
    {
        StormDetectorChanged?.Invoke(value);
        Log.Information("[DevTools] Storm detector {State}", value ? "enabled" : "disabled");
    }

    #endregion
}
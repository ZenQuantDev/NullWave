using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Plugins;
using NullWave.Services.Security;
using NullWave.Services.SmartSorting;
using NullWave.ViewModels.Settings;
using Serilog;
using Serilog.Events;

namespace NullWave.ViewModels;

public partial class SettingsViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYtDlpReady))]
    [NotifyPropertyChangedFor(nameof(IsDepsChecking))]
    [NotifyPropertyChangedFor(nameof(IsDepsReady))]
    [NotifyPropertyChangedFor(nameof(ShowYtdlpMissing))]
    private string _ytDlpStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallVlc))]
    [NotifyPropertyChangedFor(nameof(IsVlcReady))]
    [NotifyPropertyChangedFor(nameof(IsDepsChecking))]
    [NotifyPropertyChangedFor(nameof(IsDepsReady))]
    [NotifyPropertyChangedFor(nameof(ShowVlcMissing))]
    private string _vlcStatus = string.Empty;

    [ObservableProperty] private string _ffmpegStatus = string.Empty;
    [ObservableProperty] private string _dotNetStatus = string.Empty;
    [ObservableProperty] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isUpdatingYtDlp;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _releaseUrl = string.Empty;
    [ObservableProperty] private string _updateStatus = "Not checked yet";
    [ObservableProperty] private bool _isStagingUpdate;
    [ObservableProperty] private bool _updateStaged;

    public bool IsDepsChecking => YtDlpStatus == "Checking..." || VlcStatus == "Checking...";
    public bool IsYtDlpReady => !string.IsNullOrEmpty(YtDlpStatus) && YtDlpStatus != "Not installed" && YtDlpStatus != "Checking...";
    public bool IsVlcReady => !string.IsNullOrEmpty(VlcStatus) && VlcStatus != "Not installed" && VlcStatus != "Checking...";
    public bool IsDepsReady => IsYtDlpReady && IsVlcReady;
    public bool ShowVlcMissing => !IsVlcReady && !IsDepsChecking;
    public bool ShowYtdlpMissing => !IsYtDlpReady && !IsDepsChecking;
    public bool CanInstallVlc => IsWindows && VlcStatus == "Not installed";

    [RelayCommand] private async Task CheckForUpdateAsync()
    {
        IsCheckingUpdate = true;
        UpdateStatus = "Checking...";
        try
        {
            var result = await _updater.CheckForUpdateAsync();
            UpdateAvailable = result.IsUpdateAvailable;
            LatestVersion = result.LatestVersion;
            ReleaseUrl = result.ReleaseUrl;
            UpdateStatus = result.IsUpdateAvailable ? $"Update available: v{result.LatestVersion}" : $"You are up to date (v{result.CurrentVersion})";
        }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand] private async Task DownloadUpdateAsync()
    {
        IsStagingUpdate = true;
        try
        {
            // Velopack handles RID matching automatically
            UpdateStaged = await _updater.StageUpdateAsync(); 
            ToastService.Instance.Show(
                UpdateStaged ? "Update downloaded — restart to install." : "No update available.", 
                UpdateStaged ? ToastType.Success : ToastType.Warning);
        }
        catch (Exception ex) 
        { 
            ToastService.Instance.Show($"Update download failed: {ex.Message}", ToastType.Error); 
        }
        finally 
        { 
            IsStagingUpdate = false; 
        }
    }

    [RelayCommand] private void RestartToUpdate() => _updater.ApplyUpdatesAndRestart();
    
    [RelayCommand] private void OpenReleasePage()
    {
        if (!string.IsNullOrEmpty(ReleaseUrl))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = ReleaseUrl, UseShellExecute = true });
    }

    [RelayCommand] private async Task UpdateYtDlpAsync()
    {
        IsUpdatingYtDlp = true;
        YtDlpStatus = "Updating yt-dlp...";
        try
        {
            await _deps.UpdateYtDlpAsync();
            var info = await _deps.GetYtDlpInfoAsync();
            YtDlpStatus = info.IsInstalled ? $"yt-dlp {info.InstalledVersion} (up to date)" : "yt-dlp not found";
        }
        finally { IsUpdatingYtDlp = false; }
    }

    [RelayCommand] private async Task InstallVlcAsync()
    {
        VlcStatus = "Installing...";
        VlcStatus = await _deps.InstallVlcAsync();
    }

    [RelayCommand] private async Task CheckDependenciesAsync()
    {
        YtDlpStatus = VlcStatus = FfmpegStatus = DotNetStatus = "Checking...";
        var ytDlp = await _deps.GetYtDlpInfoAsync();
        var vlc = await _deps.GetVlcInfoAsync();
        var ffmpeg = await _deps.GetFfmpegInfoAsync();
        var dotNet = await _deps.GetDotNetInfoAsync();
        YtDlpStatus = ytDlp.IsInstalled ? ytDlp.InstalledVersion : "Not installed";
        VlcStatus = vlc.IsInstalled ? vlc.InstalledVersion : "Not installed";
        FfmpegStatus = ffmpeg.IsInstalled ? ffmpeg.InstalledVersion : "Not installed";
        DotNetStatus = dotNet.IsInstalled ? dotNet.InstalledVersion : "Not found";
    }

    [RelayCommand] private void ClearYtDlpCache() => ClearYtDlpCacheRequested?.Invoke();

    [RelayCommand] private async Task CopyDebugInfoAsync()
    {
        var text = $"NullWave v{CurrentVersion} “{VersionCodename}”\n" +
                   $"OS: {OsLabel} ({RuntimeInformation.OSDescription})\n" +
                   $"RID: {RuntimeInformation.RuntimeIdentifier}\n" +
                   $".NET: {Environment.Version}";
        try
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
                ToastService.Instance.Show("Debug info copied to clipboard.", ToastType.Success);
            }
            else { ToastService.Instance.Show("Could not access clipboard.", ToastType.Warning); }
        }
        catch (Exception ex) { Log.Warning(ex, "[Settings] Failed to copy debug info"); }
    }

    private int _versionTapCount;
    [RelayCommand] private void TapVersion()
    {
        _versionTapCount++;
        if (_versionTapCount >= 7)
        {
            _versionTapCount = 0;
            ToastService.Instance.Show("🌼 Oxeye Daisy blooms for the curious.", ToastType.Info);
        }
    }

    [RelayCommand] private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch (Exception ex) { Log.Warning(ex, "[Settings] Failed to open URL: {Url}", url); }
    }
}
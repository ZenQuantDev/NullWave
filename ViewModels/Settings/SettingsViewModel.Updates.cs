using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
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
using Avalonia.Threading;
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCheckUpdate))] private bool _isCheckingUpdate;
    [ObservableProperty] private bool _isUpdatingYtDlp;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _releaseUrl = string.Empty;
    [ObservableProperty] private string _updateStatus = "Not checked yet";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCheckUpdate))] private bool _isStagingUpdate;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCheckUpdate))] private bool _updateStaged;
    [ObservableProperty] private int _downloadProgress;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LastCheckText))] [NotifyPropertyChangedFor(nameof(HasLastCheck))] private DateTime? _lastCheckUtc;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsDepsChecking))] private bool _isCheckingDeps;

    public bool IsAppInstalled => _updater.IsInstalled;
    public bool CanCheckUpdate => IsAppInstalled && !IsCheckingUpdate && !IsStagingUpdate && !UpdateStaged;
    public bool HasLastCheck => LastCheckUtc.HasValue;
    public string LastCheckText => LastCheckUtc is { } time
        ? string.Format(CultureInfo.CurrentCulture, L("Settings_Updates_CheckedAt"), time.ToLocalTime().ToString("t"))
        : string.Empty;

    private DependencyRowViewModel _ytDlpRow = null!;
    private DependencyRowViewModel _vlcRow = null!;
    private DependencyRowViewModel _ffmpegRow = null!;
    public IReadOnlyList<DependencyRowViewModel> Dependencies { get; private set; } = Array.Empty<DependencyRowViewModel>();

    private void BuildDependencyRows()
    {
        _ytDlpRow = new("yt-dlp", L) { ActionCommand = UpdateYtDlpCommand };
        _vlcRow = new("VLC", L) { ActionCommand = InstallVlcCommand };
        _ffmpegRow = new("FFmpeg", L);
        Dependencies = new[] { _ytDlpRow, _vlcRow, _ffmpegRow };
    }

    private void ApplyYtDlpRow(DependencyInfo info)
    {
        var state = DependencyStatus.ClassifyYtDlp(info);
        var canTry = state is DependencyState.UpdateAvailable or DependencyState.Ready;
        _ytDlpRow.Update(state, info.IsInstalled ? info.InstalledVersion : null, info.LatestVersion,
            actionLabel: canTry ? L("Settings_Updates_Update") : null);
    }

    private void ApplyVlcRow(DependencyInfo info) =>
        _vlcRow.Update(info.IsInstalled ? DependencyState.Ready : DependencyState.Missing,
            info.IsInstalled ? info.InstalledVersion : null,
            actionLabel: !info.IsInstalled && IsWindows ? L("Settings_Updates_Install") : null,
            note: info.IsInstalled ? L("Settings_Updates_SystemPackage") : null);

    private void ApplyFfmpegRow(DependencyInfo info) =>
        _ffmpegRow.Update(info.IsInstalled ? DependencyState.Ready : DependencyState.Missing,
            info.IsInstalled ? info.InstalledVersion : null);

    public bool IsDepsChecking => IsCheckingDeps || YtDlpStatus == "Checking..." || VlcStatus == "Checking...";
    public bool IsYtDlpReady => !string.IsNullOrEmpty(YtDlpStatus) && YtDlpStatus != "Not installed" && YtDlpStatus != "Checking...";
    public bool IsVlcReady => !string.IsNullOrEmpty(VlcStatus) && VlcStatus != "Not installed" && VlcStatus != "Checking...";
    public bool IsDepsReady => IsYtDlpReady && IsVlcReady;
    public bool ShowVlcMissing => !IsVlcReady && !IsDepsChecking;
    public bool ShowYtdlpMissing => !IsYtDlpReady && !IsDepsChecking;
    public bool CanInstallVlc => IsWindows && VlcStatus == "Not installed";

    [RelayCommand] private async Task CheckForUpdateAsync()
    {
        if (!CanCheckUpdate) return;

        IsCheckingUpdate = true;
        UpdateStatus = L("Settings_Updates_Status_Checking");
        try
        {
            var result = await _updater.CheckForUpdateAsync();
            UpdateAvailable = result.IsUpdateAvailable;
            LatestVersion = result.LatestVersion;
            ReleaseUrl = result.ReleaseUrl;
            
            UpdateStatus = UpdateVersioning.StatusText(result, L);
            if (!result.Failed && !result.NotInstalled) LastCheckUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Settings] Update check failed");
            UpdateStatus = L("Settings_Updates_Status_CheckFailed");
        }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand] private async Task DownloadUpdateAsync()
    {
        if (IsStagingUpdate || UpdateStaged) return;
        IsStagingUpdate = true;
        DownloadProgress = 0;
        try
        {
            var result = await _updater.StageUpdateAsync(progress => Dispatcher.UIThread.Post(() => DownloadProgress = progress));
            switch (result.Outcome)
            {
                case StageOutcome.Staged:
                    UpdateStaged = true;
                    UpdateStatus = string.Format(CultureInfo.CurrentCulture, L("Settings_Updates_Ready"), LatestVersion);
                    ToastService.Instance.Show(L("Settings_Updates_Toast_Downloaded"), ToastType.Success);
                    break;
                case StageOutcome.NoUpdate:
                    UpdateAvailable = false;
                    UpdateStatus = UpdateVersioning.StatusText(new UpdateCheckResult { CurrentVersion = CurrentVersion }, L);
                    ToastService.Instance.Show(L("Settings_Updates_Toast_NoUpdate"), ToastType.Warning);
                    break;
                case StageOutcome.NotInstalled:
                    ToastService.Instance.Show(L("Settings_Updates_Toast_NotInstalled"), ToastType.Warning);
                    break;
                default:
                    ToastService.Instance.Show(string.Format(CultureInfo.CurrentCulture, L("Settings_Updates_Toast_Failed"), result.Error), ToastType.Error);
                    break;
            }
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
            ShellHelper.OpenUrl(ReleaseUrl);
    }

    [RelayCommand] private async Task UpdateYtDlpAsync()
    {
        IsUpdatingYtDlp = true;
        YtDlpStatus = "Updating yt-dlp...";
        _ytDlpRow.Update(DependencyState.Busy);
        try
        {
            var msg = await _deps.UpdateYtDlpAsync();
            var info = await _deps.GetYtDlpInfoAsync();
            
            // FIX: Honest status based on update result and version comparison
            YtDlpStatus = !info.IsInstalled ? "Not installed"
                : msg.StartsWith("Update failed") ? $"{info.InstalledVersion} (update failed)"
                : msg == "Already up to date" ? $"{info.InstalledVersion} (up to date)"
                : info.LatestVersion != "unknown" && info.InstalledVersion != info.LatestVersion 
                    ? $"{info.InstalledVersion} → {info.LatestVersion}"
                    : $"{info.InstalledVersion} (up to date)";
            if (msg.StartsWith("Update failed", StringComparison.Ordinal) && info.IsInstalled)
                _ytDlpRow.Update(DependencyState.Failed, info.InstalledVersion, info.LatestVersion, L("Settings_Updates_Update"));
            else ApplyYtDlpRow(info);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Settings] yt-dlp update failed");
            YtDlpStatus = "Update failed";
            _ytDlpRow.Update(DependencyState.Failed);
        }
        finally { IsUpdatingYtDlp = false; }
    }

    [RelayCommand] private async Task InstallVlcAsync()
    {
        VlcStatus = "Installing...";
        _vlcRow.Update(DependencyState.Busy);
        try
        {
            var status = await _deps.InstallVlcAsync(ConfirmPackageInstallAsync);
            VlcStatus = status == DependencyUpdateService.InstallCancelledStatus
                ? LocalizationService.Instance["Settings_Updates_InstallDeclined"]
                : status;
            var vlcInfo = await _deps.GetVlcInfoAsync();
            ApplyVlcRow(vlcInfo);
            if (!vlcInfo.IsInstalled) _vlcRow.SetMessage(VlcStatus);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Settings] VLC install failed");
            VlcStatus = "Install failed";
            _vlcRow.Update(DependencyState.Failed);
        }
    }

    private async Task<bool> ConfirmPackageInstallAsync(string command)
    {
        var rememberedChoice = _prefsService.Current.PackageManagerInstallConsent;
        if (rememberedChoice.HasValue)
            return rememberedChoice.Value;

        var dialog = new Views.Dialogs.PackageManagerInstallDialog(command);
        var choice = await Dispatcher.UIThread.InvokeAsync(
            () => DialogService.Instance.ShowDialogAsync<Views.Dialogs.PackageInstallConsentChoice>(dialog));
        if (choice?.RememberChoice == true)
            _prefsService.Update(p => p.PackageManagerInstallConsent = choice.Approved);
        return choice?.Approved == true;
    }

    [RelayCommand] private async Task CheckDependenciesAsync()
    {
        // FIX: Busy guard to prevent double-clicks
        if (IsCheckingDeps) return;
        
        IsCheckingDeps = true;
        YtDlpStatus = VlcStatus = FfmpegStatus = DotNetStatus = "Checking...";
        foreach (var row in Dependencies) row.Update(DependencyState.Checking);
        
        try
        {
            // FIX: Run all probes in parallel for faster UI response
            var ytDlpTask = _deps.GetYtDlpInfoAsync();
            var vlcTask = _deps.GetVlcInfoAsync();
            var ffmpegTask = _deps.GetFfmpegInfoAsync();
            var dotNetTask = _deps.GetDotNetInfoAsync();
            
            await Task.WhenAll(ytDlpTask, vlcTask, ffmpegTask, dotNetTask);
            
            var ytDlp = ytDlpTask.Result;
            var vlc = vlcTask.Result;
            var ffmpeg = ffmpegTask.Result;
            var dotNet = dotNetTask.Result;
            
            YtDlpStatus = ytDlp.IsInstalled ? ytDlp.InstalledVersion : "Not installed";
            VlcStatus = vlc.IsInstalled ? vlc.InstalledVersion : "Not installed";
            FfmpegStatus = ffmpeg.IsInstalled ? ffmpeg.InstalledVersion : "Not installed";
            DotNetStatus = dotNet.IsInstalled ? dotNet.InstalledVersion : "Not found";
            ApplyYtDlpRow(ytDlp);
            ApplyVlcRow(vlc);
            ApplyFfmpegRow(ffmpeg);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Settings] Dependency check failed");
            YtDlpStatus = VlcStatus = FfmpegStatus = DotNetStatus = "Check failed";
            foreach (var row in Dependencies) row.Update(DependencyState.Failed);
        }
        finally
        {
            IsCheckingDeps = false;
        }
    }

    [RelayCommand] private void ClearYtDlpCache() => ClearYtDlpCacheRequested?.Invoke();

    [RelayCommand] private async Task CopyDebugInfoAsync()
    {
        var text = $"NullWave v{CurrentVersion} \"{VersionCodename}\"\n" +
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
        var reward = WallpaperBuiltIns.RewardFor(_versionTapCount);
        if (reward.Lore)
            ToastService.Instance.Show("🌼 Oxeye Daisy blooms for the curious.", ToastType.Info);
        if (reward.SignatureAccent)
        {
            AccentColor = ThemeService.CodenameAccent.Name;
            ToastService.Instance.Show(L("Settings_About_Tap14"), ToastType.Success);
        }
        if (reward.ExclusiveUnlock)
            UnlockExclusiveWallpaper();
    }

    [RelayCommand] private void OpenUrl(string url)
    {
        ShellHelper.OpenUrl(url);
    }
}
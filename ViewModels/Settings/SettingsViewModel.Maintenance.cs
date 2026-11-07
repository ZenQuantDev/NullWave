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
    [ObservableProperty] private string _thumbnailStatus = string.Empty;
    [ObservableProperty] private string _repairStatus = string.Empty;
    [ObservableProperty] private string _sweepStatus = string.Empty;
    [ObservableProperty] private string _vacuumStatus = string.Empty;
    [ObservableProperty] private string _verifyLinksStatus = string.Empty;
    [ObservableProperty] private string _forceCleanStatus = string.Empty;
    [ObservableProperty] private string _dedupeStatus = string.Empty;
    [ObservableProperty] private string _syncFilesStatus = string.Empty;

    [RelayCommand] private void ClearThumbnails() { ThumbnailStatus = L("Settings_Maintenance_Status_ClearingThumbnails"); ClearThumbnailsRequested?.Invoke(); }
    [RelayCommand] private void RepairPaths() { IsRepairing = true; RepairStatus = L("Settings_Maintenance_Status_ScanningPaths"); RepairPathsRequested?.Invoke(); }
    [RelayCommand] private void ReimportAssets() { IsRepairing = true; RepairStatus = L("Settings_Maintenance_Status_ScanningDownloads"); ReimportAssetsRequested?.Invoke(); }
    [RelayCommand] private void ForceMetaResync() { IsRepairing = true; RepairStatus = L("Settings_Maintenance_Status_ResyncStarting"); ForceMetaResyncRequested?.Invoke(); }
    [RelayCommand] private void PreviewOrphanedFiles() { IsRepairing = true; SweepStatus = LocalizationService.Instance["Settings_Maintenance_Sweep_Scanning"]; SweepOrphanedFilesRequested?.Invoke(true); }
    [RelayCommand] private void SweepOrphanedFiles() { IsRepairing = true; SweepStatus = LocalizationService.Instance["Settings_Maintenance_Sweep_Moving"]; SweepOrphanedFilesRequested?.Invoke(false); }
    [RelayCommand] private void VacuumDatabase() { IsRepairing = true; VacuumStatus = L("Settings_Maintenance_Status_OptimizingDatabase"); VacuumDatabaseRequested?.Invoke(); }
    [RelayCommand] private void RebuildSearchIndex() { IsRepairing = true; RebuildSearchIndexRequested?.Invoke(); }
    [RelayCommand] private void VerifyLinks() { IsRepairing = true; VerifyLinksStatus = L("Settings_Maintenance_Status_CheckingLinks"); VerifyLinksRequested?.Invoke(); }
    [RelayCommand] private void ForceCleanTitles() { IsRepairing = true; ForceCleanStatus = L("Settings_Maintenance_Status_CleaningTitles"); ForceCleanTitlesRequested?.Invoke(); }
    [RelayCommand] private void MergeSimilarArtists() => MergeSimilarArtistsRequested?.Invoke();
    [RelayCommand] private void PreviewDuplicates() { IsRepairing = true; DedupeStatus = L("Settings_Maintenance_Status_ScanningDuplicates"); RemoveDuplicatesRequested?.Invoke(true); }
    [RelayCommand] private void RemoveDuplicates() { IsRepairing = true; DedupeStatus = L("Settings_Maintenance_Status_RemovingDuplicates"); RemoveDuplicatesRequested?.Invoke(false); }
    [RelayCommand] private void PreviewSyncFiles() { IsRepairing = true; SyncFilesStatus = L("Settings_Maintenance_Status_ScanningFiles"); SyncFilesRequested?.Invoke(true); }
    [RelayCommand] private void SyncFiles() { IsRepairing = true; SyncFilesStatus = L("Settings_Maintenance_Status_SyncingFiles"); SyncFilesRequested?.Invoke(false); }
    [RelayCommand] private void RequestBackfillDurations() => BackfillDurationsRequested?.Invoke();
    public event Action<string>? BackupDatabaseRequested;
    [RelayCommand] private void RequestBackup(string path) => BackupDatabaseRequested?.Invoke(path);

    [RelayCommand] private void RestoreDatabase() => RestoreDatabaseRequested?.Invoke();   

    #region Reporters
    public void ReportDedupeComplete(int scanned, int groups, int removed, bool wasDryRun)
    {
        IsRepairing = false;
        DedupeStatus = wasDryRun
            ? string.Format(L("Settings_Maintenance_Status_DedupePreview"), groups, removed, scanned)
            : string.Format(L("Settings_Maintenance_Status_DedupeComplete"), removed, groups);
        ToastService.Instance.Show(DedupeStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportSweepComplete(int scanned, int orphaned, int moved, int failed, bool wasDryRun, bool guardRefused, bool usedFallback, bool cancelled)
    {
        IsRepairing = false;
        var strings = LocalizationService.Instance;
        if (guardRefused)
        {
            SweepStatus = strings["Settings_Maintenance_Sweep_Guard"];
            ToastService.Instance.Show(SweepStatus, ToastType.Warning, scope: "maintenance");
            return;
        }
        if (cancelled)
            SweepStatus = strings["Settings_Maintenance_Sweep_Cancelled"];
        else if (wasDryRun)
            SweepStatus = string.Format(strings["Settings_Maintenance_Sweep_Preview"], orphaned, scanned);
        else if (failed == 0)
            SweepStatus = string.Format(strings["Settings_Maintenance_Sweep_Complete"], moved);
        else
            SweepStatus = string.Format(strings["Settings_Maintenance_Sweep_Failed"], moved, failed);

        if (usedFallback)
            SweepStatus = strings["Settings_Maintenance_Sweep_Fallback"] + " " + SweepStatus;
        ToastService.Instance.Show(
            SweepStatus,
            usedFallback || failed > 0 ? ToastType.Warning : ToastType.Success,
            scope: "maintenance");
    }
    public void ReportVacuumComplete(long beforeKB, long afterKB)
    {
        IsRepairing = false;
        var saved = beforeKB - afterKB;
        VacuumStatus = saved > 0
            ? string.Format(L("Settings_Maintenance_Status_VacuumComplete"), beforeKB, afterKB, saved)
            : string.Format(L("Settings_Maintenance_Status_VacuumAlreadyOptimal"), afterKB);
        ToastService.Instance.Show(VacuumStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportSearchIndexRebuildComplete()
    {
        IsRepairing = false;
        ToastService.Instance.Show(L("Settings_Maintenance_Status_SearchIndexComplete"), ToastType.Success, scope: "maintenance");
    }
    public void ReportSearchIndexRebuildFailed(string reason)
    {
        IsRepairing = false;
        Log.Error("[Settings] Search index rebuild failed: {Reason}", reason);
        ToastService.Instance.Show(L("Settings_Maintenance_Status_SearchIndexFailed"), ToastType.Error, scope: "maintenance");
    }
    public void ReportVerifyLinksComplete(int checkedCount, int mismatchCount)
    {
        IsRepairing = false;
        VerifyLinksStatus = mismatchCount == 0
            ? string.Format(L("Settings_Maintenance_Status_VerifyLinksClean"), checkedCount)
            : string.Format(L("Settings_Maintenance_Status_VerifyLinksMismatch"), checkedCount, mismatchCount);
        ToastService.Instance.Show(VerifyLinksStatus, mismatchCount > 0 ? ToastType.Warning : ToastType.Success, scope: "maintenance");
    }
    public void ReportForceCleanComplete(int cleaned)
    {
        IsRepairing = false;
        ForceCleanStatus = cleaned == 0
            ? L("Settings_Maintenance_Status_NoTitlesToClean")
            : string.Format(L("Settings_Maintenance_Status_TitlesCleaned"), cleaned);
        ToastService.Instance.Show(ForceCleanStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportArtistMergeComplete(int groupsFound, int tracksUpdated)
    {
        var message = groupsFound == 0
            ? L("Settings_Maintenance_Status_NoDuplicateArtists")
            : string.Format(L("Settings_Maintenance_Status_ArtistsMerged"), groupsFound, tracksUpdated);
        ToastService.Instance.Show(message, ToastType.Info, scope: "maintenance");
    }
    public void ReportSyncFilesComplete(FileSyncReport r, bool wasDryRun)
    {
        IsRepairing = false;
        SyncFilesStatus = wasDryRun
            ? string.Format(L("Settings_Maintenance_Status_SyncPreview"), r.Retagged, r.Renamed, r.Scanned)
            : string.Format(L("Settings_Maintenance_Status_SyncComplete"), r.Scanned, r.Retagged, r.Renamed, r.Failed);
        ToastService.Instance.Show(SyncFilesStatus, r.Failed > 0 ? ToastType.Warning : ToastType.Success, scope: "maintenance");
    }
    public void ReportThumbnailsCleared(int count)
    {
        ThumbnailStatus = string.Format(L("Settings_Maintenance_Status_ThumbnailsCleared"), count);
        ToastService.Instance.Show(ThumbnailStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportBackfillDurationsComplete(int updated)
    {
        ToastService.Instance.Show(string.Format(L("Settings_Maintenance_Status_DurationsUpdated"), updated), ToastType.Success, scope: "maintenance");
    }
    public void ReportRepairPathsComplete(int total, int missing, int cleared, int relinked)
    {
        IsRepairing = false;
        if (missing == 0)
        {
            RepairStatus = string.Format(L("Settings_Maintenance_Status_PathsValid"), total);
        }
        else
        {
            RepairStatus = string.Format(L("Settings_Maintenance_Status_PathsMissing"), missing, cleared, relinked);
        }
        ToastService.Instance.Show(RepairStatus, missing == 0 ? ToastType.Success : ToastType.Warning, scope: "maintenance");
    }
    public void ReportReimportComplete(int relinked)
    {
        IsRepairing = false;
        RepairStatus = relinked == 0
            ? L("Settings_Maintenance_Status_NoReimportMatches")
            : string.Format(L("Settings_Maintenance_Status_ReimportComplete"), relinked);
        ToastService.Instance.Show(RepairStatus, relinked > 0 ? ToastType.Success : ToastType.Info, scope: "maintenance");
    }
    public void ReportMetaResyncComplete(int cleared)
    {
        IsRepairing = false;
        RepairStatus = string.Format(L("Settings_Maintenance_Status_ResyncComplete"), cleared);
        ToastService.Instance.Show(RepairStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportRepairFailed(string operation, string reason)
    {
        IsRepairing = false;
        RepairStatus = string.Format(L("Settings_Maintenance_Status_OperationFailed"), operation, reason);
        ToastService.Instance.Show(RepairStatus, ToastType.Error, scope: "maintenance");
    }

    public void ReportBackupComplete(string fileName)
    {
        IsRepairing = false;
        ToastService.Instance.Show(string.Format(L("Settings_Maintenance_Status_BackupComplete"), fileName), ToastType.Success, scope: "maintenance");
    }
    public void ReportBackupFailed(string reason)
    {
        IsRepairing = false;
        ToastService.Instance.Show(string.Format(L("Settings_Maintenance_Status_BackupFailed"), reason), ToastType.Error, scope: "maintenance");
    }
    #endregion
}
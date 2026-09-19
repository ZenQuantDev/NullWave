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

    [RelayCommand] private void ClearThumbnails() { ThumbnailStatus = "Clearing thumbnails..."; ClearThumbnailsRequested?.Invoke(); }
    [RelayCommand] private void RepairPaths() { IsRepairing = true; RepairStatus = "Scanning file paths..."; RepairPathsRequested?.Invoke(); }
    [RelayCommand] private void ReimportAssets() { IsRepairing = true; RepairStatus = "Scanning download folder..."; ReimportAssetsRequested?.Invoke(); }
    [RelayCommand] private void ForceMetaResync() { IsRepairing = true; RepairStatus = "Clearing cached tags - re-sync starting..."; ForceMetaResyncRequested?.Invoke(); }
    [RelayCommand] private void PreviewOrphanedFiles() { IsRepairing = true; SweepStatus = "Scanning for orphaned files..."; SweepOrphanedFilesRequested?.Invoke(true); }
    [RelayCommand] private void SweepOrphanedFiles() { IsRepairing = true; SweepStatus = "Deleting orphaned files..."; SweepOrphanedFilesRequested?.Invoke(false); }
    [RelayCommand] private void VacuumDatabase() { IsRepairing = true; VacuumStatus = "Optimizing database..."; VacuumDatabaseRequested?.Invoke(); }
    [RelayCommand] private void VerifyLinks() { IsRepairing = true; VerifyLinksStatus = "Checking file links against embedded metadata..."; VerifyLinksRequested?.Invoke(); }
    [RelayCommand] private void ForceCleanTitles() { IsRepairing = true; ForceCleanStatus = "Re-parsing track titles for embedded artist names..."; ForceCleanTitlesRequested?.Invoke(); }
    [RelayCommand] private void MergeSimilarArtists() => MergeSimilarArtistsRequested?.Invoke();
    [RelayCommand] private void PreviewDuplicates() { IsRepairing = true; DedupeStatus = "Scanning for duplicate tracks..."; RemoveDuplicatesRequested?.Invoke(true); }
    [RelayCommand] private void RemoveDuplicates() { IsRepairing = true; DedupeStatus = "Removing duplicate tracks..."; RemoveDuplicatesRequested?.Invoke(false); }
    [RelayCommand] private void PreviewSyncFiles() { IsRepairing = true; SyncFilesStatus = "Scanning files..."; SyncFilesRequested?.Invoke(true); }
    [RelayCommand] private void SyncFiles() { IsRepairing = true; SyncFilesStatus = "Syncing files..."; SyncFilesRequested?.Invoke(false); }
    [RelayCommand] private void RequestBackfillDurations() => BackfillDurationsRequested?.Invoke();
    [RelayCommand] private void RestoreDatabase() => RestoreDatabaseRequested?.Invoke();

    #region Reporters
    public void ReportDedupeComplete(int scanned, int groups, int removed, bool wasDryRun)
    {
        IsRepairing = false;
        DedupeStatus = wasDryRun ? $"Found {groups} duplicate group(s) ({removed} extra track(s) would be removed) out of {scanned} scanned. Click Remove to clean up." : $"✓ Removed {removed} duplicate track(s) across {groups} group(s).";
        ToastService.Instance.Show(DedupeStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportSweepComplete(int scanned, int orphaned, int deleted, int failed, bool wasDryRun)
    {
        IsRepairing = false;
        SweepStatus = wasDryRun ? $"Found {orphaned} orphaned file(s) out of {scanned} scanned. Click Sweep to delete." : failed == 0 ? $"✓ Deleted {deleted} orphaned file(s)." : $"Deleted {deleted} file(s), {failed} failed (check logs).";
        ToastService.Instance.Show(SweepStatus, failed > 0 ? ToastType.Warning : ToastType.Success, scope: "maintenance");
    }
    public void ReportVacuumComplete(long beforeKB, long afterKB)
    {
        IsRepairing = false;
        var saved = beforeKB - afterKB;
        VacuumStatus = saved > 0 ? $"✓ Optimized: {beforeKB}KB → {afterKB}KB ({saved}KB reclaimed)" : $"✓ Database already optimal ({afterKB}KB)";
        ToastService.Instance.Show(VacuumStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportVerifyLinksComplete(int checkedCount, int mismatchCount)
    {
        IsRepairing = false;
        VerifyLinksStatus = mismatchCount == 0 ? $"✓ Checked {checkedCount} linked track(s) — no mismatches found." : $"⚠ Checked {checkedCount} track(s) — found {mismatchCount} possible mis-link(s). See logs for details.";
        ToastService.Instance.Show(VerifyLinksStatus, mismatchCount > 0 ? ToastType.Warning : ToastType.Success, scope: "maintenance");
    }
    public void ReportForceCleanComplete(int cleaned)
    {
        IsRepairing = false;
        ForceCleanStatus = cleaned == 0 ? "No titles needed cleaning." : $"✓ Cleaned {cleaned} track title(s)/artist(s). Spot-check multi-dash titles for accuracy.";
        ToastService.Instance.Show(ForceCleanStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportArtistMergeComplete(int groupsFound, int tracksUpdated)
    {
        var message = groupsFound == 0 ? "No duplicate artist names found." : $"Merged {groupsFound} artist group(s), updated {tracksUpdated} track(s).";
        ToastService.Instance.Show(message, ToastType.Info, scope: "maintenance");
    }
    public void ReportSyncFilesComplete(FileSyncReport r, bool wasDryRun)
    {
        IsRepairing = false;
        SyncFilesStatus = wasDryRun ? $"Preview: {r.Retagged} file(s) would be retagged, {r.Renamed} renamed (of {r.Scanned} scanned)." : $"✓ Synced {r.Scanned} file(s): {r.Retagged} retagged, {r.Renamed} renamed, {r.Failed} failed.";
        ToastService.Instance.Show(SyncFilesStatus, r.Failed > 0 ? ToastType.Warning : ToastType.Success, scope: "maintenance");
    }
    public void ReportThumbnailsCleared(int count)
    {
        ThumbnailStatus = $"Cleared {count} thumbnails - re-fetching in background...";
        ToastService.Instance.Show(ThumbnailStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportBackfillDurationsComplete(int updated)
    {
        ToastService.Instance.Show($"Updated duration for {updated} track(s).", ToastType.Success, scope: "maintenance");
    }
    public void ReportRepairPathsComplete(int total, int missing, int cleared)
    {
        IsRepairing = false;
        RepairStatus = missing == 0 ? $"✓ All {total} file paths are valid." : $"Found {missing} missing file(s) - {cleared} path(s) cleared.";
        ToastService.Instance.Show(RepairStatus, missing == 0 ? ToastType.Success : ToastType.Warning, scope: "maintenance");
    }
    public void ReportReimportComplete(int relinked)
    {
        IsRepairing = false;
        RepairStatus = relinked == 0 ? "No new file matches found." : $"✓ Re-linked {relinked} track(s).";
        ToastService.Instance.Show(RepairStatus, relinked > 0 ? ToastType.Success : ToastType.Info, scope: "maintenance");
    }
    public void ReportMetaResyncComplete(int cleared)
    {
        IsRepairing = false;
        RepairStatus = $"✓ Cleared tags for {cleared} track(s) - re-sync running.";
        ToastService.Instance.Show(RepairStatus, ToastType.Success, scope: "maintenance");
    }
    public void ReportRepairFailed(string operation, string reason)
    {
        IsRepairing = false;
        RepairStatus = $"✗ {operation} failed: {reason}";
        ToastService.Instance.Show(RepairStatus, ToastType.Error, scope: "maintenance");
    }
    #endregion
}
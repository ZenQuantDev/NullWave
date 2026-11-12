using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Services;
using NullWave.Helpers;
using Serilog;

namespace NullWave.ViewModels;

public partial class MainViewModel
{
    private void WireMaintenanceEvents()
    {
        Settings.SweepOrphanedFilesRequested += dryRun => RunMaintenanceTask("SweepOrphanedFiles", dryRun, async () =>
        {
            var configuredDirectory = _prefsService.Current.DownloadDirectory;
            var result = dryRun
                ? await OrphanSweepCoordinator.PreviewAsync(_library, configuredDirectory, NullWavePaths.DownloadsDir)
                : await OrphanSweepCoordinator.SweepAsync(
                    _library,
                    configuredDirectory,
                    NullWavePaths.DownloadsDir,
                    ConfirmOrphanSweepAsync);
            return () => Settings.ReportSweepComplete(
                result.Scanned, result.Orphaned, result.Moved, result.Failed, dryRun,
                result.GuardRefused, result.UsedFallback, result.Cancelled);
        });

        Settings.VacuumDatabaseRequested += () => RunMaintenanceTask("VacuumDatabase", false, async () =>
        {
            var (before, after) = await Task.Run(() => _library.VacuumDatabase());
            return () => Settings.ReportVacuumComplete(before, after);
        });

        Settings.RebuildSearchIndexRequested += () => RunMaintenanceTask("RebuildSearchIndex", false, async () =>
        {
            try
            {
                await Task.Run(_library.RebuildFtsIndex);
                return () => Settings.ReportSearchIndexRebuildComplete();
            }
            catch (Exception ex)
            {
                return () => Settings.ReportSearchIndexRebuildFailed(ex.Message);
            }
        });

        Settings.VerifyLinksRequested += () => RunMaintenanceTask("VerifyLinks", false, async () =>
        {
            var (checkedCount, mismatches) = await Task.Run(() => _library.VerifyLinks());
            return () => Settings.ReportVerifyLinksComplete(checkedCount, mismatches.Count);
        });

        Settings.RemoveDuplicatesRequested += dryRun => RunMaintenanceTask("RemoveDuplicates", dryRun, async () =>
        {
            var (scanned, groups, removed) = await Task.Run(() => _library.RemoveDuplicates(dryRun));
            return () => { Settings.ReportDedupeComplete(scanned, groups, removed, dryRun); Library.Refresh(); Library.RefreshArtistGroups(); };
        });

        Settings.ForceCleanTitlesRequested += () => RunMaintenanceTask("ForceCleanTitles", false, async () =>
        {
            var cleaned = await Task.Run(() => _library.ForceCleanTitles());
            return () => { Settings.ReportForceCleanComplete(cleaned); Library.Refresh(); Library.RefreshArtistGroups(); };
        });

        Settings.MergeSimilarArtistsRequested += () => RunMaintenanceTask("MergeSimilarArtists", false, async () =>
        {
            var groups = await Task.Run(() => _library.FindSimilarArtistGroups());
            int merged = 0;
            foreach (var group in groups) merged += _library.MergeArtistGroup(group);
            return () => { Settings.ReportArtistMergeComplete(groups.Count, merged); Library.Refresh(); Library.RefreshArtistGroups(); };
        });

        Settings.ClearThumbnailsRequested += () => RunMaintenanceTask("ClearThumbnails", false, async () =>
        {
            int cleared = await Task.Run(() => { var count = _library.GetAll().Count(t => !string.IsNullOrEmpty(t.AlbumArtPath)); _library.ClearAllArt(); return count; });
            _library.RebackfillThumbnails();
            await Task.Delay(500);
            _enrichment.BackfillAsync();
            await Task.Delay(1500);
            return () => { Settings.ReportThumbnailsCleared(cleared); Library.Refresh(); };
        });

        Settings.RefetchYouTubeThumbsRequested += () =>
        {
            _ = Task.Run(() =>
            {
                var count = _library.RefetchYouTubeThumbnails();
                _library.RebackfillThumbnails();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Settings.ReportYouTubeThumbsRefetched(count));
            });
        };

        Settings.RepairPathsRequested += () => RunMaintenanceTask("RepairPaths", false, async () =>
        {
            // 1. Auto-relink them using the [nw-id-videoId] suffix FIRST!
            var relinked = await Task.Run(() => _library.RelinkMissingFiles());
            
            // 2. Clear remaining dead paths
            var (total, missing, removed) = await Task.Run(() => _library.RepairPaths(removeDeadEntries: true));

            return () => { Library.Refresh(); Settings.ReportRepairPathsComplete(total, missing, removed, relinked); };
        });

        Settings.ReimportAssetsRequested += () => RunMaintenanceTask("ReimportAssets", false, async () =>
        {
            var dir = _prefsService.Current.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave", "downloads");
            var relinked = await Task.Run(() => _library.ReimportAssets(dir));
            return () => { Library.Refresh(); Settings.ReportReimportComplete(relinked); };
        });

        Settings.ForceMetaResyncRequested += () => RunMaintenanceTask("ForceMetaResync", false, async () =>
        {
            int cleared = await Task.Run(() => _library.ClearTagsForReSync());
            await Task.Delay(800);
            _enrichment.BackfillAsync();
            await Task.Delay(1200);
            return () => { Settings.ReportMetaResyncComplete(cleared); Library.Refresh(); };
        });

        Settings.SyncFilesRequested += dryRun => RunMaintenanceTask("SyncFiles", dryRun, async () =>
        {
            string? playing = null;
            await Dispatcher.UIThread.InvokeAsync(() => playing = Player.CurrentTrack?.FilePath);
            var r = _library.SyncLocalFilesWithLibrary(dryRun, playing);
            return () => { Settings.ReportSyncFilesComplete(r, dryRun); Library.Refresh(); };
        });

        Settings.BackfillDurationsRequested += () => RunMaintenanceTask("BackfillDurations", false, async () =>
        {
            int updated = await Task.Run(() => _library.BackfillDurations());
            return () => Settings.ReportBackfillDurationsComplete(updated);
        });

        Settings.RestoreDatabaseRequested += () => RunMaintenanceTask("RestoreDatabase", false, async () =>
        {
            string? selectedFile = null;
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
                {
                    var files = await desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
                    {
                        Title = LocalizationService.Instance["Settings_Maintenance_Restore_Picker_Title"],
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType(LocalizationService.Instance["Settings_Maintenance_Restore_FileType"]) { Patterns = new[] { "*.db" } } }
                    });
                    if (files.Count > 0) selectedFile = files[0].Path.LocalPath;
                }
            });

            if (string.IsNullOrEmpty(selectedFile)) return () => ToastService.Instance.Show(LocalizationService.Instance["Settings_Maintenance_Status_RestoreCancelled"], ToastType.Info);

            string targetPath = NullWavePaths.DatabasePath;
            string tempPath = targetPath + ".restoring";
            File.Copy(selectedFile, tempPath, overwrite: true);
            return () => ToastService.Instance.Show(LocalizationService.Instance["Settings_Maintenance_Status_RestoreStaged"], ToastType.Success, 10000, scope: "maintenance");
        });

        Settings.BackupDatabaseRequested += path => RunMaintenanceTask("BackupDatabase", false, async () =>
        {
            await Task.Run(() => _library.BackupDatabase(path));
            return () => Settings.ReportBackupComplete(Path.GetFileName(path));
        });
    }

    private void RunMaintenanceTask(string taskName, bool dryRun, Func<Task<Action>> taskFactory)
    {
        if (_isMaintenanceRunning) return;
        _isMaintenanceRunning = true;

        _ = Task.Run(async () =>
        {
            LiveNotification? activity = null;
            string taskLabel = LocalizationService.Instance[$"Settings_Maintenance_Task_{taskName}"];
            string activityLabel = string.Format(
                LocalizationService.Instance[dryRun ? "Settings_Maintenance_Task_Previewing" : "Settings_Maintenance_Task_Running"],
                taskLabel);
            
            await Dispatcher.UIThread.InvokeAsync(() =>
                activity = ToastService.Instance.StartLiveActivity(
                    activityLabel,
                    LocalizationService.Instance["Settings_Maintenance_Task_Processing"],
                    isIndeterminate: true,
                    scope: "maintenance"));

            try
            {
                var uiCallback = await taskFactory();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    uiCallback();
                    _isMaintenanceRunning = false;
                });
            }
            catch (Exception ex)
            {
                NullActionLogger.Error(nameof(MainViewModel), ex, $"{taskName} failed");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ToastService.Instance.CompleteLiveActivity(
                        activity,
                        string.Format(LocalizationService.Instance["Settings_Maintenance_Task_Failed"], taskLabel),
                        finalType: ToastType.Error);
                    _isMaintenanceRunning = false;
                });
            }
        });
    }

    private async Task<bool> ConfirmOrphanSweepAsync(int scanned, int orphaned)
    {
        var strings = LocalizationService.Instance;
        var message = string.Format(strings["Settings_Maintenance_Sweep_Confirm"], orphaned, scanned);
        var dialog = new Views.ConfirmDialog(
            strings["Settings_Maintenance_Sweep_Confirm_Title"],
            message);
        var dialogTask = await Dispatcher.UIThread.InvokeAsync(
            () => DialogService.Instance.ShowDialogAsync<bool>(dialog));
        return dialogTask == true;
    }
}
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
            var dir = _prefsService.Current.DownloadDirectory;
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nullwave", "downloads");
            var (scanned, orphaned, deleted, failed) = await Task.Run(() => _library.SweepOrphanedFiles(dir, dryRun));
            return () => Settings.ReportSweepComplete(scanned, orphaned, deleted, failed, dryRun);
        });

        Settings.VacuumDatabaseRequested += () => RunMaintenanceTask("VacuumDatabase", false, async () =>
        {
            var (before, after) = await Task.Run(() => _library.VacuumDatabase());
            return () => Settings.ReportVacuumComplete(before, after);
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

        Settings.RepairPathsRequested += () => RunMaintenanceTask("RepairPaths", false, async () =>
        {
            var (total, missing, removed) = await Task.Run(() => _library.RepairPaths(removeDeadEntries: true));
            return () => { Library.Refresh(); Settings.ReportRepairPathsComplete(total, missing, removed); };
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
                        Title = "Select Database Backup (.db)",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("SQLite Backup") { Patterns = new[] { "*.db" } } }
                    });
                    if (files.Count > 0) selectedFile = files[0].Path.LocalPath;
                }
            });

            if (string.IsNullOrEmpty(selectedFile)) return () => ToastService.Instance.Show("Restore cancelled.", ToastType.Info);

            string targetPath = NullWavePaths.DatabasePath;
            string tempPath = targetPath + ".restoring";
            File.Copy(selectedFile, tempPath, overwrite: true);
            return () => ToastService.Instance.Show("Backup staged! Please restart NullWave to apply.", ToastType.Success, 10000, scope: "maintenance");
        });
    }

    private void RunMaintenanceTask(string taskName, bool dryRun, Func<Task<Action>> taskFactory)
    {
        if (_isMaintenanceRunning) return;
        _isMaintenanceRunning = true;

        _ = Task.Run(async () =>
        {
            LiveNotification? activity = null;
            string prefix = dryRun ? "Previewing" : "Running";
            
            await Dispatcher.UIThread.InvokeAsync(() =>
                activity = ToastService.Instance.StartLiveActivity($"{prefix} {taskName}", "Processing...", isIndeterminate: true, scope: "maintenance"));

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
                    ToastService.Instance.CompleteLiveActivity(activity, $"{taskName} failed.", finalType: ToastType.Error);
                    _isMaintenanceRunning = false;
                });
            }
        });
    }
}
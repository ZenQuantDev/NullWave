using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace NullWave.Services;

public sealed record OrphanSweepResult(
    int Scanned,
    int Orphaned,
    int Moved,
    int Failed,
    bool GuardRefused,
    bool Cancelled,
    bool UsedFallback);

public static class OrphanSweepCoordinator
{
    public static async Task<OrphanSweepResult> PreviewAsync(
        LibraryService library,
        string? configuredDirectory,
        string defaultDirectory)
    {
        var (directory, usedFallback) = ResolveDirectory(configuredDirectory, defaultDirectory);
        var (scanned, orphaned, _, failed) = await Task.Run(
            () => library.SweepOrphanedFiles(directory, dryRun: true));

        return new OrphanSweepResult(scanned, orphaned, 0, failed, orphaned == -1, false, usedFallback);
    }

    public static async Task<OrphanSweepResult> SweepAsync(
        LibraryService library,
        string? configuredDirectory,
        string defaultDirectory,
        Func<int, int, Task<bool>> confirm)
    {
        var preview = await PreviewAsync(library, configuredDirectory, defaultDirectory);
        if (preview.GuardRefused || preview.Orphaned == 0)
            return preview;

        if (!await confirm(preview.Scanned, preview.Orphaned))
            return preview with { Cancelled = true };

        var (directory, usedFallback) = ResolveDirectory(configuredDirectory, defaultDirectory);
        var (scanned, orphaned, moved, failed) = await Task.Run(
            () => library.SweepOrphanedFiles(directory, dryRun: false));

        return new OrphanSweepResult(
            scanned, orphaned, moved, failed, orphaned == -1, false,
            usedFallback || preview.UsedFallback);
    }

    private static (string Directory, bool UsedFallback) ResolveDirectory(
        string? configuredDirectory,
        string defaultDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredDirectory))
            return (EnsureWritable(defaultDirectory), false);

        if (IsWritable(configuredDirectory))
            return (configuredDirectory, false);

        Log.Warning(
            "[OrphanSweep] Configured downloads directory is missing or not writable; falling back to {Directory}",
            defaultDirectory);
        return (EnsureWritable(defaultDirectory), true);
    }

    private static string EnsureWritable(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!IsWritable(directory))
            throw new IOException($"The default downloads directory is not writable: {directory}");
        return directory;
    }

    private static bool IsWritable(string directory)
    {
        if (!Directory.Exists(directory))
            return false;

        var probe = Path.Combine(directory, $".nullwave-write-probe-{Guid.NewGuid():N}");
        try
        {
            using var stream = new FileStream(
                probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (IOException ex)
        {
            Log.Warning(ex, "[OrphanSweep] Cannot write to downloads directory {Directory}", directory);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Log.Warning(ex, "[OrphanSweep] Cannot write to downloads directory {Directory}", directory);
            return false;
        }
    }
}

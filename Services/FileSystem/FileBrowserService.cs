using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models.FileSystem;
using Serilog;

namespace NullWave.Services.FileSystem;

public sealed record FolderListing(IReadOnlyList<FileSystemNode> Nodes, bool AccessDenied);
public sealed record TagInfo(string? Title, string? Artist, string? Album, string? Duration, string? Details);

public class FileBrowserService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma", ".opus"
    };

    public static string[] GetDriveNames() =>
        DriveInfo.GetDrives().Select(d => d.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<List<FileSystemNode>> GetRootsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var roots = new List<FileSystemNode>();
            var L = LocalizationService.Instance;

            foreach (var drive in DriveInfo.GetDrives())
            {
                ct.ThrowIfCancellationRequested();
                bool ready;
                try { ready = drive.IsReady; } catch { continue; }
                if (!ready) continue;

                long free = 0, total = 0;
                try { free = drive.AvailableFreeSpace; total = drive.TotalSize; } catch { }

                roots.Add(new FileSystemNode
                {
                    Path = drive.RootDirectory.FullName,
                    Name = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? drive.Name.TrimEnd('\\', '/')
                        : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\', '/')})",
                    Type = FileSystemNodeType.Drive,
                    DriveType = drive.DriveType.ToString(),
                    FreeSpace = free,
                    TotalSpace = total,
                    LastModified = DateTime.UtcNow
                });
            }

            var places = new[]
            {
                (Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), L["Files_Places_Music"]),
                (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), L["Files_Places_Home"]),
                (Environment.GetFolderPath(Environment.SpecialFolder.Desktop), L["Files_Places_Desktop"]),
                (Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), L["Files_Places_Documents"]),
            };

            foreach (var (path, name) in places)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) continue;
                roots.Add(new FileSystemNode
                {
                    Path = path,
                    Name = name,
                    Type = FileSystemNodeType.Folder,
                    LastModified = Directory.GetLastWriteTimeUtc(path)
                });
            }

            return roots;
        }, ct);
    }

    public Task<FolderListing> ListFolderAsync(string folderPath, bool showHidden, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                using var probe = Directory.EnumerateFileSystemEntries(folderPath).GetEnumerator();
                probe.MoveNext();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Log.Warning(ex, "[FileBrowser] Cannot open {Path}", folderPath);
                return new FolderListing(Array.Empty<FileSystemNode>(), true);
            }

            var nodes = new List<FileSystemNode>();
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = showHidden ? FileAttributes.System : FileAttributes.Hidden | FileAttributes.System
            };

            try
            {
                foreach (var dir in Directory.EnumerateDirectories(folderPath, "*", opts))
                {
                    ct.ThrowIfCancellationRequested();
                    var info = new DirectoryInfo(dir);
                    nodes.Add(new FileSystemNode
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Type = FileSystemNodeType.Folder,
                        LastModified = info.LastWriteTimeUtc
                    });
                }

                foreach (var file in Directory.EnumerateFiles(folderPath, "*", opts))
                {
                    ct.ThrowIfCancellationRequested();
                    var ext = Path.GetExtension(file);
                    if (!AudioExtensions.Contains(ext)) continue;
                    var info = new FileInfo(file);
                    nodes.Add(new FileSystemNode
                    {
                        Path = info.FullName,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Type = FileSystemNodeType.AudioFile,
                        Size = info.Length,
                        LastModified = info.LastWriteTimeUtc
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[FileBrowser] Enumeration failed for {Path}", folderPath);
            }

            var sorted = nodes
                .OrderByDescending(n => n.Type == FileSystemNodeType.Folder)
                .ThenBy(n => n.Name, NaturalComparer.Instance)
                .ToList();

            return new FolderListing(sorted, false);
        }, ct);
    }

    public TagInfo? ReadTags(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path, TagLib.ReadStyle.Average);
            var d = f.Properties.Duration;
            var details = string.Join(" · ", new[]
            {
                f.Properties.Codecs.FirstOrDefault()?.Description,
                f.Properties.AudioBitrate > 0 ? $"{f.Properties.AudioBitrate} kbps" : null,
                f.Properties.AudioSampleRate > 0 ? $"{f.Properties.AudioSampleRate / 1000.0:0.#} kHz" : null
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            return new TagInfo(
                Clean(f.Tag.Title),
                Clean(f.Tag.FirstPerformer),
                Clean(f.Tag.Album),
                d > TimeSpan.Zero ? FormatDuration(d) : null,
                details.Length > 0 ? details : null);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[FileBrowser] Tag read failed for {Path}", path);
            return null;
        }
    }

    public byte[]? ReadCoverArt(string path)
    {
        try
        {
            using var f = TagLib.File.Create(path, TagLib.ReadStyle.None);
            return f.Tag.Pictures.FirstOrDefault()?.Data.Data;
        }
        catch { return null; }
    }

    public Task<bool> IsPathAccessibleAsync(string path, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try { return Directory.Exists(path) || File.Exists(path); }
            catch { return false; }
        }, ct);
    }

    public static string? NormalizeEnteredPath(string text)
    {
        var expanded = text.Trim();
        if (expanded.Length == 0) return null;
        if (expanded.StartsWith("~"))
            expanded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                expanded[1..].TrimStart('\\', '/'));
        try
        {
            expanded = Environment.ExpandEnvironmentVariables(expanded);
            var full = Path.GetFullPath(expanded);
            if (File.Exists(full)) full = Path.GetDirectoryName(full)!;
            return Directory.Exists(full) ? full : null;
        }
        catch { return null; }
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public static string FormatDuration(TimeSpan d) => d.TotalHours >= 1
        ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
        : $"{d.Minutes}:{d.Seconds:00}";
}

public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null) return string.CompareOrdinal(x, y);
        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsAsciiDigit(x[ix]) && char.IsAsciiDigit(y[iy]))
            {
                int nx = ix; while (nx < x.Length && char.IsAsciiDigit(x[nx])) nx++;
                int ny = iy; while (ny < y.Length && char.IsAsciiDigit(y[ny])) ny++;
                var rx = x.AsSpan(ix, nx - ix).TrimStart('0');
                var ry = y.AsSpan(iy, ny - iy).TrimStart('0');
                if (rx.Length != ry.Length) return rx.Length.CompareTo(ry.Length);
                int c = rx.SequenceCompareTo(ry);
                if (c != 0) return c;
                ix = nx; iy = ny;
            }
            else
            {
                int c = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                if (c != 0) return c;
                ix++; iy++;
            }
        }
        return (x.Length - ix).CompareTo(y.Length - iy);
    }
}
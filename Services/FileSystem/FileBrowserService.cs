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
public sealed record TagInfo(string? Title, string? Artist, string? Album, string? Duration, string? Details, bool AccessDenied = false);
public class FileBrowserService
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma", ".opus"
    };

    public static string[] GetDriveNames() =>
        OperatingSystem.IsLinux()
            ? LinuxMountRoots().Select(n => n.Path).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : DriveInfo.GetDrives().Select(d => d.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

    public Task<List<FileSystemNode>> GetRootsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var roots = new List<FileSystemNode>();
            var L = LocalizationService.Instance;

            foreach (var node in EnumerateRootDrives(ct))
            {
                roots.Add(node);
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
                    LastModified = Directory.GetLastWriteTimeUtc(path),
                    IsReparsePoint = GetIsReparsePoint(path)
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
                Log.Debug("[FileBrowser] Cannot open {Path}: {Message}", folderPath, ex.Message);
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
                        LastModified = info.LastWriteTimeUtc,
                        IsReparsePoint = (info.Attributes & FileAttributes.ReparsePoint) != 0
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
                        LastModified = info.LastWriteTimeUtc,
                        IsReparsePoint = (info.Attributes & FileAttributes.ReparsePoint) != 0
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[FileBrowser] Enumeration failed for {Path}: {Message}", folderPath, ex.Message);
            }

            List<FileSystemNode> sorted;
            try
            {
                sorted = nodes
                    .OrderBy(n => n.IsFolderLike ? 0 : 1)
                    .ThenBy(n => n.Name, NaturalComparer.Instance)
                    .ToList();
            }
            catch (ArgumentException ex)
            {
                Log.Warning(ex, "[FileBrowser] Natural sort failed for {Path}; falling back to ordinal sort", folderPath);

                sorted = nodes
                    .OrderBy(n => n.IsFolderLike ? 0 : 1)
                    .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return new FolderListing(sorted, false);
        }, ct);
    }

    public TagInfo? ReadTags(string path)
    {
        try
        {
            using var fs = OpenRead(path);
            using var f = TagLib.File.Create(
                new StreamAbstraction(Path.GetFileName(path), fs),
                TagLib.ReadStyle.Average);

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
        catch (UnauthorizedAccessException ex)
        {
            string reason;
            try
            {
                var attrs = File.GetAttributes(path);
                var L = NullWave.Services.LocalizationService.Instance;
                reason = (attrs & FileAttributes.Encrypted) != 0
                    ? L["Files_Encrypted"]
                    : L["Files_AccessDeniedDetail"];
            }
                catch { reason = NullWave.Services.LocalizationService.Instance["Files_AccessDeniedDetail"]; }

            Log.Debug("[FileBrowser] Tag read denied for {Path}: {Reason} ({Message})", path, reason, ex.Message);
            // Non-null + AccessDenied so the row keeps its filename as title but shows a lock.
            return new TagInfo(null, null, null, null, reason, AccessDenied: true);
        }
        catch (Exception ex)
        {
            Log.Debug("[FileBrowser] Tag read failed for {Path}: {Message}", path, ex.Message);
            return null;
        }
    }

    public byte[]? ReadCoverArt(string path)
    {
        try
        {
            using var fs = OpenRead(path);
            using var f = TagLib.File.Create(
                new StreamAbstraction(Path.GetFileName(path), fs),
                TagLib.ReadStyle.None);

            return f.Tag.Pictures.FirstOrDefault()?.Data.Data;
        }
        catch (Exception ex)
        {
            Log.Debug("[FileBrowser] Cover art read failed for {Path}: {Message}", path, ex.Message);
            return null;
        }
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

    private static IEnumerable<FileSystemNode> EnumerateRootDrives(CancellationToken ct)
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var node in LinuxMountRoots())
            {
                ct.ThrowIfCancellationRequested();
                yield return node;
            }
            yield break;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            ct.ThrowIfCancellationRequested();
            bool ready;
            try { ready = drive.IsReady; } catch { continue; }
            if (!ready) continue;

            long free = 0, total = 0;
            try { free = drive.AvailableFreeSpace; total = drive.TotalSize; } catch { }

            yield return new FileSystemNode
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
            };
        }
    }

    // Linux: /proc/mounts lists every pseudo-filesystem (sysfs, cgroupfs, tmpfs, gvfs/portals
    // under /run), which floods the Places row. Keep only real storage mounts, capped.
    private static readonly HashSet<string> LinuxRealFilesystems = new(StringComparer.Ordinal)
    {
        "ext2", "ext3", "ext4", "btrfs", "xfs", "f2fs", "jfs", "reiserfs", "erofs",
        "overlay", "overlayfs",
        "vfat", "exfat", "ntfs", "ntfs3", "udf", "iso9666",
        "zfs", "nfs", "nfs4", "cifs", "smbfs", "fuseblk", "fuse.sshfs"
    };

    private static readonly string[] LinuxSkipMountPrefixes =
    {
        "/proc", "/sys", "/dev", "/run", "/snap", "/var/lib/snapd", "/var/lib/docker",
        "/var/lib/containers", "/var/lib/libvirt", "/var/tmp", "/tmp", "/boot/efi", "/efi"
    };

    private const int LinuxMaxRoots = 12;

    private static IEnumerable<FileSystemNode> LinuxMountRoots()
    {
        if (!File.Exists("/proc/mounts")) yield break;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in File.ReadLines("/proc/mounts"))
        {
            var parts = raw.Split(' ', '\t');
            if (parts.Length < 3) continue;
            if (!LinuxRealFilesystems.Contains(parts[2])) continue;

            var mp = UnescapeMountPath(parts[1]);
            if (mp != "/" && LinuxSkipMountPrefixes.Any(p => mp == p || mp.StartsWith(p + "/", StringComparison.Ordinal))) continue;
            if (!seen.Add(mp)) continue;
            if (seen.Count > LinuxMaxRoots) yield break;

            long free = 0, total = 0;
            try
            {
                var di = new DriveInfo(mp);
                if (!di.IsReady) continue;
                free = di.AvailableFreeSpace;
                total = di.TotalSize;
            }
            catch { continue; }

            yield return new FileSystemNode
            {
                Path = mp,
                Name = mp == "/" ? "/" : Path.GetFileName(mp.TrimEnd('/')),
                Type = FileSystemNodeType.Drive,
                DriveType = parts[2],
                FreeSpace = free,
                TotalSpace = total,
                LastModified = DateTime.UtcNow
            };
        }
    }

    private static string UnescapeMountPath(string s) =>
        s.Replace("\\040", " ").Replace("\\011", "\t").Replace("\\012", "\n").Replace("\\134", "\\");
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    public static string FormatDuration(TimeSpan d) => d.TotalHours >= 1
        ? $"{(int)d.TotalHours}:{d.Minutes:00}:{d.Seconds:00}"
        : $"{d.Minutes}:{d.Seconds:00}";

    private static FileStream OpenRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private sealed class StreamAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _stream;

        public StreamAbstraction(string name, Stream stream)
        {
            Name = name;
            _stream = stream;
        }

        public string Name { get; }
        public Stream ReadStream => _stream;
        public Stream WriteStream => throw new NotSupportedException();
        public void CloseStream(Stream stream) { /* outer using disposes the FileStream */ }
    }

    private static bool GetIsReparsePoint(string path)
    {
        try
        { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return false; }
    }

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
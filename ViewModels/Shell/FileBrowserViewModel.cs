using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NullWave.Helpers.Logging;
using NullWave.Models;
using NullWave.Models.FileSystem;
using NullWave.Services;
using NullWave.Services.FileSystem;
using Serilog;

namespace NullWave.ViewModels;

public sealed record BreadcrumbSegment(string Name, string Path, bool IsLast);

public partial class FileBrowserViewModel : ObservableObject
{
    private readonly FileBrowserService _browser;
    private readonly LibraryService _library;
    private readonly PreferencesService _preferences;

    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _enrichCts;
    private HashSet<string> _libraryPaths = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;
    private DispatcherTimer? _driveWatch;
    private string[] _lastDriveNames = Array.Empty<string>();

    [ObservableProperty] private ObservableCollection<FileSystemNode> _places = new();
    [ObservableProperty] private ObservableCollection<FileSystemNode> _currentNodes = new();
    [ObservableProperty] private ObservableCollection<BreadcrumbSegment> _segments = new();
    [ObservableProperty] private FileSystemNode? _selectedNode;
    [ObservableProperty] private string _currentPath = string.Empty;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _accessDenied;
    [ObservableProperty] private bool _loadFailed;
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private bool _canGoForward;
    [ObservableProperty] private bool _canGoUp;
    [ObservableProperty] private bool _isEditingPath;
    [ObservableProperty] private string _pathDraft = string.Empty;
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private bool _isCurrentPinned;

    public FileBrowserViewModel(FileBrowserService browser, LibraryService library, PreferencesService preferences)
    {
        _browser = browser;
        _library = library;
        _preferences = preferences;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized) return;
        _initialized = true;

        var roots = await _browser.GetRootsAsync();
        foreach (var pinned in LoadPinnedFolders())
            if (roots.All(r => r.Path != pinned.Path)) roots.Add(pinned);

        var newPlaces = new ObservableCollection<FileSystemNode>(roots);
        foreach (var p in newPlaces)
            p.IsCurrentPlace = string.Equals(p.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);
        Places = newPlaces;

        var last = _preferences.Current.LastBrowsedPath;
        var start = !string.IsNullOrWhiteSpace(last) && await _browser.IsPathAccessibleAsync(last)
            ? last
            : roots.FirstOrDefault(r => r.Type == FileSystemNodeType.Folder && r.DriveType != "pinned")?.Path
              ?? roots.FirstOrDefault()?.Path;

        if (start != null) await NavigateToPathAsync(start);
        else await ShowThisPcAsync();
    }

    // ---------- navigation ----------

    [RelayCommand]
    private Task ShowThisPcAsync() => NavigateCoreAsync(string.Empty, pushHistory: true);

    [RelayCommand]
    private Task NavigateToAsync(FileSystemNode? node)
        => node == null ? ShowThisPcAsync() : NavigateToPathAsync(node.Path);

    public Task NavigateToPathAsync(string path) => NavigateCoreAsync(path, pushHistory: true);

    private async Task NavigateCoreAsync(string path, bool pushHistory)
    {
        _loadCts?.Cancel();
        _enrichCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        var ct = cts.Token;

        if (pushHistory && CurrentPath != path)
        {
            _back.Push(CurrentPath);
            _forward.Clear();
        }

        CurrentPath = path;
        SelectedNode = null;
        IsLoading = true;
        IsEmpty = false;
        AccessDenied = false;
        LoadFailed = false;
        UpdateNavFlags();
        RebuildSegments();
        UpdatePinState();

        try
        {
            FolderListing listing;
            if (path.Length == 0)
            {
                var roots = Places.ToList();
                listing = new FolderListing(roots, false);
            }
            else
            {
                if (!await _browser.IsPathAccessibleAsync(path, ct))
                {
                    LoadFailed = true;
                    CurrentNodes = new ObservableCollection<FileSystemNode>();
                    IsLoading = false;
                    return;
                }
                listing = await _browser.ListFolderAsync(path, ShowHidden, ct);
            }

            if (ct.IsCancellationRequested) return;

            RefreshLibraryPaths();
            foreach (var n in listing.Nodes) n.IsInLibrary = n.IsAudio && _libraryPaths.Contains(n.Path);

            CurrentNodes = new ObservableCollection<FileSystemNode>(listing.Nodes);
            AccessDenied = listing.AccessDenied;
            IsEmpty = !listing.AccessDenied && listing.Nodes.Count == 0;

            if (!string.Equals(_preferences.Current.LastBrowsedPath, path, StringComparison.Ordinal))
                _preferences.Update(p => p.LastBrowsedPath = path);

            StartEnrichment(listing.Nodes.Where(n => n.IsAudio).ToList(), ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "[FileBrowser] Navigation failed for {Path}", path);
            LoadFailed = true;
        }
        finally
        {
            if (!ct.IsCancellationRequested) IsLoading = false;
        }
    }

    private void StartEnrichment(List<FileSystemNode> audioNodes, CancellationToken token)
    {
        if (audioNodes.Count == 0) return;
        _enrichCts?.Cancel();
        var enrichCts = _enrichCts = CancellationTokenSource.CreateLinkedTokenSource(token);

        _ = Task.Run(async () =>
        {
            try
            {
                await Parallel.ForEachAsync(audioNodes,
                    new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = enrichCts.Token },
                    async (node, tk) =>
                    {
                        var info = await Task.Run(() => _browser.ReadTags(node.Path), tk);
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (info == null) { node.MetadataFailed = true; return; }
                            node.Title = info.Title ?? node.Name;
                            node.Artist = info.Artist;
                            node.Album = info.Album;
                            node.Duration = info.Duration;
                            node.Details = info.Details;
                        });
                    });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Warning(ex, "[FileBrowser] Enrichment failed"); }
        }, enrichCts.Token);
    }

    // ---------- drive watch ----------

    public void StartDriveWatch()
    {
        if (_driveWatch != null) return;
        _lastDriveNames = FileBrowserService.GetDriveNames();
        _driveWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _driveWatch.Tick += async (_, _) => await RefreshDrivesAsync();
        _driveWatch.Start();
    }

    public void StopDriveWatch()
    {
        _driveWatch?.Stop();
        _driveWatch = null;
    }

    private async Task RefreshDrivesAsync()
    {
        try
        {
            var names = FileBrowserService.GetDriveNames();
            if (names.SequenceEqual(_lastDriveNames, StringComparer.OrdinalIgnoreCase)) return;
            _lastDriveNames = names;

            var fresh = (await _browser.GetRootsAsync()).Where(n => n.Type == FileSystemNodeType.Drive).ToList();
            var current = Places.Where(p => p.Type == FileSystemNodeType.Drive).ToList();
            var (added, removedPaths) = ComputeDriveDiff(current, fresh);

            foreach (var path in removedPaths)
            {
                var node = Places.FirstOrDefault(p => p.Type == FileSystemNodeType.Drive && p.Path == path);
                if (node != null) Places.Remove(node);
            }
            int at = Places.Count(p => p.Type == FileSystemNodeType.Drive);
            foreach (var d in added) Places.Insert(at++, d);
            foreach (var p in Places)
                p.IsCurrentPlace = string.Equals(p.Path, CurrentPath, StringComparison.OrdinalIgnoreCase);

            if (removedPaths.Any(r => CurrentPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                ToastService.Instance.Show(L("Files_DriveRemoved"), ToastType.Warning, scope: "files");
            if (CurrentPath.Length == 0 || removedPaths.Any(r => CurrentPath.StartsWith(r, StringComparison.OrdinalIgnoreCase)))
                await ShowThisPcAsync();
        }
        catch (Exception ex) { Log.Debug(ex, "[FileBrowser] Drive refresh failed"); }
    }

    public static (List<FileSystemNode> Added, List<string> Removed) ComputeDriveDiff(
        IReadOnlyList<FileSystemNode> current, IReadOnlyList<FileSystemNode> fresh)
    {
        var added = fresh.Where(f => current.All(c => c.Path != f.Path)).ToList();
        var removed = current.Where(c => c.Type == FileSystemNodeType.Drive &&
                                         fresh.All(f => f.Path != c.Path))
                             .Select(c => c.Path).ToList();
        return (added, removed);
    }

    // ---------- nav commands ----------

    [RelayCommand]
    private Task GoBackAsync()
    {
        if (_back.Count == 0) return Task.CompletedTask;
        var target = _back.Pop();
        _forward.Push(CurrentPath);
        return NavigateCoreAsync(target, pushHistory: false);
    }

    [RelayCommand]
    private Task GoForwardAsync()
    {
        if (_forward.Count == 0) return Task.CompletedTask;
        var target = _forward.Pop();
        _back.Push(CurrentPath);
        return NavigateCoreAsync(target, pushHistory: false);
    }

    [RelayCommand]
    private Task GoUpAsync()
    {
        var p = ParentOf(CurrentPath);
        return p == null ? Task.CompletedTask : NavigateToPathAsync(p);
    }

    [RelayCommand]
    private Task RefreshAsync() => NavigateCoreAsync(CurrentPath, pushHistory: false);

    [RelayCommand]
    private Task ToggleShowHiddenAsync()
    {
        ShowHidden = !ShowHidden;
        return RefreshAsync();
    }

    // ---------- path editing ----------

    [RelayCommand]
    private void StartEditPath()
    {
        PathDraft = CurrentPath;
        IsEditingPath = true;
    }

    [RelayCommand]
    private Task CommitEditPathAsync()
    {
        IsEditingPath = false;
        var normalized = FileBrowserService.NormalizeEnteredPath(PathDraft);
        return normalized == null ? Task.CompletedTask : NavigateToPathAsync(normalized);
    }

    [RelayCommand]
    private void CancelEditPath() => IsEditingPath = false;

    // ---------- opening & playback ----------

    [RelayCommand]
    private void OpenFile(FileSystemNode? node)
    {
        if (node == null) return;
        if (node.IsAudio) { PlayFile(node); return; }
        _ = NavigateToPathAsync(node.Path);
    }

    [RelayCommand]
    private void PlayFile(FileSystemNode? node)
    {
        if (node is not { IsAudio: true }) return;
        PlayTrackRequested?.Invoke(FindInLibrary(node) ?? BuildTrack(node));
    }

    [RelayCommand]
    private void AddToLibrary(FileSystemNode? node)
    {
        if (node is not { IsAudio: true }) return;
        if (FindInLibrary(node) != null)
        {
            node.IsInLibrary = true;
            ToastService.Instance.Show(L("Files_AlreadyInLibrary"), ToastType.Info, scope: "files");
            return;
        }
        var track = BuildTrack(node);
        if (_library.IsDuplicate(track))
        {
            ToastService.Instance.Show(L("Files_DuplicateInLibrary"), ToastType.Info, scope: "files");
            return;
        }
        if (!_library.Add(track))
        {
            ToastService.Instance.Show(L("Files_AddFailed"), ToastType.Warning, scope: "files");
            return;
        }
        node.IsInLibrary = true;
        _libraryPaths.Add(node.Path);
        NullActionLogger.User("AddToLibrary", track.Id.ToString(), nameof(FileBrowserViewModel));
        ToastService.Instance.Show(string.Format(L("Files_AddedToLibrary"), track.Title), ToastType.Success, scope: "files");
    }

    [RelayCommand]
    private void AddToQueue(FileSystemNode? node)
    {
        if (node is not { IsAudio: true }) return;
        var track = FindInLibrary(node);
        if (track == null)
        {
            ToastService.Instance.Show(L("Files_QueueNeedsLibrary"), ToastType.Info, scope: "files");
            return;
        }
        _library.AddToQueue(track.Id);
        NullActionLogger.User("AddToQueue", track.Id.ToString(), nameof(FileBrowserViewModel));
        ToastService.Instance.Show(string.Format(L("Files_Queued"), track.Title), ToastType.Info, durationMs: 2500, scope: "queue-add");
    }

    // ---------- pins ----------

    [RelayCommand]
    private void TogglePinCurrentFolder()
    {
        if (CurrentPath.Length == 0) return;
        var existing = Places.FirstOrDefault(p => p.Type == FileSystemNodeType.Folder && p.Path == CurrentPath && p.DriveType == "pinned");
        if (existing != null)
        {
            Places.Remove(existing);
            SavePinnedFolders(Places.Where(p => p.DriveType == "pinned").Select(p => p.Path).ToList());
        }
        else
        {
            Places.Add(new FileSystemNode
            {
                Path = CurrentPath,
                Name = new DirectoryInfo(CurrentPath).Name,
                Type = FileSystemNodeType.Folder,
                DriveType = "pinned",
                LastModified = Directory.GetLastWriteTimeUtc(CurrentPath)
            });
            SavePinnedFolders(Places.Where(p => p.DriveType == "pinned").Select(p => p.Path).ToList());
        }
        UpdatePinState();
    }

    private void UpdatePinState()
        => IsCurrentPinned = CurrentPath.Length > 0 &&
           Places.Any(p => p.DriveType == "pinned" && p.Path == CurrentPath);

    private List<FileSystemNode> LoadPinnedFolders()
    {
        var list = new List<FileSystemNode>();
        foreach (var path in _preferences.Current.PinnedFolders ?? new List<string>())
        {
            if (!Directory.Exists(path)) continue;
            list.Add(new FileSystemNode
            {
                Path = path,
                Name = new DirectoryInfo(path).Name,
                Type = FileSystemNodeType.Folder,
                DriveType = "pinned",
                LastModified = Directory.GetLastWriteTimeUtc(path)
            });
        }
        return list;
    }

    private void SavePinnedFolders(List<string> paths)
        => _preferences.Update(p => p.PinnedFolders = paths);

    // ---------- helpers ----------

    private static string L(string key) => LocalizationService.Instance[key];

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private Track? FindInLibrary(FileSystemNode n) =>
        _library.GetAll().FirstOrDefault(t => string.Equals(t.FilePath, n.Path, PathComparison));

    private Track BuildTrack(FileSystemNode node) => new()
    {
        Id = StableIdForPath(node.Path),
        Title = node.Title ?? node.Name,
        Artist = node.Artist ?? "Unknown Artist",
        Album = node.Album ?? string.Empty,
        FilePath = node.Path,
        Source = TrackSource.Local,
        DateAdded = DateTime.UtcNow,
        MediaType = MediaType.Music
    };

    internal static Guid StableIdForPath(string path)
    {
        var n = path.Replace('\\', '/').TrimEnd('/');
        if (OperatingSystem.IsWindows()) n = n.ToLowerInvariant();
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("filebrowser:" + n));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    internal static string? ParentOf(string path)
    {
        if (path.Length == 0) return null;
        var root = Path.GetPathRoot(path);
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase)) return string.Empty;
        return Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
    }

    private void RefreshLibraryPaths()
        => _libraryPaths = _library.GetAll()
            .Where(t => !string.IsNullOrEmpty(t.FilePath))
            .Select(t => t.FilePath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void UpdateNavFlags()
    {
        CanGoBack = _back.Count > 0;
        CanGoForward = _forward.Count > 0;
        CanGoUp = CurrentPath.Length > 0;
    }

    private void RebuildSegments()
    {
        Segments.Clear();
        if (CurrentPath.Length == 0)
        {
            Segments.Add(new BreadcrumbSegment(L("Files_ThisPc"), string.Empty, true));
            return;
        }
        var root = Path.GetPathRoot(CurrentPath) ?? string.Empty;
        var rootName = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parts = CurrentPath[root.Length..]
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);

        Segments.Add(new BreadcrumbSegment(rootName.Length > 0 ? rootName : root, root, parts.Length == 0));
        var acc = root;
        for (int i = 0; i < parts.Length; i++)
        {
            acc = Path.Combine(acc, parts[i]);
            Segments.Add(new BreadcrumbSegment(parts[i], acc, i == parts.Length - 1));
        }
    }

    partial void OnCurrentPathChanged(string value)
    {
        foreach (var place in Places)
            place.IsCurrentPlace = string.Equals(place.Path, value, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private Task NavigateSegmentAsync(BreadcrumbSegment? segment)
        => segment == null ? Task.CompletedTask : NavigateToPathAsync(segment.Path);

    public event Action<Track>? PlayTrackRequested;
}
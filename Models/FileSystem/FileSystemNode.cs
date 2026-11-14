using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NullWave.Models.FileSystem;

public enum FileSystemNodeType
{
    ThisPc,
    Drive,
    Folder,
    AudioFile
}

/// <summary>
/// One row in the file browser. Metadata fields are observable so the background
/// tag-reader can fill them in after the listing appears (two-phase load).
/// </summary>
public sealed class FileSystemNode : ObservableObject
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public FileSystemNodeType Type { get; init; }
    public DateTime LastModified { get; init; }

    // Drive info
    public string? DriveType { get; init; }
    public long? FreeSpace { get; init; }
    public long? TotalSpace { get; init; }

    // File info (known immediately)
    private long _size;
    public long Size { get => _size; set => SetProperty(ref _size, value); }

    // Audio metadata (filled by phase 2)
    private string? _title;
    public string? Title { get => _title; set => SetProperty(ref _title, value); }

    private string? _artist;
    public string? Artist { get => _artist; set => SetProperty(ref _artist, value); }

    private string? _album;
    public string? Album { get => _album; set => SetProperty(ref _album, value); }

    private string? _duration;
    public string? Duration { get => _duration; set => SetProperty(ref _duration, value); }

    private bool _isInLibrary;
    public bool IsInLibrary { get => _isInLibrary; set => SetProperty(ref _isInLibrary, value); }

    private bool _metadataFailed;
    public bool MetadataFailed { get => _metadataFailed; set => SetProperty(ref _metadataFailed, value); }

    public bool IsAudio => Type == FileSystemNodeType.AudioFile;
    public bool IsFolderLike => Type is FileSystemNodeType.Folder or FileSystemNodeType.Drive;
    private bool _isCurrentPlace;
    public bool IsCurrentPlace { get => _isCurrentPlace; set => SetProperty(ref _isCurrentPlace, value); }
    private string? _details;
    public string? Details
    {
        get => _details;
        set => SetProperty(ref _details, value);
    }
}
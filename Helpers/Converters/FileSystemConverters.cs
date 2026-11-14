using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Material.Icons;
using NullWave.Models.FileSystem;

namespace NullWave.Helpers.Converters;

/// <summary>Maps a file system node type to its sidebar/row icon.</summary>
public class NodeTypeToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            FileSystemNodeType.Drive => MaterialIconKind.Harddisk,
            FileSystemNodeType.Folder => MaterialIconKind.Folder,
            FileSystemNodeType.AudioFile => MaterialIconKind.MusicNote,
            _ => MaterialIconKind.File
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a byte count as a human-readable file size.</summary>
public class FileSizeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes) return string.Empty;
        
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int unit = 0;
        
        while (size >= 1024 && unit < units.Length - 1) 
        { 
            size /= 1024; 
            unit++; 
        }
        
        return $"{size:0.#} {units[unit]}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
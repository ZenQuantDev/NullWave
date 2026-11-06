using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Unified filename generator for NullWave. Enforces consistent naming across downloads,
/// sync operations, and exports. Handles cross-platform compatibility including:
/// - Invalid character removal
/// - 255-byte UTF-8 filename limit (critical for CJK/Cyrillic)
/// - NullWave ID embedding for reliable file recovery
/// - Reserved name handling (Windows: CON, PRN, etc.)
/// </summary>
public static class FileNamer
{
    // Maximum filename length in BYTES (not characters) for cross-platform compatibility
    private const int MaxFilenameBytes = 255;
    
    // Windows reserved names
    private static readonly string[] ReservedNames = {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    /// Generates a standardized filename: {Artist} - {Title} [nw-{id8}].{ext}
    /// All invalid characters are removed, and the result is guaranteed to fit within
    /// the 255-byte UTF-8 limit while preserving the NullWave ID suffix.
    /// </summary>
    public static string Generate(string artist, string title, string trackId, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException("Extension cannot be null or empty", nameof(extension));
        }

        // Normalize extension
        if (!extension.StartsWith("."))
            extension = "." + extension;

        // Extract 8-char ID prefix for embedding
        var id8 = ExtractId8(trackId);

        // Sanitize artist and title
        var cleanArtist = Sanitize(artist ?? "Unknown Artist");
        var cleanTitle = Sanitize(title ?? "Unknown Title");

        // Build the suffix that must always be preserved: [nw-{id8}]{ext}
        var suffix = $" [nw-{id8}]{extension}";
        var suffixBytes = Encoding.UTF8.GetByteCount(suffix);

        // Calculate available bytes for "{Artist} - {Title}"
        var availableBytes = MaxFilenameBytes - suffixBytes;
        
        if (availableBytes < 20) // Sanity check
        {
            Log.Warning("[FileNamer] Suffix too long, truncating ID");
            suffix = $" [nw]{extension}";
            suffixBytes = Encoding.UTF8.GetByteCount(suffix);
            availableBytes = MaxFilenameBytes - suffixBytes;
        }

        // Build the base name with truncation if needed
        var baseName = $"{cleanArtist} - {cleanTitle}";
        baseName = TruncateToByteLimit(baseName, availableBytes);

        // Handle Windows reserved names
        if (IsReservedName(baseName))
        {
            baseName = "_" + baseName;
        }

        var result = baseName + suffix;
        
        // Final validation
        if (Encoding.UTF8.GetByteCount(result) > MaxFilenameBytes)
        {
            Log.Warning("[FileNamer] Generated filename exceeds limit, forcing truncation");
            result = TruncateToByteLimit(result, MaxFilenameBytes);
        }

        return result;
    }

    /// <summary>
    /// Extracts the first 8 characters of a track ID (GUID or string).
    /// Used for embedding in filenames for reliable recovery.
    /// </summary>
    private static string ExtractId8(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return "00000000";

        // If it's a GUID, format without hyphens
        if (Guid.TryParse(trackId, out var guid))
        {
            return guid.ToString("N")[..8];
        }

        // Otherwise take first 8 chars
        var cleaned = Regex.Replace(trackId, @"[^a-zA-Z0-9]", "");
        return cleaned.Length >= 8 ? cleaned[..8] : cleaned.PadRight(8, '0');
    }

    /// <summary>
    /// Removes all invalid filename characters and collapses whitespace.
    /// </summary>
    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Unknown";

        // Remove invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(input.Where(c => !invalidChars.Contains(c)).ToArray());

        // Replace multiple spaces with single space
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();

        // Remove leading/trailing dots and spaces (Windows restriction)
        sanitized = sanitized.Trim('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }

    /// <summary>
    /// Truncates a string to fit within a byte limit while preserving UTF-8 character boundaries.
    /// Uses a binary search approach for efficiency.
    /// </summary>
    private static string TruncateToByteLimit(string input, int maxBytes)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var bytes = Encoding.UTF8.GetBytes(input);
        if (bytes.Length <= maxBytes)
            return input;

        // Binary search for the right character count
        int low = 0;
        int high = input.Length;
        int bestLength = 0;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            var testBytes = Encoding.UTF8.GetByteCount(input[..mid]);

            if (testBytes <= maxBytes)
            {
                bestLength = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        var result = input[..bestLength].TrimEnd();
        
        // Add ellipsis if we truncated
        if (bestLength < input.Length && bestLength > 3)
        {
            var ellipsisBytes = Encoding.UTF8.GetByteCount("...");
            if (maxBytes - Encoding.UTF8.GetByteCount(result) >= ellipsisBytes)
            {
                result += "...";
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if a filename is a Windows reserved name.
    /// </summary>
    private static bool IsReservedName(string name)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(name);
        return ReservedNames.Any(r => 
            string.Equals(r, nameWithoutExt, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Validates that a filename is safe for cross-platform use.
    /// Returns true if the filename is valid, false otherwise.
    /// </summary>
    public static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return false;

        // Check byte length
        if (Encoding.UTF8.GetByteCount(filename) > MaxFilenameBytes)
            return false;

        // Check for invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        if (filename.Any(c => invalidChars.Contains(c)))
            return false;

        // Check for reserved names
        if (IsReservedName(filename))
            return false;

        // Check for path traversal
        if (filename.Contains("..") || filename.Contains("/") || filename.Contains("\\"))
            return false;

        return true;
    }
}
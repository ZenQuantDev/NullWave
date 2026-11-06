using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Serilog;

namespace NullWave.Helpers;

/// <summary>
/// Unified filename generator for NullWave. Enforces consistent naming across downloads,
/// sync operations, and exports. Handles cross-platform compatibility including:
/// - Invalid character removal (uses a FIXED set so Linux exports stay Windows/FAT-safe)
/// - 255-byte UTF-8 filename limit (critical for CJK/Cyrillic)
/// - NullWave ID embedding for reliable file recovery via TryRelink
/// </summary>
public static class FileNamer
{
    private const int MaxFilenameBytes = 255;

    // FIXED invalid-char set: union of Windows invalid chars + Linux "/" + FAT/USB reserved.
    // Path.GetInvalidFileNameChars() on Linux only returns '/' and '\0', which would let
    // us write "Song: Title?.mp3" on Fedora and then fail on a USB stick or Windows share.
    private static readonly HashSet<char> InvalidChars = new(
        Path.GetInvalidFileNameChars()
            .Concat(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' })
            .Distinct());

    /// <summary>
    /// Generates: {Artist} - {Title} [nw-{id8}].{ext}
    /// </summary>
    public static string Generate(string artist, string title, string trackId, string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            throw new ArgumentException("Extension cannot be null or empty", nameof(extension));

        if (!extension.StartsWith("."))
            extension = "." + extension;

        var id8 = ExtractId8(trackId);
        var cleanArtist = Sanitize(artist ?? "Unknown Artist");
        var cleanTitle = Sanitize(title ?? "Unknown Title");

        // The suffix we MUST preserve so TryRelink can find the file later.
        var suffix = $" [nw-{id8}]{extension}";
        var suffixBytes = Encoding.UTF8.GetByteCount(suffix);

        var availableBytes = MaxFilenameBytes - suffixBytes;
        if (availableBytes < 20)
        {
            Log.Warning("[FileNamer] Suffix too long, truncating ID");
            suffix = $" [nw]{extension}";
            suffixBytes = Encoding.UTF8.GetByteCount(suffix);
            availableBytes = MaxFilenameBytes - suffixBytes;
        }

        var baseName = TruncateToByteLimit($"{cleanArtist} - {cleanTitle}", availableBytes);
        var result = baseName + suffix;

        if (Encoding.UTF8.GetByteCount(result) > MaxFilenameBytes)
        {
            Log.Warning("[FileNamer] Generated filename exceeds limit, forcing truncation");
            result = TruncateToByteLimit(result, MaxFilenameBytes - suffixBytes) + suffix;
        }

        return result;
    }

    private static string ExtractId8(string trackId)
    {
        if (string.IsNullOrWhiteSpace(trackId))
            return "00000000";

        if (Guid.TryParse(trackId, out var guid))
            return guid.ToString("N")[..8];

        var cleaned = Regex.Replace(trackId, @"[^a-zA-Z0-9]", "");
        return cleaned.Length >= 8 ? cleaned[..8] : cleaned.PadRight(8, '0');
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Unknown";

        // Use the FIXED cross-platform set, not Path.GetInvalidFileNameChars().
        var sanitized = new string(input.Where(c => !InvalidChars.Contains(c) && c >= 32).ToArray());
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        sanitized = sanitized.Trim('.', ' ');

        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }

    private static string TruncateToByteLimit(string input, int maxBytes)
    {
        if (string.IsNullOrEmpty(input)) return input;

        if (Encoding.UTF8.GetByteCount(input) <= maxBytes) return input;

        int low = 0, high = input.Length, bestLength = 0;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (Encoding.UTF8.GetByteCount(input.AsSpan(0, mid)) <= maxBytes)
            {
                bestLength = mid;
                low = mid + 1;
            }
            else high = mid - 1;
        }

        // Never split a surrogate pair (emoji, rare CJK).
        if (bestLength > 0 && bestLength < input.Length && char.IsHighSurrogate(input[bestLength - 1]))
            bestLength--;

        var result = input[..bestLength].TrimEnd();

        if (bestLength < input.Length && bestLength > 3
            && Encoding.UTF8.GetByteCount(result) + Encoding.UTF8.GetByteCount("...") <= maxBytes)
        {
            result += "...";
        }

        return result;
    }

    public static bool IsValidFilename(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename)) return false;
        if (Encoding.UTF8.GetByteCount(filename) > MaxFilenameBytes) return false;
        if (filename.Any(c => InvalidChars.Contains(c) || c < 32)) return false;
        if (filename.Contains("..") || filename.Contains('/') || filename.Contains('\\')) return false;
        return true;
    }
}
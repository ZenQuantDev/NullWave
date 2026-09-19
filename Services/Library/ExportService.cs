using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using NullWave.Models;

namespace NullWave.Services;

public class ExportService
{
    public void ExportToJson(IReadOnlyList<Track> tracks, string filePath)
    {
        var json = JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filePath, json);
    }

    public void ExportToCsv(IReadOnlyList<Track> tracks, string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Artist,Source,URL,FilePath,DateAdded");

        foreach (var t in tracks)
        {
            // FIX: Sanitize formula injection before RFC-4180 escaping
            var safeTitle = CsvSafe(t.Title);
            var safeArtist = CsvSafe(t.Artist);
            var safeUrl = CsvSafe(t.Url);
            var safePath = CsvSafe(t.FilePath);

            // RFC-4180 CSV escaping: double up internal quotes
            var escapedTitle = safeTitle?.Replace("\"", "\"\"") ?? "";
            var escapedArtist = safeArtist?.Replace("\"", "\"\"") ?? "";
            var escapedUrl = safeUrl?.Replace("\"", "\"\"") ?? "";
            var escapedPath = safePath?.Replace("\"", "\"\"") ?? "";

            sb.AppendLine($"\"{escapedTitle}\",\"{escapedArtist}\",{t.Source},\"{escapedUrl}\",\"{escapedPath}\",{t.DateAdded:yyyy-MM-dd}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public List<Track> ImportFromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<Track>>(json) ?? new();
    }

    /// <summary>
    /// Prevents CSV formula injection by prefixing dangerous characters with a single quote.
    /// Characters that trigger formula interpretation in Excel: = + - @ \t \r
    /// </summary>
    private static string? CsvSafe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if ("=+-@\t\r".Contains(value[0]))
            return "'" + value;
        return value;
    }
}
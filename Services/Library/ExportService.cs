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
            // RFC-4180 CSV escaping: double up internal quotes
            var escapedTitle = t.Title?.Replace("\"", "\"\"") ?? "";
            var escapedArtist = t.Artist?.Replace("\"", "\"\"") ?? "";
            var escapedUrl = t.Url?.Replace("\"", "\"\"") ?? "";
            var escapedPath = t.FilePath?.Replace("\"", "\"\"") ?? "";

            sb.AppendLine($"\"{escapedTitle}\",\"{escapedArtist}\",{t.Source},\"{escapedUrl}\",\"{escapedPath}\",{t.DateAdded:yyyy-MM-dd}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public List<Track> ImportFromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<Track>>(json) ?? new();
    }
}
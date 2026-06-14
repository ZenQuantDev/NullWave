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
            sb.AppendLine($"\"{t.Title}\",\"{t.Artist}\",{t.Source},\"{t.Url}\",\"{t.FilePath}\",{t.DateAdded:yyyy-MM-dd}");

        File.WriteAllText(filePath, sb.ToString());
    }

    public List<Track> ImportFromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<Track>>(json) ?? new();
    }
}
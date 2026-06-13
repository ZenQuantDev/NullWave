using System;
using System.IO;
using Serilog;
using TagLib;

namespace NullWave.Services.Metadata;

public class LocalMetadataFetcher
{
    public (string Title, string Artist) Fetch(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var title = file.Tag.Title;
            var artist = file.Tag.FirstPerformer
                         ?? (file.Tag.Performers.Length > 0
                             ? string.Join(", ", file.Tag.Performers)
                             : null);

            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileNameWithoutExtension(filePath);
            if (string.IsNullOrWhiteSpace(artist))
                artist = "Unknown";

            Log.Information("Local file tags read: {Title} by {Artist}", title, artist);
            return (title, artist);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "TagLib failed for {Path}, falling back to filename", filePath);
            return (Path.GetFileNameWithoutExtension(filePath), "Unknown");
        }
    }
}
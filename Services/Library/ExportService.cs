using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NullWave.Helpers;
using NullWave.Models;
using Serilog;

namespace NullWave.Services;

public record ExportFolderResult(int Copied, int Skipped, int Failed, string? PlaylistPath);

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
            var safeTitle = CsvSafe(t.Title);
            var safeArtist = CsvSafe(t.Artist);
            var safeUrl = CsvSafe(t.Url);
            var safePath = CsvSafe(t.FilePath);

            var escapedTitle = safeTitle?.Replace("\"", "\"\"") ?? "";
            var escapedArtist = safeArtist?.Replace("\"", "\"\"") ?? "";
            var escapedUrl = safeUrl?.Replace("\"", "\"\"") ?? "";
            var escapedPath = safePath?.Replace("\"", "\"\"") ?? "";

            sb.AppendLine($"\"{escapedTitle}\",\"{escapedArtist}\",{t.Source},\"{escapedUrl}\",\"{escapedPath}\",{t.DateAdded:dd-MM-yyyy}");
        }

        File.WriteAllText(filePath, sb.ToString());
    }

    public List<Track> ImportFromJson(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<List<Track>>(json) ?? new();
    }

    /// <summary>
    /// N6: Safe Export Pipeline. Copies files to a destination folder, renames them using
    /// FileNamer (255-byte limit), retags the COPIES (forcing ID3v2.3 for compatibility),
    /// and generates a relative M3U8 playlist. Original files are never modified.
    /// </summary>
    public Task<ExportFolderResult> ExportToFolderAsync(IReadOnlyList<Track> tracks, string destDir, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destDir);
            int copied = 0, skipped = 0, failed = 0;
            var exportedTracks = new List<Track>();

            // Force ID3v2.3 for maximum compatibility (Android players, older car stereos).
            // These are STATIC in TagLib, so we MUST restore them in a finally block or every
            // later tag write in the app silently becomes v2.3.
            var prevForce = TagLib.Id3v2.Tag.ForceDefaultVersion;
            var prevVer = TagLib.Id3v2.Tag.DefaultVersion;
            TagLib.Id3v2.Tag.ForceDefaultVersion = true;
            TagLib.Id3v2.Tag.DefaultVersion = 3;

            try
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var t = tracks[i];

                    if (string.IsNullOrEmpty(t.FilePath) || !File.Exists(t.FilePath))
                    {
                        skipped++;
                        continue;
                    }

                    string? destPath = null;
                    try
                    {
                        var ext = Path.GetExtension(t.FilePath);
                        var safeName = FileNamer.Generate(t.Artist, t.Title, t.Id.ToString(), ext);
                        destPath = Path.Combine(destDir, safeName);

                        if (File.Exists(destPath))
                            destPath = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(safeName)}_{i}{ext}");

                        // 1. Copy the file (never move or modify the original)
                        try
                        {
                            File.Copy(t.FilePath, destPath, overwrite: false);
                        }
                        catch
                        {
                            // Don't leave a half-copied file on the USB if the copy failed.
                            try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                            throw;
                        }
                        FileOperationJournal.Instance.Record(FileOpType.Copy, t.FilePath, destPath, t.Id);

                        // 2. Retag the COPY. Skip placeholder artists so we don't write "Unknown" into files.
                        try
                        {
                            using var file = TagLib.File.Create(destPath);
                            file.Tag.Title = t.Title;
                            if (!string.IsNullOrWhiteSpace(t.Artist) && !TrackNormalizer.IsPlaceholderArtist(t.Artist))
                                file.Tag.Performers = new[] { t.Artist };
                            file.Save();
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex, "[Export] Retag failed for copy {Path}", destPath);
                        }

                        copied++;
                        exportedTracks.Add(new Track
                        {
                            Title = t.Title,
                            Artist = t.Artist,
                            FilePath = destPath,
                            Duration = t.Duration,
                            MediaType = t.MediaType
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "[ExportService] Failed to export {Title}", t.Title);
                        failed++;
                    }
                }

                // 3. Generate relative M3U8 playlist
                string? m3uPath = null;
                if (exportedTracks.Count > 0)
                {
                    m3uPath = Path.Combine(destDir, "playlist.m3u8");
                    var result = M3uWriter.Build(exportedTracks, m3uPath,
                        new M3uWriteOptions(RelativePaths: true, PlaylistName: "Exported Playlist"));
                    M3uWriter.WriteFile(m3uPath, result.Text);
                }

                return new ExportFolderResult(copied, skipped, failed, m3uPath);
            }
            finally
            {
                // ALWAYS restore TagLib's global state, even if cancellation or error.
                TagLib.Id3v2.Tag.ForceDefaultVersion = prevForce;
                TagLib.Id3v2.Tag.DefaultVersion = prevVer;
            }
        }, ct);
    }

    private static string? CsvSafe(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if ("=+-@\t\r".Contains(value[0]))
            return "'" + value;
        return value;
    }
}
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Generic;

namespace NullWave.Helpers;

public static class ChangelogParser
{
    public static string GetLatestReleaseNotes()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("CHANGELOG.md"));
                
            if (resourceName == null) return "Release notes not found.";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return "Could not read release notes.";
            
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();
            
            var lines = content.Split('\n');
            var result = new StringBuilder();
            bool inSection = false;
            
            foreach (var line in lines)
            {
                // Robust guard: matches standard KeepAChangelog "## [1.0.0]" headers 
                // without accidentally catching prose "## " headers.
                if (line.StartsWith("## [") || line.StartsWith("## "))
                {
                    if (inSection) break; // Stop at the next version header
                    inSection = true;
                }
                
                if (inSection) result.AppendLine(line.TrimEnd('\r'));
            }
            
            return result.ToString().Trim();
        }
        catch
        {
            return "Could not load release notes.";
        }
    }

    /// <summary>
    /// Extracts the raw header line (e.g., "[0.6.2] - 2026-09-26") to display 
    /// as a subtitle in the What's New UI.
    /// </summary>
    public static string GetLatestVersionHeader()
    {
        var notes = GetLatestReleaseNotes();
        if (string.IsNullOrWhiteSpace(notes)) return string.Empty;

        return notes.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("## [") || l.StartsWith("## "))?
            .TrimStart('#', ' ') ?? string.Empty;
    }

    public record ChangelogSection(string Header, List<string> Bullets);

    public static List<ChangelogSection> GetLatestReleaseSections()
    {
        return ParseSections(GetLatestReleaseNotes());
    }

    /// <summary>
    /// Extracted for testability (InternalsVisibleTo is already in the csproj).
    /// Indented sub-bullets are intentionally dropped — the popup is a summary.
    /// </summary>
    internal static List<ChangelogSection> ParseSections(string raw)
    {
        var sections = new List<ChangelogSection>();
        if (string.IsNullOrWhiteSpace(raw)) return sections;

        ChangelogSection? current = null;

        foreach (var rawLine in raw.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimEnd();
            if (line.StartsWith("### "))
            {
                current = new ChangelogSection(line[4..].Trim(), new List<string>());
                sections.Add(current);
            }
            else if ((line.StartsWith("- ") || line.StartsWith("* ")) && current != null)
            {
                current.Bullets.Add(line[2..].Trim());
            }
        }
        return sections;
    }
}
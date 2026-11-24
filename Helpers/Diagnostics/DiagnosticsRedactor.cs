using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NullWave.Helpers.Diagnostics;

public static class DiagnosticsRedactor
{
    private const string PrivacyMarker = "Privacy Note: NullWave operates entirely offline";

    private const string HonestyLine =
        "\n\n---\n" +
        "*Privacy Note: NullWave operates entirely offline for your library. " +
        "No audio files, playlists, or listening history are ever transmitted to external servers. " +
        "Local paths in this report have been redacted for your privacy.*";

    // Matches Windows absolute paths with at least one directory separator after the drive.
    // Keeps the final file name, hides directories.
    private static readonly Regex WindowsAbsolutePath = new Regex(
        "(?<dir>[A-Za-z]:[\\\\/].*[\\\\/])(?<file>[^\\\\/:\\r\\n]+)(?<suffix>:\\s*line\\s*\\d+)?",
        RegexOptions.Compiled);

    // Matches UNC paths. Keeps final file/share leaf, hides server/share directories.
    private static readonly Regex UncAbsolutePath = new Regex(
        "\\\\\\\\[^\\\\]+\\\\[^\\\\]+\\\\(?:.*\\\\)?(?<file>[^\\\\:\\r\\n]+)(?<suffix>:\\s*line\\s*\\d+)?",
        RegexOptions.Compiled);

    // Matches Unix-like home paths. Keeps final file name, hides directories.
    private static readonly Regex UnixHomePath = new Regex(
        "(?<dir>/home/[^/\\r\\n]+/.*[/])(?<file>[^/:\\r\\n]+)(?<suffix>:\\s*line\\s*\\d+)?",
        RegexOptions.Compiled);

    private static readonly Regex WindowsBarePath = new Regex(
    @"[A-Za-z]:\\[^\\/:*?""<>|\r\n]+",
    RegexOptions.Compiled);

    public static string RedactText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var redacted = RedactPaths(text);

        if (!redacted.Contains(PrivacyMarker, StringComparison.Ordinal))
            redacted += HonestyLine;

        return redacted;
    }

    public static string RedactPaths(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Temporarily normalize the current user profile to a synthetic drive path
        // so the Windows path regex can hide subdirectories under it.
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        const string userTempPrefix = "Z:\\__NULLWAVE_USERPROFILE__";

        if (!string.IsNullOrEmpty(userProfile))
        {
            text = text.Replace(
                userProfile,
                userTempPrefix,
                StringComparison.OrdinalIgnoreCase);
        }

        text = WindowsAbsolutePath.Replace(
            text,
            "[PATH]\\${file}${suffix}");

        text = WindowsBarePath.Replace(text, "[PATH]");
            text = UncAbsolutePath.Replace(
            text,
            "[UNC]\\${file}${suffix}");

        text = UnixHomePath.Replace(
            text,
            "[HOME]/${file}${suffix}");

        if (!string.IsNullOrEmpty(userProfile))
        {
            text = text.Replace(
                userTempPrefix,
                "[USER_PROFILE]",
                StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    public static string RedactPreferencesJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "{}";

        try
        {
            using var doc = JsonDocument.Parse(json);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                WriteRedactedElement(doc.RootElement, writer, propertyName: null);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch
        {
            // Never fall back to the original JSON. If redaction fails, withhold contents.
            return "{ \"error\": \"Preferences could not be safely redacted. Original contents withheld.\" }";
        }
    }

    private static void WriteRedactedElement(
        JsonElement element,
        Utf8JsonWriter writer,
        string? propertyName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var prop in element.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name); 
                    WriteRedactedElement(prop.Value, writer, prop.Name);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in element.EnumerateArray())
                {
                    WriteRedactedElement(item, writer, propertyName);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                var s = element.GetString() ?? string.Empty;

                if (IsSecretName(propertyName))
                {
                    writer.WriteStringValue("[REDACTED_SECRET]");
                }
                else if (IsPathName(propertyName))
                {
                    writer.WriteStringValue("[REDACTED_PATH]");
                }
                else
                {
                    writer.WriteStringValue(RedactPaths(s));
                }

                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsSecretName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var n = name
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return
            n.Contains("apikey", StringComparison.Ordinal) ||
            n.Contains("secret", StringComparison.Ordinal) ||
            n.Contains("token", StringComparison.Ordinal) ||
            n.Contains("password", StringComparison.Ordinal) ||
            n.Contains("passwd", StringComparison.Ordinal) ||
            n.Contains("credential", StringComparison.Ordinal) ||
            n.Contains("privatekey", StringComparison.Ordinal) ||
            n.Contains("accesskey", StringComparison.Ordinal) ||
            n.Contains("authorization", StringComparison.Ordinal) ||
            n.Contains("bearer", StringComparison.Ordinal);
    }

    private static bool IsPathName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var n = name
            .Replace("_", "", StringComparison.Ordinal)
            .Replace("-", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();

        return
            n.Contains("path", StringComparison.Ordinal) ||
            n.Contains("directory", StringComparison.Ordinal) ||
            n.Contains("dir", StringComparison.Ordinal) ||
            n.Contains("folder", StringComparison.Ordinal) ||
            n.EndsWith("file", StringComparison.Ordinal) ||
            n.Contains("filepath", StringComparison.Ordinal);
    }
}
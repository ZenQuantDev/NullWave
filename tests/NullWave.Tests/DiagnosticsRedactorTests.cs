using System;
using NullWave.Helpers.Diagnostics;
using Xunit;

namespace NullWave.Tests;

public class DiagnosticsRedactorTests
{
    [Theory]
    [InlineData(@"C:\Users\Alex\Music\song.mp3")]
    [InlineData(@"D:\всё в месте\Xiaomi Pad 5\Audio\Chinese_Tom😢.mp3")]
    [InlineData(@"C:\Users\Alex\Desktop\VS Code Projects\NullWave\Services\Audio\PlaybackService.cs:line 166")]
    [InlineData(@"/home/alex/music/song.mp3")]
    [InlineData(@"\\NAS\Music\song.mp3")]
    public void RedactText_masks_directory_structure_but_can_keep_file_leaf(string path)
    {
        var redacted = DiagnosticsRedactor.RedactText(path);

        Assert.DoesNotContain(@"C:\Users\Alex", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\всё в месте", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/home/alex", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\\NAS", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"Desktop\VS Code Projects", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactPreferencesJson_masks_secrets_and_paths()
    {
        var json = """
        {
          "Language": "ru-RU",
          "LastFmApiKey": "super-secret",
          "Token": "abc123",
          "Password": "hunter2",
          "DownloadDirectory": "D:\\всё в месте",
          "PinnedFolders": [
            "D:\\всё в месте\\Xiaomi Pad 5"
          ]
        }
        """;

        var redacted = DiagnosticsRedactor.RedactPreferencesJson(json);

        Assert.DoesNotContain("super-secret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\всё в месте", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ru-RU", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED_SECRET]", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED_PATH]", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactPreferencesJson_does_not_return_original_on_invalid_json()
    {
        var bad = "{ not json";
        var redacted = DiagnosticsRedactor.RedactPreferencesJson(bad);

        Assert.DoesNotContain(bad, redacted, StringComparison.Ordinal);
        Assert.Contains("could not be safely redacted", redacted, StringComparison.OrdinalIgnoreCase);
    }
}
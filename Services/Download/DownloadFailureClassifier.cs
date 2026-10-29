using System;

namespace NullWave.Services;

public enum DownloadFailureKind
{
    Unknown,
    CookiesUnusable,
    BotCheck,
    RateLimited,
    JsRuntimeMissing,
    Unavailable
}

/// <summary>
/// Classifies yt-dlp stderr into failure kinds.
/// Used to decide whether to retry, what message to show the user, and whether to
/// keep or delete a track during playlist downloads.
/// </summary>
public static class DownloadFailureClassifier
{
    public static DownloadFailureKind Classify(string stderr, bool hasJsRuntime)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return DownloadFailureKind.Unknown;

        static bool Has(string s, string w) =>
            s.Contains(w, StringComparison.OrdinalIgnoreCase);

        // Cookie failures: Chrome is running, encryption issues, permission denied
        if (Has(stderr, "cookie") &&
            (Has(stderr, "Could not copy") ||
             Has(stderr, "Failed to decrypt") ||
             Has(stderr, "could not find") ||
             Has(stderr, "Permission denied")))
        {
            return DownloadFailureKind.CookiesUnusable;
        }

        // YouTube bot check (age gate, sign-in required)
        if (Has(stderr, "not a bot") || Has(stderr, "Sign in to confirm"))
            return DownloadFailureKind.BotCheck;

        // Rate limiting
        if (Has(stderr, "HTTP Error 429") || Has(stderr, "Too Many Requests"))
            return DownloadFailureKind.RateLimited;

        // Missing JS runtime for signature decryption
        if (!hasJsRuntime &&
            (Has(stderr, "n challenge") ||
             Has(stderr, "Requested format is not available")))
        {
            return DownloadFailureKind.JsRuntimeMissing;
        }

        // Video permanently unavailable
        if (Has(stderr, "Video unavailable") ||
            Has(stderr, "Private video") ||
            Has(stderr, "has been removed"))
        {
            return DownloadFailureKind.Unavailable;
        }

        return DownloadFailureKind.Unknown;
    }
}
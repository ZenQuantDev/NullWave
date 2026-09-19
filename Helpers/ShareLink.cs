using System;

namespace NullWave.Helpers;

/// <summary>
/// Foundation for future P2P track sharing + local-first profile sharing.
/// Track URIs embed the owner's Install ID (peer address) + track GUID:
///   nullwave://{installId}/track/{trackId}
/// Profile URIs embed a compressed, self-contained share payload:
///   nullwave://p/NW1.{base64url}
/// When P2P lands, NullWave registers as the OS handler for this scheme.
/// </summary>
public static class ShareLink
{
    public const string Scheme = "nullwave";
    private const string ProfileHost = "p";
    private const string ProfilePayloadPrefix = "NW1.";

    public static string Track(string installId, Guid trackId) =>
        $"{Scheme}://{installId}/track/{trackId:N}";

    public static bool TryParseTrack(string? uri, out string installId, out Guid trackId)
    {
        installId = string.Empty;
        trackId = Guid.Empty;
        if (!Uri.TryCreate(uri ?? "", UriKind.Absolute, out var u) ||
            !string.Equals(u.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = u.AbsolutePath.Trim('/').Split('/');
        if (parts.Length != 2 || !string.Equals(parts[0], "track", StringComparison.OrdinalIgnoreCase))
            return false;

        installId = u.Host;
        return Guid.TryParse(parts[1], out trackId);
    }

    /// <summary>
    /// Builds a profile deep-link from an encoded payload, tolerating payloads
    /// that already carry the scheme.
    /// </summary>
    public static string ProfileUri(string encodedPayload) =>
        encodedPayload.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)
            ? encodedPayload
            : $"{Scheme}://{ProfileHost}/{encodedPayload}";

    /// <summary>
    /// Accepts either a full nullwave://p/... URI, any nullwave://p URI parsed
    /// loosely, or a bare "NW1...." payload string (manual entry from a card)
    /// and returns the encoded payload for ProfileShareService.Decode.
    /// </summary>
    public static bool TryParseProfile(string? input, out string encodedPayload)
    {
        encodedPayload = string.Empty;
        var raw = (input ?? "").Trim();
        if (raw.Length == 0) return false;

        // Full URI, prefix form
        var prefix = $"{Scheme}://{ProfileHost}/";
        if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            encodedPayload = raw[prefix.Length..].Trim();
            return encodedPayload.Length > 0;
        }

        // Full URI, parsed form (covers case/variations)
        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(uri.Host, ProfileHost, StringComparison.OrdinalIgnoreCase))
        {
            encodedPayload = uri.AbsolutePath.Trim('/');
            return encodedPayload.Length > 0;
        }

        // Bare payload typed manually from a printed/shared card
        if (raw.StartsWith(ProfilePayloadPrefix, StringComparison.OrdinalIgnoreCase))
        {
            encodedPayload = raw;
            return true;
        }

        return false;
    }

    public static bool IsProfileLink(string? input) => TryParseProfile(input, out _);
}
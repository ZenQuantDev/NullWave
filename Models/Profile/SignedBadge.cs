using System;
using System.Text;
using System.Text.Json;
using NullWave.Helpers;
using NullWave.Services.Security;

namespace NullWave.Models;

public record BadgePayload(
    string RecipientFingerprint, 
    string BadgeType, 
    long IssuedUnix, 
    string? IssuerName,
    long? ExpiresUnix = null
);

public class SignedBadge
{
    public string KeyId { get; set; } = string.Empty;
    public BadgePayload Payload { get; set; } = null!;
    public string Signature { get; set; } = string.Empty;

    private static bool ContainsPipe(string? s) => s != null && s.Contains('|');

    public string CanonicalPayload()
    {
        var p = Payload ?? throw new InvalidOperationException("Badge has no payload");
        if (ContainsPipe(KeyId) || ContainsPipe(p.BadgeType) ||
            ContainsPipe(p.RecipientFingerprint) || ContainsPipe(p.IssuerName))
            throw new InvalidOperationException("Badge fields must not contain '|'");

        return $"v2|{KeyId}|{p.BadgeType}|{p.RecipientFingerprint}|{p.IssuedUnix}|{p.ExpiresUnix ?? 0}|{p.IssuerName ?? ""}";
    }

    public bool IsExpired => Payload?.ExpiresUnix is long exp && 
                             DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp;

    public bool VerifySignature()
    {
        try
        {
            if (!TrustedKeys.TryGetPublicKey(KeyId, out var publicKey)) return false;
            
            var signatureBytes = Convert.FromBase64String(Signature);
            return IdentityService.Verify(
                Encoding.UTF8.GetBytes(CanonicalPayload()),
                signatureBytes,
                publicKey);
        }
        catch
        {
            return false;
        }
    }

    public bool IsOfficial() => VerifySignature();

    public bool IsBoundTo(string fingerprint)
    {
        return !string.IsNullOrWhiteSpace(fingerprint) &&
               Payload != null &&
               string.Equals(Payload.RecipientFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
    }
}

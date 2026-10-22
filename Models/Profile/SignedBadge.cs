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

    /// <summary>
    /// v2 canonical format: v2|keyId|badgeId|fingerprint|issuedUnix|expiresUnix|issuerName
    /// </summary>
    public string CanonicalPayload()
    {
        // Reject any field containing '|' to prevent ambiguity
        if (ContainsPipe(KeyId) || ContainsPipe(Payload?.BadgeType) || 
            ContainsPipe(Payload?.RecipientFingerprint) || ContainsPipe(Payload?.IssuerName))
            throw new InvalidOperationException("Badge fields must not contain '|'");

        return $"v2|{KeyId}|{Payload.BadgeType}|{Payload.RecipientFingerprint}|{Payload.IssuedUnix}|{Payload.ExpiresUnix ?? 0}|{Payload.IssuerName ?? ""}";
    }

    public bool IsExpired => Payload.ExpiresUnix.HasValue && 
                             DateTimeOffset.UtcNow.ToUnixTimeSeconds() > Payload.ExpiresUnix.Value;

    /// <summary>
    /// Verifies the signature against the trusted key for this KeyId.
    /// </summary>
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

using System;
using System.Text;
using System.Text.Json;
using NullWave.Helpers;
using NullWave.Services.Security;

namespace NullWave.Models;

public record BadgePayload(string RecipientFingerprint, string BadgeType, long IssuedUnix, string? IssuerName);

public class SignedBadge
{
    public BadgePayload Payload { get; set; } = null!;
    public string IssuerPublicKey { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;

    public string CanonicalPayload() => JsonSerializer.Serialize(Payload);

    /// <summary>
    /// Verifies the signature against a specific trusted public key.
    /// Never trust the IssuerPublicKey embedded inside the badge itself.
    /// </summary>
    public bool VerifySignature(byte[] trustedPublicKey)
    {
        try
        {
            if (trustedPublicKey == null || trustedPublicKey.Length == 0) return false;
            
            var signatureBytes = Convert.FromBase64String(Signature);
            
            return IdentityService.Verify(
                Encoding.UTF8.GetBytes(CanonicalPayload()),
                signatureBytes,
                trustedPublicKey);
        }
        catch
        {
            // Malformed base64, bad signature format, or crypto exception
            return false;
        }
    }

    /// <summary>
    /// Verifies against the app's pinned official key. 
    /// False if the key is a placeholder or the signature is bad.
    /// </summary>
    public bool VerifyAgainstMasterKey() =>
        MasterKeys.TryGetPublicKey(out var key) && VerifySignature(key);

    /// <summary>
    /// Checks if the badge was legitimately issued by the official NullWave master key.
    /// </summary>
    public bool IsOfficial()
    {
        // 1. The embedded key must match our pinned master key
        if (IssuerPublicKey != MasterKeys.PublicKeyBase64) return false;
        
        // 2. The signature must actually be valid for that master key
        return VerifyAgainstMasterKey();
    }

    /// <summary>
    /// Checks if this badge was issued to the specified identity fingerprint.
    /// Prevents badges from being copied between users.
    /// </summary>
    public bool IsBoundTo(string fingerprint)
    {
        return !string.IsNullOrWhiteSpace(fingerprint) &&
               Payload != null &&
               string.Equals(Payload.RecipientFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase);
    }
}
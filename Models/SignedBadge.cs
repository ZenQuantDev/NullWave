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

    public bool VerifySignature() =>
        IdentityService.Verify(
            Encoding.UTF8.GetBytes(CanonicalPayload()),
            Convert.FromBase64String(Signature),
            Convert.FromBase64String(IssuerPublicKey));

    public bool IsOfficial() => IssuerPublicKey == NullWave.Helpers.MasterKeys.PublicKeyBase64;
}
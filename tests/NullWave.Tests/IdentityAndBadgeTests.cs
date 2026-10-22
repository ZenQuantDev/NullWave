using System;
using System.Security.Cryptography;
using System.Text;
using NullWave.Helpers;
using NullWave.Models;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

public class IdentityAndBadgeTests
{
    [Fact]
    public void Badge_signed_by_attacker_key_is_rejected_as_official()
    {
        // Attacker generates their own key
        using var attackerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        
        // Attacker signs with their own key, but uses a fake keyId
        var payload = new BadgePayload("NW-1234-5678-9ABC", "early", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "Attacker");
        var canonical = $"v2|attacker-key-id|early|NW-1234-5678-9ABC|{payload.IssuedUnix}|0|Attacker";
        var signature = Convert.ToBase64String(attackerKey.SignData(
            Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256));

        var badge = new SignedBadge
        {
            KeyId = "attacker-key-id",
            Payload = payload,
            Signature = signature
        };

        // Even though the signature is valid for the attacker's key,
        // the keyId is not in TrustedKeys, so IsOfficial() returns false
        Assert.False(badge.IsOfficial());
    }

    [Fact]
    public void Badge_with_malformed_signature_does_not_throw()
    {
        var badge = new SignedBadge
        {
            KeyId = "zenquant-2026-10",
            Payload = new BadgePayload("NW-1234", "test", 0, "Test"),
            Signature = "also-not-valid!!!"
        };

        // Should safely return false, not crash the app
        Assert.False(badge.IsOfficial());
    }

    [Fact]
    public void Badge_must_be_bound_to_the_local_recipient()
    {
        var payload = new BadgePayload("NW-1111-2222-3333", "test", 0, "Test");
        var badge = new SignedBadge { KeyId = "zenquant-2026-10", Payload = payload };

        Assert.True(badge.IsBoundTo("NW-1111-2222-3333"));
        Assert.True(badge.IsBoundTo("nw-1111-2222-3333")); // case insensitive
        Assert.False(badge.IsBoundTo("NW-9999-8888-7777"));
        Assert.False(badge.IsBoundTo(""));
        Assert.False(badge.IsBoundTo(null!));
    }

    [Fact]
    public void Canonical_payload_format_is_stable()
    {
        var badge = new SignedBadge
        {
            KeyId = "test-key",
            Payload = new BadgePayload("NW-AAAA", "founder", 1234567890, "ZenQuant", ExpiresUnix: 9999999999)
        };

        var canonical = badge.CanonicalPayload();
        
        // Verify the v2 format: v2|keyId|badgeId|fingerprint|issuedUnix|expiresUnix|issuerName
        Assert.StartsWith("v2|", canonical);
        var parts = canonical.Split('|');
        Assert.Equal(7, parts.Length);
        Assert.Equal("v2", parts[0]);
        Assert.Equal("test-key", parts[1]);
        Assert.Equal("founder", parts[2]);
        Assert.Equal("NW-AAAA", parts[3]);
        Assert.Equal("1234567890", parts[4]);
        Assert.Equal("9999999999", parts[5]);
        Assert.Equal("ZenQuant", parts[6]);
    }

    [Fact]
    public void IdentityService_generates_valid_fingerprint()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pubKeyBytes = key.ExportSubjectPublicKeyInfo();
        var fingerprint = IdentityService.ComputeFingerprint(pubKeyBytes);

        Assert.Matches("^NW-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$", fingerprint);
    }
}

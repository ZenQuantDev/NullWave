using System;
using System.Security.Cryptography;
using System.Text;
using NullWave.Helpers;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class BadgePinningTests
{
    private static SignedBadge SignWith(ECDsa key, string keyId, string badgeType = "founder")
    {
        var badge = new SignedBadge
        {
            KeyId = keyId,
            Payload = new BadgePayload("NW-AAAA-BBBB-CCCC", badgeType, 1_700_000_000, "ZenQuant"),
        };
        var sig = key.SignData(Encoding.UTF8.GetBytes(badge.CanonicalPayload()), HashAlgorithmName.SHA256);
        badge.Signature = Convert.ToBase64String(sig);
        return badge;
    }

    [Fact]
    public void Valid_badge_signed_by_trusted_key_is_accepted()
    {
        // Generate a test keypair and temporarily register it
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = "test-key-" + Guid.NewGuid().ToString("N")[..8];
        TrustedKeys.RegisterForTest(keyId, key.ExportSubjectPublicKeyInfo());
        try
        {
            var badge = SignWith(key, keyId);
            Assert.True(badge.VerifySignature());
            Assert.True(badge.IsOfficial());
        }
        finally
        {
            TrustedKeys.UnregisterForTest(keyId);
        }
    }

    [Fact]
    public void Tampered_payload_is_rejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var keyId = "test-key-" + Guid.NewGuid().ToString("N")[..8];
        TrustedKeys.RegisterForTest(keyId, key.ExportSubjectPublicKeyInfo());
        try
        {
            var badge = SignWith(key, keyId);
            // Tamper: change the badge type after signing
            badge.Payload = new BadgePayload("NW-AAAA-BBBB-CCCC", "dev", 1_700_000_000, "ZenQuant");
            Assert.False(badge.VerifySignature());
        }
        finally
        {
            TrustedKeys.UnregisterForTest(keyId);
        }
    }

    [Fact]
    public void A_badge_signed_by_an_unknown_key_is_rejected()
    {
        using var unknownKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var badge = SignWith(unknownKey, "unknown-key-id");

        Assert.False(badge.VerifySignature());
        Assert.False(badge.IsOfficial());
    }

    [Fact]
    public void TrustedKeys_returns_true_for_known_key()
    {
        Assert.True(TrustedKeys.TryGetPublicKey("zenquant-2026-10", out var key));
        Assert.True(key.Length > 0);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(key, out _);
    }

    [Fact]
    public void TrustedKeys_returns_false_for_unknown_key()
    {
        Assert.False(TrustedKeys.TryGetPublicKey("nonexistent-key", out _));
    }

    [Fact]
    public void Malformed_signature_does_not_throw()
    {
        var badge = new SignedBadge
        {
            KeyId = "zenquant-2026-10",
            Payload = new BadgePayload("NW-AAAA", "founder", 0, "Test"),
            Signature = "not-base64!!"
        };

        Assert.False(badge.VerifySignature());
    }

    [Fact]
    public void Expired_badge_is_detected()
    {
        var pastUnix = DateTimeOffset.UtcNow.AddDays(-10).ToUnixTimeSeconds();
        var badge = new SignedBadge
        {
            KeyId = "zenquant-2026-10",
            Payload = new BadgePayload("NW-AAAA", "founder", pastUnix - 86400, "Test", ExpiresUnix: pastUnix),
        };

        Assert.True(badge.IsExpired);
    }

    [Fact]
    public void Non_expired_badge_is_not_flagged()
    {
        var futureUnix = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds();
        var badge = new SignedBadge
        {
            KeyId = "zenquant-2026-10",
            Payload = new BadgePayload("NW-AAAA", "founder", 0, "Test", ExpiresUnix: futureUnix),
        };

        Assert.False(badge.IsExpired);
    }

    [Fact]
    public void Badge_without_expiry_is_never_expired()
    {
        var badge = new SignedBadge
        {
            KeyId = "zenquant-2026-10",
            Payload = new BadgePayload("NW-AAAA", "founder", 0, "Test", ExpiresUnix: null),
        };

        Assert.False(badge.IsExpired);
    }
}

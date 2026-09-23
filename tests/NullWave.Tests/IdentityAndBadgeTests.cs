using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        var attackerPubKey = Convert.ToBase64String(attackerKey.ExportSubjectPublicKeyInfo());
        
        var payload = new BadgePayload("NW-1234-5678-9ABC", "EarlyAdopter", DateTimeOffset.UtcNow.ToUnixTimeSeconds(), "Attacker");
        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        var signature = Convert.ToBase64String(attackerKey.SignData(payloadBytes, HashAlgorithmName.SHA256));

        var badge = new SignedBadge
        {
            Payload = payload,
            IssuerPublicKey = attackerPubKey, // Attacker embeds their own key
            Signature = signature
        };

        // Even though the signature matches the embedded key, it is NOT official
        Assert.False(badge.IsOfficial());
    }

    [Fact]
    public void Badge_with_malformed_base64_does_not_throw()
    {
        var badge = new SignedBadge
        {
            Payload = new BadgePayload("NW-1234", "Test", 0, "Test"),
            IssuerPublicKey = "not-valid-base64!!!",
            Signature = "also-not-valid!!!"
        };

        // Should safely return false, not crash the app
        Assert.False(badge.IsOfficial());
    }

    [Fact]
    public void Badge_must_be_bound_to_the_local_recipient()
    {
        var payload = new BadgePayload("NW-1111-2222-3333", "Test", 0, "Test");
        var badge = new SignedBadge { Payload = payload };

        Assert.True(badge.IsBoundTo("NW-1111-2222-3333"));
        Assert.False(badge.IsBoundTo("NW-9999-8888-7777"));
    }
}
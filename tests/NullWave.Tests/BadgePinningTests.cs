using System;
using System.Security.Cryptography;
using System.Text;
using NullWave.Helpers;
using NullWave.Models;
using Xunit;

namespace NullWave.Tests;

public class BadgePinningTests
{
    private static SignedBadge SignWith(ECDsa key)
    {
        var badge = new SignedBadge
        {
            Payload = new BadgePayload("NW-AAAA-BBBB-CCCC", "Founder", 1_700_000_000, "ZenQuant"),
            IssuerPublicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo())
        };
        var sig = key.SignData(Encoding.UTF8.GetBytes(badge.CanonicalPayload()), HashAlgorithmName.SHA256);
        badge.Signature = Convert.ToBase64String(sig);
        return badge;
    }

    [Fact]
    public void A_badge_signed_by_an_attacker_key_fails_against_the_pinned_key()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var pinned = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var forged = SignWith(attacker);

        Assert.False(forged.VerifySignature(pinned.ExportSubjectPublicKeyInfo()));
        Assert.True(forged.VerifySignature(attacker.ExportSubjectPublicKeyInfo()));
    }

    [Fact]
    public void The_master_key_is_valid_base64_and_parses_as_a_P256_key()
    {
        Assert.True(MasterKeys.TryGetPublicKey(out var key));

        using var ecdsa = ECDsa.Create();
        var ex = Record.Exception(() => ecdsa.ImportSubjectPublicKeyInfo(key, out _));
        Assert.Null(ex);
    }

    [Fact]
    public void Malformed_base64_in_a_badge_does_not_throw()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var badge = SignWith(key);
        badge.Signature = "not base64!!";

        Assert.False(badge.VerifySignature(key.ExportSubjectPublicKeyInfo()));
    }
}
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NullWave.Models;
using NullWave.Services;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

public class IdentityAndSharingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nw-id-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public IdentityAndSharingTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private KeyStoreService CreateStore() => new(Path.Combine(_dir, "keys.enc"), _key);

    [Fact]
    public void IdentityService_warns_when_keystore_was_recovered()
    {
        var store = CreateStore();
        // Simulate a recovered keystore (e.g., corrupted file moved aside)
        typeof(KeyStoreService).GetProperty("WasRecovered")!.SetValue(store, true);

        var identity = new IdentityService(store);

        Assert.True(identity.IdentityWasRegenerated);
    }

    [Fact]
    public void ProfileShare_round_trip_preserves_payload()
    {
        var payload = new ProfileSharePayload
        {
            V = 1, Code = "NW-1234-5678", Name = "Alex", Bio = "Test",
            TopTags = ["rock", "indie"], Tracks = 50
        };

        var encoded = ProfileShareService.Encode(payload);
        var decoded = ProfileShareService.Decode(encoded);

        Assert.NotNull(decoded);
        Assert.Equal("Alex", decoded!.Name);
        Assert.Equal(50, decoded.Tracks);
    }

    [Fact]
    public void ProfileShare_rejects_decompression_bombs()
    {
        // Create a payload that compresses very well (repeated characters)
        var hugeString = new string('A', 2_000_000); 
        var payload = new ProfileSharePayload { V = 1, Bio = hugeString };
        
        // Manually create a bomb to bypass any encode limits if we add them later
        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var outMs = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(outMs, System.IO.Compression.CompressionLevel.SmallestSize, true))
        using (var writer = new StreamWriter(deflate, Encoding.UTF8)) writer.Write(json);
        
        var b64 = Convert.ToBase64String(outMs.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var maliciousInput = "NW1." + b64;

        var decoded = ProfileShareService.Decode(maliciousInput);
        
        Assert.Null(decoded); // Should be rejected safely
    }

    [Fact]
    public void ProfileShare_rejects_oversize_input()
    {
        var hugeInput = "NW1." + new string('A', 20_000); // 20KB base64
        Assert.Null(ProfileShareService.Decode(hugeInput));
    }

    [Fact]
    public void GetPublicCode_generates_unique_codes_for_different_fingerprints()
    {
        // The old implementation only generated ~4096 unique codes total.
        // We test 2000 random fingerprints and expect almost all of them to be unique.
        var codes = new System.Collections.Generic.HashSet<string>();
        var dummyStore = CreateStore();
        
        for (int i = 0; i < 2000; i++)
        {
            var identity = new IdentityService(dummyStore);
            codes.Add(ProfileShareService.GetPublicCode(identity));
            
            // Force a new identity for the next loop iteration
            dummyStore.DeleteKey("Identity:PrivateKey");
        }

        // We expect at least 1950 unique codes (allowing for tiny hash collisions)
        Assert.True(codes.Count > 1950, $"Only {codes.Count} unique codes generated out of 2000.");
    }
}
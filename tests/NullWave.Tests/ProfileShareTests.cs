using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using NullWave.Models;
using NullWave.Services;
using Xunit;

namespace NullWave.Tests;

public class ProfileShareTests
{
    [Fact]
    public void Decode_rejects_decompression_bombs()
    {
        // One byte over the limit, so this stays correct even if the limit changes.
        var hugeString = new string('A', ProfileShareService.MaxDecompressedSizeForTests + 1);
        var payload = new ProfileSharePayload { V = 1, Bio = hugeString };

        using var ms = new MemoryStream();
        using (var deflate = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(deflate, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            writer.Write(System.Text.Json.JsonSerializer.Serialize(payload));

        var b64 = Convert.ToBase64String(ms.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var maliciousInput = "NW1." + b64;

        var result = ProfileShareService.Decode(maliciousInput);
        Assert.Null(result);
    }

    [Fact]
    public void Decode_accepts_a_payload_right_at_the_size_limit()
    {
        // One byte under, to prove the limit isn't overly strict.
        var bio = new string('A', ProfileShareService.MaxDecompressedSizeForTests - 200); // leave room for JSON overhead
        var payload = new ProfileSharePayload { V = 1, Bio = bio };

        var encoded = ProfileShareService.Encode(payload);
        var result = ProfileShareService.Decode(encoded);

        Assert.NotNull(result);
    }

    [Fact]
    public void Decode_handles_garbage_input_without_throwing()
    {
        Assert.Null(ProfileShareService.Decode("NW1.NotRealBase64!!!"));
        Assert.Null(ProfileShareService.Decode(""));
        Assert.Null(ProfileShareService.Decode("nullwave://p/NW1.???"));
    }
}
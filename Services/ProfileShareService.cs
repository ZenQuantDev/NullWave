using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia.Media.Imaging;
using NullWave.Models;
using NullWave.Services.Security;
using QRCoder;
using Serilog;

namespace NullWave.Services;

public static class ProfileShareService
{
    private const string Prefix = "NW1.";
    private const int MaxInputLength = 16 * 1024; // 16 KB base64 limit
    private const int MaxDecompressedSize = 128 * 1024; // 128 KB decompressed limit
    internal const int MaxDecompressedSizeForTests = MaxDecompressedSize;
    
    private static readonly JsonSerializerOptions JsonOpts = new();

    public static string GetPublicCode(IdentityService identity)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity.Fingerprint ?? "unknown"));
        
        var sb = new StringBuilder(8);
        for (int i = 0; i < 8; i++)
            sb.Append(alphabet[hash[i] % 32]);
        
        return $"NW-{sb.ToString(0, 4)}-{sb.ToString(4, 4)}";
    }

    public static string Encode(ProfileSharePayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var outMs = new MemoryStream();
        
        // FIX: Use UTF8Encoding(false) to prevent the Byte Order Mark (BOM) from being written.
        // The BOM breaks System.Text.Json deserialization on the receiving end.
        using (var deflate = new DeflateStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(deflate, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(json);
        }
        
        return Prefix + Convert.ToBase64String(outMs.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static ProfileSharePayload? Decode(string input)
    {
        try
        {
            var body = input.Trim();
            if (body.StartsWith("nullwave://p/", StringComparison.OrdinalIgnoreCase))
                body = body["nullwave://p/".Length..];
            if (!body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

            if (body.Length > MaxInputLength)
            {
                Log.Warning("[ProfileShare] Share string exceeds maximum length.");
                return null;
            }

            var b64 = body[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            var padding = (4 - b64.Length % 4) % 4;
            if (padding > 0) b64 += new string('=', padding);
            
            var raw = Convert.FromBase64String(b64);
            
            using var inMs = new MemoryStream(raw);
            using var deflate = new DeflateStream(inMs, CompressionMode.Decompress);
            
            using var outMs = new MemoryStream();
            var buffer = new byte[8192];
            int bytesRead;
            while ((bytesRead = deflate.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (outMs.Length + bytesRead > MaxDecompressedSize)
                {
                    Log.Warning("[ProfileShare] Decompressed payload exceeded size limit.");
                    return null;
                }
                outMs.Write(buffer, 0, bytesRead);
            }

            var bytes = outMs.ToArray();
            
            // FIX: Strip UTF-8 BOM if it exists (for backward compatibility with older generated codes)
            int start = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                start = 3;
            
            var json = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
            var payload = JsonSerializer.Deserialize<ProfileSharePayload>(json);
            
            if (payload == null) return null;
            if (payload.V != 1 && payload.V != 0) return null;
            
            return payload;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[ProfileShare] Failed to decode share string");
            return null;
        }
    }

    public static Bitmap? GenerateQrBitmap(string shareString, int pixelsPerModule = 6)
    {
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(shareString, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
            return new Bitmap(new MemoryStream(png));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProfileShare] QR generation failed");
            return null;
        }
    }

    public static double ComputeTasteOverlap(ProfileSharePayload other, IReadOnlyList<Track> library)
    {
        if (other.TopTracks.Count == 0 && other.TopTags.Count == 0) return 0;
        string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var myTracks = library.Select(t => (Norm(t.Title), Norm(t.Artist))).ToHashSet();
        var myTags = library.SelectMany(t => t.Tags ?? new List<string>()).Select(Norm).ToHashSet();

        int hit = 0, total = 0;
        foreach (var t in other.TopTracks) { total++; if (myTracks.Contains((Norm(t.Title), Norm(t.Artist)))) hit++; }
        foreach (var g in other.TopTags) { total++; if (myTags.Contains(Norm(g))) hit++; }
        return total == 0 ? 0 : (double)hit / total;
    }
}
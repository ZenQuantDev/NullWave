using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
    private static readonly JsonSerializerOptions JsonOpts = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault };

    public static string GetPublicCode(IdentityService identity)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var bytes = Encoding.UTF8.GetBytes(identity.Fingerprint ?? "unknown");
        var sb = new StringBuilder("NW-");
        for (int i = 0; i < 6; i++)
            sb.Append(alphabet[bytes[i % bytes.Length] % 32]);
        return sb.ToString();
    }

    public static string Encode(ProfileSharePayload payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        using var outMs = new MemoryStream();
        using (var deflate = new DeflateStream(outMs, CompressionLevel.Optimal, leaveOpen: true))
        using (var writer = new StreamWriter(deflate, Encoding.UTF8))
            writer.Write(json);
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

            var b64 = body[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            var raw = Convert.FromBase64String(b64 + new string('=', (4 - b64.Length % 4) % 4));
            
            using var inMs = new MemoryStream(raw);
            using var deflate = new DeflateStream(inMs, CompressionMode.Decompress);
            using var reader = new StreamReader(deflate, Encoding.UTF8);
            var json = reader.ReadToEnd();
            var payload = JsonSerializer.Deserialize<ProfileSharePayload>(json);
            return payload?.V == 1 ? payload : null;
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
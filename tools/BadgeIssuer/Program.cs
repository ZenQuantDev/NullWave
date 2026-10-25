using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Usage: dotnet run -- --key <base64-pkcs8> --keyid zenquant-2026-10 --badge founder --fingerprint NW-xxxx --issuer ZenQuant [--days 365]

class Program
{
    static int Main(string[] args)
    {
        string? keyBase64 = null, keyId = null, badgeType = null, fingerprint = null, issuer = null;
        int? days = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--key" when i + 1 < args.Length: keyBase64 = args[++i]; break;
                case "--keyid" when i + 1 < args.Length: keyId = args[++i]; break;
                case "--badge" when i + 1 < args.Length: badgeType = args[++i]; break;
                case "--fingerprint" when i + 1 < args.Length: fingerprint = args[++i]; break;
                case "--issuer" when i + 1 < args.Length: issuer = args[++i]; break;
                case "--days" when i + 1 < args.Length: days = int.Parse(args[++i]); break;
            }
        }

        if (keyBase64 == null || keyId == null || badgeType == null || fingerprint == null || issuer == null)
        {
            Console.WriteLine("Usage: dotnet run -- --key <base64-pkcs8> --keyid <id> --badge <type> --fingerprint <NW-xxxx> --issuer <name> [--days <n>]");
            return 1;
        }

        try
        {
            var privateKey = ECDsa.Create();
            privateKey.ImportPkcs8PrivateKey(Convert.FromBase64String(keyBase64), out _);

            var issuedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiresUnix = days.HasValue 
                ? DateTimeOffset.UtcNow.AddDays(days.Value).ToUnixTimeSeconds() 
                : (long?)null;

            var canonical = $"v2|{keyId}|{badgeType}|{fingerprint}|{issuedUnix}|{expiresUnix ?? 0}|{issuer}";
            var signature = Convert.ToBase64String(privateKey.SignData(
                Encoding.UTF8.GetBytes(canonical), 
                HashAlgorithmName.SHA256));

            var badge = new
            {
                KeyId = keyId,
                Payload = new
                {
                    RecipientFingerprint = fingerprint,
                    BadgeType = badgeType,
                    IssuedUnix = issuedUnix,
                    IssuerName = issuer,
                    ExpiresUnix = expiresUnix
                },
                Signature = signature
            };

            Console.WriteLine(JsonSerializer.Serialize(badge, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}

using System;

namespace NullWave.Helpers;

public static class MasterKeys
{
    // ZenQuant Official P-256 public key (SubjectPublicKeyInfo, Base64).
    // Generated offline; the matching private key is never in this repo.
    public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEiGxeHXjJvbLEc7qKBtEuGb1cuJGAcU3Vmaqw9RhFeFvGGMzgp1HOAevBnzp8ZQt+AEMSs6m9vPDYJ+kVx8pO6Q==";

    public static bool TryGetPublicKey(out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(PublicKeyBase64);
            return key.Length > 0;
        }
        catch (FormatException)
        {
            key = Array.Empty<byte>();
            return false;
        }
    }
}
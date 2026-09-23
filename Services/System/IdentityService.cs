using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Security;

public class IdentityService
{
    private readonly KeyStoreService _keyStore;
    private ECDsa? _key;

    public string Fingerprint { get; private set; } = string.Empty;
    public string PublicKeyBase64 => _key == null ? string.Empty : Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());
    
    /// <summary>True if the keystore was unreadable and a new identity had to be generated.</summary>
    public bool IdentityWasRegenerated { get; private set; }

    public IdentityService(KeyStoreService keyStore)
    {
        _keyStore = keyStore;
        LoadOrCreate();
    }

    private void LoadOrCreate()
    {
        try
        {
            var privateKeyBase64 = _keyStore.GetKey("Identity:PrivateKey");
            
            if (!string.IsNullOrEmpty(privateKeyBase64))
            {
                _key = ECDsa.Create();
                _key.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
                Log.Information("[IdentityService] Loaded cryptographic identity.");
            }
            else
            {
                // FIX: Detect if the keystore was recovered (quarantined) so we can warn the user
                IdentityWasRegenerated = _keyStore.WasRecovered;
                if (IdentityWasRegenerated)
                    Log.Warning("[IdentityService] Keystore was recovered - generating a NEW identity. The old data is in the keys.enc.bad-* file.");

                _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                var pkcs8 = _key.ExportPkcs8PrivateKey();
                _keyStore.SaveKey("Identity:PrivateKey", Convert.ToBase64String(pkcs8));
                Log.Information("[IdentityService] Generated new local cryptographic identity.");
            }
            
            Fingerprint = ComputeFingerprint(_key.ExportSubjectPublicKeyInfo());
        }
        catch (Exception ex) 
        { 
            Log.Error(ex, "[IdentityService] Failed to initialize IdentityService"); 
        }
    }

    public static string ComputeFingerprint(byte[] publicKeyBytes)
    {
        var hash = SHA256.HashData(publicKeyBytes);
        var b32 = Convert.ToHexString(hash)[..12]; 
        return $"NW-{b32[..4]}-{b32[4..8]}-{b32[8..12]}";
    }

    public byte[] Sign(byte[] data) => _key!.SignData(data, HashAlgorithmName.SHA256);
    public byte[] ExportPublicKey() => _key!.ExportSubjectPublicKeyInfo();

    public static bool Verify(byte[] data, byte[] signature, byte[] publicKeyBytes)
    {
        try
        {
            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
            return verifier.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[IdentityService] Signature verification failed.");
            return false;
        }
    }
}
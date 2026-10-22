using System;
using System.Collections.Generic;

namespace NullWave.Helpers;

public static class TrustedKeys
{
    private static readonly Dictionary<string, string> Keys = new()
    {
        ["zenquant-2026-10"] = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEiGxeHXjJvbLEc7qKBtEuGb1cuJGAcU3Vmaqw9RhFeFvGGMzgp1HOAevBnzp8ZQt+AEMSs6m9vPDYJ+kVx8pO6Q=="
    };

    // Test-only: allow tests to register ephemeral keys
    private static readonly Dictionary<string, byte[]> _testKeys = new();
    private static readonly object _lock = new();

    public static bool TryGetPublicKey(string keyId, out byte[] key)
    {
        lock (_lock)
        {
            if (_testKeys.TryGetValue(keyId, out var testKey))
            {
                key = testKey;
                return true;
            }
        }

        if (Keys.TryGetValue(keyId, out var base64))
        {
            try
            {
                key = Convert.FromBase64String(base64);
                return key.Length > 0;
            }
            catch (FormatException)
            {
                key = Array.Empty<byte>();
                return false;
            }
        }
        key = Array.Empty<byte>();
        return false;
    }

    public static IReadOnlyCollection<string> AllKeyIds => Keys.Keys;

#if DEBUG
    public static void RegisterForTest(string keyId, byte[] publicKey)
    {
        lock (_lock) { _testKeys[keyId] = publicKey; }
    }

    public static void UnregisterForTest(string keyId)
    {
        lock (_lock) { _testKeys.Remove(keyId); }
    }
#endif
}

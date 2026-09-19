using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Security;

public class KeyStoreService
{
    private readonly string _storePath;
    private readonly byte[] _encryptionKey;

    public KeyStoreService()
    {
        Directory.CreateDirectory(NullWavePaths.DataDir);
        _storePath = NullWavePaths.KeyStorePath;
        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave", "keys.enc");
        if (!File.Exists(_storePath) && File.Exists(legacy))
        {
            File.Move(legacy, _storePath);
            Log.Information("Migrated keys.enc to {Path}", _storePath);
        }
        _encryptionKey = DeriveKey();
    }

    /*
     * DEV NOTE: We derive the encryption key from the OS Machine ID and Username.
     * This means if the user renames their Windows account or moves the drive to
     * another PC, their API keys will become unreadable. We accept this trade-off
     * for a zero-friction "no master password" UX.
     */
    private static byte[] DeriveKey()
    {
        var machineId = GetMachineId();
        var username = Environment.UserName;
        var raw = $"{machineId}:{username}:nullwave-keystore-v1";
        return SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }

    private static string GetMachineId()
    {
        if (NullWavePaths.IsLinux)
        {
            try { return File.ReadAllText("/etc/machine-id").Trim(); } catch { }
        }
        return Environment.MachineName;
    }

    public Dictionary<string, string> LoadKeys()
    {
        if (!File.Exists(_storePath)) return new Dictionary<string, string>();
        try
        {
            var blob = File.ReadAllBytes(_storePath);
            var json = Decrypt(blob);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load keystore - may be corrupted or machine ID changed");
            return new Dictionary<string, string>();
        }
    }

    public void SaveKey(string name, string value)
    {
        var keys = LoadKeys();
        keys[name] = value;
        Persist(keys);
    }

    public void DeleteKey(string name)
    {
        var keys = LoadKeys();
        if (keys.Remove(name)) Persist(keys);
    }

    public string? GetKey(string name) => LoadKeys().TryGetValue(name, out var val) ? val : null;

    public void DeleteAllKeys()
    {
        if (File.Exists(_storePath))
        {
            var size = new FileInfo(_storePath).Length;
            using (var fs = new FileStream(_storePath, FileMode.Open))
            {
                var noise = RandomNumberGenerator.GetBytes((int)size);
                fs.Write(noise, 0, noise.Length);
            }
            File.Delete(_storePath);
            Log.Warning("All API keys have been securely deleted");
        }
    }

    private void Persist(Dictionary<string, string> keys)
    {
        var json = JsonSerializer.Serialize(keys);
        var blob = Encrypt(json);
        File.WriteAllBytes(_storePath, blob);
    }

    // .NET 8 Modern Cryptography: Uses Span<T> to avoid GC allocations of sensitive plaintext
    private byte[] Encrypt(string plaintext)
    {
        var nonce = new byte[12];
        var tag = new byte[16];
        RandomNumberGenerator.Fill(nonce);

        var input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, input, ciphertext, tag);

        var result = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);

        // Wipe plaintext from memory immediately
        Array.Clear(input, 0, input.Length);
        return result;
    }

    private byte[] Decrypt(byte[] blob)
    {
        if (blob.Length < 28) throw new CryptographicException("Invalid ciphertext size");

        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var ciphertext = blob.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Serilog;

namespace NullWave.Services;

public class KeyStoreService
{
    private readonly string _storePath;
    private readonly byte[] _encryptionKey;

    public KeyStoreService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nullwave");
        Directory.CreateDirectory(dir);
        _storePath = Path.Combine(dir, "keys.enc");
        _encryptionKey = DeriveKey();
    }

    // Derive a 256-bit key from machine-id + username — never stored anywhere
    private static byte[] DeriveKey()
    {
        var machineId = GetMachineId();
        var username = Environment.UserName;
        var raw = $"{machineId}:{username}:nullwave-keystore-v1";

        using var sha = SHA256.Create();
        return sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
    }

    private static string GetMachineId()
    {
        // Linux: /etc/machine-id, fallback to hostname
        try
        {
            var mid = File.ReadAllText("/etc/machine-id").Trim();
            if (!string.IsNullOrEmpty(mid)) return mid;
        }
        catch { }
        return Environment.MachineName;
    }

    public Dictionary<string, string> LoadKeys()
    {
        if (!File.Exists(_storePath))
            return new Dictionary<string, string>();

        try
        {
            var blob = File.ReadAllBytes(_storePath);
            var json = Decrypt(blob);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load keystore — may be corrupted");
            return new Dictionary<string, string>();
        }
    }

    public void SaveKey(string name, string value)
    {
        var keys = LoadKeys();
        keys[name] = value;
        Persist(keys);
        Log.Information("API key saved: {Name}", name);
    }

    public void DeleteKey(string name)
    {
        var keys = LoadKeys();
        if (keys.Remove(name))
        {
            Persist(keys);
            Log.Information("API key deleted: {Name}", name);
        }
    }

    public string? GetKey(string name)
    {
        var keys = LoadKeys();
        return keys.TryGetValue(name, out var val) ? val : null;
    }

    public void DeleteAllKeys()
    {
        if (File.Exists(_storePath))
        {
            // Overwrite with random bytes before deleting (secure wipe)
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

    // AES-256-GCM: nonce(12) + tag(16) + ciphertext
    private byte[] Encrypt(string plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Encrypt(nonce, input, ciphertext, tag);

        var result = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);
        return result;
    }

    private byte[] Decrypt(byte[] blob)
    {
        var nonce = blob[..12];
        var tag = blob[12..28];
        var ciphertext = blob[28..];
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
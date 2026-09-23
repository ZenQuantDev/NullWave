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
    private readonly object _lock = new();
    private Dictionary<string, string>? _cache;

    /// <summary>True when an unreadable keystore was moved aside (keys must be re-entered).</summary>
    public bool WasRecovered { get; private set; }
    private bool _quarantineFailed;

    public KeyStoreService() : this(NullWavePaths.KeyStorePath, DeriveKey())
    {
        MigrateLegacyIfNeeded();
        lock (_lock) { LoadLocked(); }   // surfaces WasRecovered at startup
    }

    internal KeyStoreService(string storePath, byte[] encryptionKey)
    {
        _storePath = storePath;
        _encryptionKey = encryptionKey;
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
    }

    private void MigrateLegacyIfNeeded()
    {
        try
        {
            var legacy = Path.Combine(NullWavePaths.LegacyDataDir, "keys.enc");
            if (File.Exists(_storePath) || !File.Exists(legacy)) return;
            if (string.Equals(Path.GetFullPath(legacy), Path.GetFullPath(_storePath),
                StringComparison.OrdinalIgnoreCase)) return;
            File.Move(legacy, _storePath);
            Log.Information("Migrated keys.enc to {Path}", _storePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Legacy keys.enc migration failed; continuing without it");
        }
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
        lock (_lock) return new Dictionary<string, string>(LoadLocked());
    }

    private Dictionary<string, string> LoadLocked()
    {
        if (_cache != null) return _cache;
        if (!File.Exists(_storePath)) return _cache = new Dictionary<string, string>();

        try
        {
            var json = Decrypt(File.ReadAllBytes(_storePath));
            return _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch (Exception ex)
        {
            Quarantine(ex);
            return _cache = new Dictionary<string, string>();
        }
    }

    private void Quarantine(Exception ex)
    {
        try
        {
            var bad = $"{_storePath}.bad-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(_storePath, bad);
            WasRecovered = true;
            Log.Error(ex, "Keystore could not be read (corrupted, or the machine or user changed). " +
                          "Moved to {Path}. API keys must be entered again", bad);
        }
        catch (Exception moveEx)
        {
            _quarantineFailed = true;
            Log.Error(moveEx, "Keystore is unreadable and could not be moved aside; refusing to overwrite it");
        }
    }

    public void SaveKey(string name, string value)
    {
        lock (_lock)
        {
            var keys = new Dictionary<string, string>(LoadLocked()) { [name] = value };
            Persist(keys);
        }
    }

    public void DeleteKey(string name)
    {
        lock (_lock)
        {
            var keys = new Dictionary<string, string>(LoadLocked());
            if (keys.Remove(name)) Persist(keys);
        }
    }

    public string? GetKey(string name)
    {
        lock (_lock) return LoadLocked().TryGetValue(name, out var value) ? value : null;
    }

    public void DeleteAllKeys()
    {
        lock (_lock)
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
                Log.Warning("All API keys have been deleted");
            }
            _cache = new Dictionary<string, string>();
        }
    }

    private void Persist(Dictionary<string, string> keys)
    {
        if (_quarantineFailed)
            throw new IOException("Keystore is unreadable and could not be moved aside; refusing to overwrite it.");

        var blob = Encrypt(JsonSerializer.Serialize(keys));
        var tmp = _storePath + ".tmp";
        File.WriteAllBytes(tmp, blob);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, _storePath, overwrite: true);
        _cache = new Dictionary<string, string>(keys);
    }

    // Note: plaintext still exists briefly in managed memory (string and byte[] copies).
    // This protects the file at rest only, not process memory.
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
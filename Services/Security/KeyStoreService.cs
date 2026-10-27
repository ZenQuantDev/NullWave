using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Security;

public class KeyStoreService
{
    private readonly string _storePath;
    private readonly byte[]? _encryptionKey;
    private readonly ISecretProtector? _activeProtector;
    private readonly ISecretProtector? _legacyProtector;
    private readonly object _lock = new();
    private Dictionary<string, string>? _cache;

    public bool WasRecovered { get; private set; }
    private bool _quarantineFailed;

    public KeyStoreService(string storePath)
    {
        _storePath = storePath;
        
        if (OperatingSystem.IsWindows())
        {
            var dpapi = new DpapiProtector();
            _activeProtector = dpapi.IsAvailable ? dpapi : new LegacyDerivedProtector(storePath);
        }
        else if (OperatingSystem.IsLinux())
        {
            var secretService = new SecretServiceProtector();
            _activeProtector = secretService.IsAvailable ? secretService : new LegacyDerivedProtector(storePath);
        }
        else
        {
            _activeProtector = new LegacyDerivedProtector(storePath);
        }
        
        _legacyProtector = new LegacyDerivedProtector(storePath);
    }

    internal KeyStoreService(string storePath, byte[] encryptionKey)
    {
        _storePath = storePath;
        _encryptionKey = encryptionKey;
        _activeProtector = new AesGcmProtector(1, "test", encryptionKey);
        _legacyProtector = _activeProtector;
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
            var blob = File.ReadAllBytes(_storePath);
            string json;
            
            // Try v2 format first (with protector)
            if (blob.Length >= 6 && blob[0] == 'N' && blob[1] == 'W' && blob[2] == 'K' && blob[3] == 'S')
            {
                var protectorId = blob[5];
                var protector = protectorId == _activeProtector?.Id ? _activeProtector : _legacyProtector;
                if (protector == null)
                    throw new CryptographicException($"No protector available for id {protectorId}");
                    
                var payload = new byte[blob.Length - 6];
                Array.Copy(blob, 6, payload, 0, payload.Length);
                var plaintext = protector.Unprotect(payload);
                json = Encoding.UTF8.GetString(plaintext);
            }
            else
            {
                // v1 format (legacy encryption)
                json = Encoding.UTF8.GetString(DecryptLegacy(blob));
            }
            
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
            var bad = $"{_storePath}.bad-{UtcTime.FileStamp()}";
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

        var json = JsonSerializer.Serialize(keys);
        byte[] blob;
        
        if (_activeProtector != null)
        {
            // v2 format: NWKS | version(1) | protectorId(1) | payload
            var plaintext = Encoding.UTF8.GetBytes(json);
            var payload = _activeProtector.Protect(plaintext);
            blob = new byte[6 + payload.Length];
            blob[0] = (byte)'N'; blob[1] = (byte)'W'; blob[2] = (byte)'K'; blob[3] = (byte)'S';
            blob[4] = 2; // version
            blob[5] = _activeProtector.Id;
            Array.Copy(payload, 0, blob, 6, payload.Length);
        }
        else
        {
            // Legacy format
            blob = EncryptLegacy(json);
        }
        
        var tmp = _storePath + ".tmp";
        File.WriteAllBytes(tmp, blob);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        ReplaceStoreFile(tmp);
        _cache = new Dictionary<string, string>(keys);
    }

    private void ReplaceStoreFile(string temporaryPath)
    {
        if (OperatingSystem.IsWindows() && File.Exists(_storePath))
        {
            var attributes = File.GetAttributes(_storePath);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(_storePath, attributes & ~FileAttributes.ReadOnly);
        }

        const int maxAttempts = 5;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temporaryPath, _storePath, overwrite: true);
                return;
            }
            catch (IOException ex) when (attempt < maxAttempts && IsTransientReplaceFailure(ex.HResult))
            {
                Thread.Sleep(10 << attempt);
            }
            catch (UnauthorizedAccessException ex) when (attempt < maxAttempts && IsTransientReplaceFailure(ex.HResult))
            {
                Thread.Sleep(10 << attempt);
            }
        }
    }

    private static bool IsTransientReplaceFailure(int hresult) => hresult is
        unchecked((int)0x80070005) or
        unchecked((int)0x80070020) or
        unchecked((int)0x80070021);

    private byte[] EncryptLegacy(string plaintext)
    {
        if (_encryptionKey == null)
            throw new InvalidOperationException("Encryption key not set");
            
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

        Array.Clear(input, 0, input.Length);
        return result;
    }

    private byte[] DecryptLegacy(byte[] blob)
    {
        if (_encryptionKey == null)
            throw new InvalidOperationException("Encryption key not set");
            
        if (blob.Length < 28) throw new CryptographicException("Invalid ciphertext size");

        var nonce = blob.AsSpan(0, 12);
        var tag = blob.AsSpan(12, 16);
        var ciphertext = blob.AsSpan(28);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_encryptionKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    [SupportedOSPlatform("windows")]
    internal sealed class DpapiProtector : ISecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NullWave.KeyStore.v2");
        public byte Id => 2;
        public string Name => "Windows DPAPI";
        public bool IsAvailable => OperatingSystem.IsWindows();
        
        public byte[] Protect(byte[] plain) => 
            ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            
        public byte[] Unprotect(byte[] payload) => 
            ProtectedData.Unprotect(payload, Entropy, DataProtectionScope.CurrentUser);
    }

    internal sealed class LegacyDerivedProtector : ISecretProtector
    {
        private readonly byte[] _key;
        
        public LegacyDerivedProtector(string storePath)
        {
            var machineId = GetMachineId();
            var username = Environment.UserName;
            var raw = $"{machineId}:{username}:nullwave-keystore-v1";
            _key = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        }
        
        public byte Id => 1;
        public string Name => "Legacy (Machine ID)";
        public bool IsAvailable => true;
        
        public byte[] Protect(byte[] plain) => new AesGcmProtector(Id, Name, _key).Protect(plain);
        public byte[] Unprotect(byte[] payload) => new AesGcmProtector(Id, Name, _key).Unprotect(payload);

        private static string GetMachineId()
        {
            if (NullWavePaths.IsLinux)
            {
                try { return File.ReadAllText("/etc/machine-id").Trim(); } catch { }
            }
            return Environment.MachineName;
        }
    }
}
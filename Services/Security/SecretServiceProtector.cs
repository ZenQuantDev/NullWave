using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Security;

[SupportedOSPlatform("linux")]
internal sealed class SecretServiceProtector : ISecretProtector
{
    private const string SchemaName = "dev.nullwave.keystore";
    private const string AttrApp = "application";
    private const string AttrPurpose = "purpose";
    private const string Label = "NullWave keystore master key";

    public byte Id => 3;
    public string Name => "Linux Secret Service (GNOME Keyring / KWallet)";
    
    private static bool? _isAvailable;

    public bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue) return _isAvailable.Value;
            if (!OperatingSystem.IsLinux()) return (_isAvailable = false).Value;
            
            try
            {
                var result = ProcessRunner.RunAsync("secret-tool", new[] { "--version" }, TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
                _isAvailable = result.ExitCode == 0 && !result.TimedOut;
            }
            catch { _isAvailable = false; }
            
            return _isAvailable.Value;
        }
    }

    public byte[] Protect(byte[] plain)
    {
        var masterKey = GetOrCreateMasterKey();
        var aesProtector = new AesGcmProtector(Id, Name, masterKey);
        return aesProtector.Protect(plain);
    }

    public byte[] Unprotect(byte[] payload)
    {
        // K1 FIX: Never create a key while decrypting. If the key is missing, throw.
        var masterKey = LookupMasterKey() ?? throw new InvalidOperationException("Master key missing from Secret Service");
        var aesProtector = new AesGcmProtector(Id, Name, masterKey);
        return aesProtector.Unprotect(payload);
    }

    private byte[] GetOrCreateMasterKey()
    {
        var existing = LookupMasterKey();
        if (existing != null) return existing;

        var newKey = RandomNumberGenerator.GetBytes(32);
        StoreMasterKey(newKey);
        return newKey;
    }

    // K1 FIX: Separate "not found" (null) from "error" (throws).
    private byte[]? LookupMasterKey()
    {
        var args = new[] { "lookup", AttrApp, SchemaName, AttrPurpose, "keystore-master" };
        var result = ProcessRunner.RunAsync("secret-tool", args, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        
        if (result.TimedOut) 
            throw new InvalidOperationException("Secret Service timed out (keyring locked?)");
            
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            return Convert.FromBase64String(result.StandardOutput.Trim());
            
        // Exit code 1 with empty stderr means "not found".
        if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError)) 
            return null;
            
        throw new InvalidOperationException($"Secret Service lookup failed: {result.StandardError}");
    }

    private void StoreMasterKey(byte[] key)
    {
        var base64 = Convert.ToBase64String(key);
        var args = new[] { "store", "--label", Label, AttrApp, SchemaName, AttrPurpose, "keystore-master" };
        
        var psi = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start secret-tool");
        
        p.StandardInput.Write(base64);
        p.StandardInput.Close();
        p.WaitForExit(10000);
        
        if (p.ExitCode != 0)
        {
            var err = p.StandardError.ReadToEnd();
            Log.Warning("[SecretService] store failed: {Err}", err);
            throw new InvalidOperationException($"secret-tool store failed: {err}");
        }
    }
}
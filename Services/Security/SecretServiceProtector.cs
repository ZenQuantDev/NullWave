using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

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
    public bool IsAvailable => CheckAvailable();

    private static bool CheckAvailable()
    {
        if (!OperatingSystem.IsLinux()) return false;
        try
        {
            var psi = new ProcessStartInfo("secret-tool")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var p = Process.Start(psi);
            p?.WaitForExit(2000);
            return p?.ExitCode == 0;
        }
        catch { return false; }
    }

    public byte[] Protect(byte[] plain)
    {
        var masterKey = GetOrCreateMasterKey();
        var aesProtector = new AesGcmProtector(Id, Name, masterKey);
        return aesProtector.Protect(plain);
    }

    public byte[] Unprotect(byte[] payload)
    {
        var masterKey = GetOrCreateMasterKey();
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

    private byte[]? LookupMasterKey()
    {
        var psi = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("lookup");
        psi.ArgumentList.Add(AttrApp);
        psi.ArgumentList.Add(SchemaName);
        psi.ArgumentList.Add(AttrPurpose);
        psi.ArgumentList.Add("keystore-master");

        using var p = Process.Start(psi);
        if (p == null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;

        var base64 = output.Trim();
        try { return Convert.FromBase64String(base64); }
        catch { return null; }
    }

    private void StoreMasterKey(byte[] key)
    {
        var base64 = Convert.ToBase64String(key);
        var psi = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("store");
        psi.ArgumentList.Add("--label");
        psi.ArgumentList.Add(Label);
        psi.ArgumentList.Add(AttrApp);
        psi.ArgumentList.Add(SchemaName);
        psi.ArgumentList.Add(AttrPurpose);
        psi.ArgumentList.Add("keystore-master");

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start secret-tool");
        
        p.StandardInput.Write(base64);
        p.StandardInput.Close();
        p.WaitForExit(10000);
        
        if (p.ExitCode != 0)
        {
            var err = p.StandardError.ReadToEnd();
            throw new InvalidOperationException($"secret-tool store failed: {err}");
        }
    }
}
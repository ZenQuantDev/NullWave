using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NullWave.Helpers;
using Serilog;

namespace NullWave.Services.Security;

internal delegate ProcessResult SecretServiceCommandRunner(
    IReadOnlyList<string> arguments,
    string? standardInput,
    TimeSpan timeout);

internal sealed class SecretServiceProtector : ISecretProtector
{
    private const string SchemaName = "dev.nullwave.keystore";
    private const string AttrApp = "application";
    private const string AttrPurpose = "purpose";
    private const string Label = "NullWave keystore master key";

    private readonly SecretServiceCommandRunner _commandRunner;
    private bool? _isAvailable;

    public byte Id => 3;
    public string Name => "Linux Secret Service (GNOME Keyring / KWallet)";

    internal SecretServiceProtector(SecretServiceCommandRunner? commandRunner = null)
        => _commandRunner = commandRunner ?? RunSecretTool;

    public bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue) return _isAvailable.Value;
            if (!OperatingSystem.IsLinux()) return (_isAvailable = false).Value;

            try
            {
                var result = _commandRunner(new[] { "--version" }, null, TimeSpan.FromSeconds(2));
                _isAvailable = result.ExitCode == 0 && !result.TimedOut && !result.Canceled;
                if (!_isAvailable.Value)
                    Log.Warning("[SecretService] secret-tool availability check failed (exit {ExitCode}, timed out: {TimedOut})",
                        result.ExitCode, result.TimedOut);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SecretService] secret-tool availability check failed");
                _isAvailable = false;
            }

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
        var masterKey = LookupMasterKey() ?? throw new InvalidOperationException("Master key missing from Secret Service");
        var aesProtector = new AesGcmProtector(Id, Name, masterKey);
        return aesProtector.Unprotect(payload);
    }

    internal bool ClearMasterKey()
    {
        var args = new[] { "clear", AttrApp, SchemaName, AttrPurpose, "keystore-master" };
        var result = _commandRunner(args, null, TimeSpan.FromSeconds(5));
        if (result.ExitCode == 0 && !result.TimedOut && !result.Canceled)
            return true;

        Log.Warning(
            "[SecretService] Failed to clear stored master key (exit {ExitCode}, timed out: {TimedOut}, canceled: {Canceled}): {Error}",
            result.ExitCode, result.TimedOut, result.Canceled, result.StandardError);
        return false;
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
        var args = new[] { "lookup", AttrApp, SchemaName, AttrPurpose, "keystore-master" };
        var result = _commandRunner(args, null, TimeSpan.FromSeconds(5));

        if (result.TimedOut || result.Canceled)
            throw new InvalidOperationException("Secret Service timed out (keyring may be locked or unavailable)");

        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
            return Convert.FromBase64String(result.StandardOutput.Trim());

        if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StandardError))
            return null;

        throw new InvalidOperationException($"Secret Service lookup failed: {result.StandardError}");
    }

    private void StoreMasterKey(byte[] key)
    {
        var base64 = Convert.ToBase64String(key);
        var args = new[] { "store", "--label", Label, AttrApp, SchemaName, AttrPurpose, "keystore-master" };
        var result = _commandRunner(args, base64, TimeSpan.FromSeconds(10));

        if (result.ExitCode == 0 && !result.TimedOut && !result.Canceled)
            return;

        Log.Warning(
            "[SecretService] store failed (exit {ExitCode}, timed out: {TimedOut}, canceled: {Canceled}): {Error}",
            result.ExitCode, result.TimedOut, result.Canceled, result.StandardError);
        throw new InvalidOperationException($"secret-tool store failed: {result.StandardError}");
    }

    private static ProcessResult RunSecretTool(
        IReadOnlyList<string> arguments,
        string? standardInput,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = standardInput != null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 2 or 3)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false, false);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (standardInput != null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception ex) { Log.Warning(ex, "[SecretService] Could not stop timed-out secret-tool"); }
            process.WaitForExit();
            return new ProcessResult(-1, stdout.ToString(), stderr.ToString(), true, false);
        }

        process.WaitForExit();
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString(), false, false);
    }
}

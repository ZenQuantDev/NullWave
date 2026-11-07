using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using NullWave.Helpers;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

[SupportedOSPlatform("linux")]
public sealed class SecretServiceProtectorTests
{
    [Fact]
    public void Lookup_timeout_is_reported_as_locked_or_unavailable()
    {
        var protector = new SecretServiceProtector((_, _, _) =>
            new ProcessResult(-1, string.Empty, string.Empty, TimedOut: true, Canceled: false));

        var error = Assert.Throws<InvalidOperationException>(() => protector.Unprotect(Array.Empty<byte>()));

        Assert.Contains("timed out", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_secret_is_distinguished_from_a_lookup_error()
    {
        var protector = new SecretServiceProtector((_, _, _) =>
            new ProcessResult(1, string.Empty, string.Empty, TimedOut: false, Canceled: false));

        var error = Assert.Throws<InvalidOperationException>(() => protector.Unprotect(Array.Empty<byte>()));

        Assert.Contains("Master key missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Locked_secret_service_is_not_mistaken_for_a_missing_secret()
    {
        var protector = new SecretServiceProtector((_, _, _) =>
            new ProcessResult(1, string.Empty, "The keyring is locked", TimedOut: false, Canceled: false));

        var error = Assert.Throws<InvalidOperationException>(() => protector.Unprotect(Array.Empty<byte>()));

        Assert.Contains("lookup failed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("locked", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delete_all_keys_clears_the_secret_service_master_key_attributes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nw-secret-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var key = new byte[32];
        string[]? clearArguments = null;
        var secretService = new SecretServiceProtector((args, _, _) =>
        {
            clearArguments = args.ToArray();
            return new ProcessResult(0, string.Empty, string.Empty, false, false);
        });

        try
        {
            var store = new KeyStoreService(
                Path.Combine(directory, "keys.enc"),
                key,
                new AesGcmProtector(1, "test", key),
                secretService);
            store.SaveKey("A", "secret");

            store.DeleteAllKeys();

            Assert.Equal(
                new[] { "clear", "application", "dev.nullwave.keystore", "purpose", "keystore-master" },
                clearArguments);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}

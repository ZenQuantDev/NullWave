using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

public class KeyStoreMigrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nw-ksm-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _legacy = RandomNumberGenerator.GetBytes(32);
    private readonly byte[] _active = RandomNumberGenerator.GetBytes(32);
    private const string Json = "{\"YouTube\":\"abc\"}";

    public KeyStoreMigrationTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }
    private string StorePath => Path.Combine(_dir, "keys.enc");

    private static byte[] V1Blob(string json, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        var input = Encoding.UTF8.GetBytes(json); var ct = new byte[input.Length];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, input, ct, tag);
        return nonce.Concat(tag).Concat(ct).ToArray();
    }

    private static byte[] MakeV2Blob(byte protectorId, string json, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16];
        var input = Encoding.UTF8.GetBytes(json); var ct = new byte[input.Length];
        using var aes = new AesGcm(key, 16); aes.Encrypt(nonce, input, ct, tag);
        var payload = nonce.Concat(tag).Concat(ct).ToArray();
        
        var blob = new byte[6 + payload.Length];
        blob[0] = (byte)'N'; blob[1] = (byte)'W'; blob[2] = (byte)'K'; blob[3] = (byte)'S';
        blob[4] = 2; blob[5] = protectorId;
        Array.Copy(payload, 0, blob, 6, payload.Length);
        return blob;
    }

    private KeyStoreService OpenBrokenMigration()
        => new(StorePath, _legacy, new BrokenProtector());

    private KeyStoreService Open() => new(StorePath, _legacy, new AesGcmProtector(2, "active", _active));

    [Fact]
    public void V1_file_migrates_to_v2_and_keys_survive()
    {
        File.WriteAllBytes(StorePath, V1Blob(Json, _legacy));
        Assert.Equal("abc", Open().GetKey("YouTube"));
        var b = File.ReadAllBytes(StorePath);
        Assert.Equal("NWKS", Encoding.ASCII.GetString(b, 0, 4)); 
        Assert.Equal(2, b[4]);
        Assert.True(File.Exists(StorePath + ".v1.bak"));
    }

    [Fact]
    public void Backup_is_removed_on_the_next_clean_start()
    {
        File.WriteAllBytes(StorePath, V1Blob(Json, _legacy));
        Open(); 
        Assert.True(File.Exists(StorePath + ".v1.bak"));
        
        var second = Open(); 
        Assert.False(File.Exists(StorePath + ".v1.bak"));
        Assert.Equal("abc", second.GetKey("YouTube"));
    }

    [Fact]
    public void Failed_round_trip_leaves_the_v1_file_alone()
    {
        var v1 = V1Blob(Json, _legacy);
        File.WriteAllBytes(StorePath, v1);
        
        var ks = OpenBrokenMigration();
        
        Assert.Equal(v1, File.ReadAllBytes(StorePath));
        Assert.False(File.Exists(StorePath + ".v1.bak"));
        Assert.Equal("abc", ks.GetKey("YouTube")); 
    }

    [Fact]
    public void Unknown_protector_is_left_untouched_and_blocks_writes()
    {
        File.WriteAllBytes(StorePath, MakeV2Blob(99, Json, _active));
        var original = File.ReadAllBytes(StorePath);
        
        var ks = Open();
        Assert.Null(ks.GetKey("YouTube")); 
        Assert.False(ks.WasRecovered); 
        
        Assert.Equal(original, File.ReadAllBytes(StorePath));
        Assert.Empty(Directory.GetFiles(_dir, "keys.enc.bad-*"));
        
        Assert.Throws<IOException>(() => ks.SaveKey("A", "1"));
        Assert.Equal(original, File.ReadAllBytes(StorePath));
    }

    [Fact]
    public void DeleteAllKeys_removes_backups_and_quarantine_files_and_unblocks_writes()
    {
        File.WriteAllBytes(StorePath, V1Blob(Json, _legacy)); 
        var ks = Open();
        File.WriteAllText(StorePath + ".bad-1", "x"); 
        
        ks.DeleteAllKeys();
        Assert.Empty(Directory.GetFiles(_dir, "keys.enc*"));
        
        ks.SaveKey("N", "1");
        Assert.Equal("1", ks.GetKey("N"));
    }

    // === NEW TESTS FOR K1-K4 FIXES ===

    [Fact] 
    public void Newer_format_version_is_left_untouched()
    {
        var b = MakeV2Blob(2, Json, _active); 
        b[4] = 9; // Fake version 9
        File.WriteAllBytes(StorePath, b);
        
        var ks = Open();
        Assert.Null(ks.GetKey("YouTube")); 
        Assert.False(ks.WasRecovered);
        Assert.Throws<IOException>(() => ks.SaveKey("A", "1"));
        Assert.Equal(b, File.ReadAllBytes(StorePath));
    }

    [Fact] 
    public void Throwing_protector_falls_back_and_keys_survive()
    {
        new KeyStoreService(StorePath, _legacy, new ThrowingProtector()).SaveKey("A", "1");
        Assert.Equal("1", new KeyStoreService(StorePath, _legacy, new ThrowingProtector()).GetKey("A"));
    }

    [Fact] 
    public void Empty_v1_store_creates_no_backup()
    { 
        File.WriteAllBytes(StorePath, V1Blob("{}", _legacy)); 
        Open(); 
        Assert.False(File.Exists(StorePath + ".v1.bak")); 
    }

    [Fact] 
    public void Stale_tmp_file_is_ignored_and_replaced()
    {
        File.WriteAllBytes(StorePath, V1Blob(Json, _legacy)); 
        File.WriteAllText(StorePath + ".tmp", "junk");
        Assert.Equal("abc", Open().GetKey("YouTube")); 
        Assert.False(File.Exists(StorePath + ".tmp"));
    }

    [Fact] 
    public void Keystore_file_mode_is_0600_on_Linux()
    {
        if (!OperatingSystem.IsLinux()) return;
        Open().SaveKey("A", "1");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StorePath));
    }

    [Fact] 
    public void Dpapi_round_trips_on_Windows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = new DpapiProtector(); 
        var d = new byte[] { 1, 2, 3 };
        Assert.Equal(d, p.Unprotect(p.Protect(d)));
    }

    private sealed class BrokenProtector : ISecretProtector
    {
        public byte Id => 2;
        public string Name => "broken";
        public bool IsAvailable => true;
        public byte[] Protect(byte[] plain) => plain;
        public byte[] Unprotect(byte[] payload) => new byte[] { 1, 2, 3 };
    }

    private sealed class ThrowingProtector : ISecretProtector
    {
        public byte Id => 2; 
        public string Name => "throws"; 
        public bool IsAvailable => true;
        public byte[] Protect(byte[] p) => throw new IOException("keyring locked");
        public byte[] Unprotect(byte[] p) => throw new IOException("keyring locked");
    }
}
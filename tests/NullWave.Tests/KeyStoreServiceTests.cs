using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NullWave.Services.Security;
using Xunit;

namespace NullWave.Tests;

public class KeyStoreServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nw-ks-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public KeyStoreServiceTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string StorePath => Path.Combine(_dir, "keys.enc");
    private KeyStoreService Create(byte[]? key = null) => new(StorePath, key ?? _key);

    [Fact]
    public void Saved_keys_can_be_read_by_a_new_instance()
    {
        Create().SaveKey("YouTube", "abc");
        Assert.Equal("abc", Create().GetKey("YouTube"));
    }

    [Fact]
    public void Deleting_a_key_removes_only_that_key()
    {
        var store = Create();
        store.SaveKey("A", "1");
        store.SaveKey("B", "2");
        store.DeleteKey("A");
        Assert.Null(Create().GetKey("A"));
        Assert.Equal("2", Create().GetKey("B"));
    }

    [Fact]
    public void Unreadable_file_is_moved_aside_and_never_overwritten()
    {
        Create().SaveKey("A", "1");
        var original = File.ReadAllBytes(StorePath);
        var other = Create(RandomNumberGenerator.GetBytes(32));   // wrong key = unreadable file
        
        Assert.Null(other.GetKey("A"));
        Assert.True(other.WasRecovered);
        
        other.SaveKey("B", "2");
        var bad = Directory.GetFiles(_dir, "keys.enc.bad-*").Single();
        Assert.Equal(original, File.ReadAllBytes(bad));            // untouched copy of the old file
        Assert.Equal("2", other.GetKey("B"));
    }

    [Fact]
    public void Truncated_file_is_quarantined()
    {
        Create().SaveKey("A", "1");
        File.WriteAllBytes(StorePath, new byte[10]);
        var store = Create();
        Assert.Null(store.GetKey("A"));
        Assert.True(store.WasRecovered);
        Assert.Single(Directory.GetFiles(_dir, "keys.enc.bad-*"));
    }

    [Fact]
    public void Tampered_ciphertext_is_rejected()
    {
        Create().SaveKey("A", "1");
        var bytes = File.ReadAllBytes(StorePath);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(StorePath, bytes);
        var store = Create();
        Assert.Null(store.GetKey("A"));
        Assert.True(store.WasRecovered);
    }

    [Fact]
    public void Parallel_saves_keep_every_key()
    {
        var store = Create();
        Parallel.For(0, 20, i => store.SaveKey($"k{i}", i.ToString()));
        Assert.Equal(20, Create().LoadKeys().Count);
    }
}
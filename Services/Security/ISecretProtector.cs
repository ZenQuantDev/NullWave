using System;
using System.Security.Cryptography;

namespace NullWave.Services.Security;

public interface ISecretProtector
{
    byte Id { get; }
    string Name { get; }
    bool IsAvailable { get; }
    byte[] Protect(byte[] plain);
    byte[] Unprotect(byte[] payload);
}

internal sealed class AesGcmProtector : ISecretProtector
{
    private readonly byte[] _key;
    public AesGcmProtector(byte id, string name, byte[] key) { Id = id; Name = name; _key = key; }
    public byte Id { get; }
    public string Name { get; }
    public bool IsAvailable => true;

    public byte[] Protect(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ct = new byte[plain.Length];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plain, ct, tag);
        var result = new byte[28 + ct.Length];
        nonce.CopyTo(result, 0); tag.CopyTo(result, 12); ct.CopyTo(result, 28);
        return result;
    }

    public byte[] Unprotect(byte[] payload)
    {
        if (payload.Length < 28) throw new CryptographicException("Invalid ciphertext size");
        var pt = new byte[payload.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(payload.AsSpan(0, 12), payload.AsSpan(28), payload.AsSpan(12, 16), pt);
        return pt;
    }
}
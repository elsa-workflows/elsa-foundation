using System.Security.Cryptography;
using System.Text;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

/// <summary>
/// Protects secret values with AES-GCM using the active key from the <see cref="ISecretKeyRing"/>.
/// New values are written in the <c>v2:&lt;keyId&gt;:nonce:tag:ciphertext</c> format so the exact key
/// used is recorded; values can therefore still be decrypted after the active key is rotated. The
/// legacy <c>v1:nonce:tag:ciphertext</c> format (no key id) is still read using the reserved
/// <c>legacy</c> key derived from <c>Elsa:Secrets:EncryptionKey</c>.
/// </summary>
public sealed class DefaultSecretValueProtector(ISecretKeyRing keyRing) : ISecretValueProtector
{
    private const string PrefixV1 = "v1";
    private const string PrefixV2 = "v2";

    public string Protect(string value)
    {
        var activeKey = keyRing.ActiveKey;
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(nonce);

        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(activeKey.Key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return string.Join(':', PrefixV2, activeKey.KeyId, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
    }

    public string Unprotect(string value)
    {
        var parts = value.Split(':');

        return parts switch
        {
            [PrefixV1, var nonce, var tag, var ciphertext] => Decrypt(ResolveLegacyKey(), nonce, tag, ciphertext),
            [PrefixV2, var keyId, var nonce, var tag, var ciphertext] => Decrypt(ResolveKey(keyId), nonce, tag, ciphertext),
            _ => throw new InvalidOperationException("The protected secret payload has an unsupported format.")
        };
    }

    private SecretEncryptionKey ResolveKey(string keyId)
    {
        if (!keyRing.TryGetKey(keyId, out var key))
            throw new InvalidOperationException(
                $"The protected secret was written with key '{keyId}', which is not present in the configured key-ring. Keep the key available to decrypt existing secrets.");

        return key;
    }

    private SecretEncryptionKey ResolveLegacyKey()
    {
        if (!keyRing.TryGetKey(SecretEncryptionKey.LegacyKeyId, out var key))
            throw new InvalidOperationException(
                "The protected secret was written with the legacy Elsa:Secrets:EncryptionKey, which is no longer configured. Keep it configured to decrypt existing secrets.");

        return key;
    }

    private static string Decrypt(SecretEncryptionKey key, string nonce64, string tag64, string ciphertext64)
    {
        var nonce = Convert.FromBase64String(nonce64);
        var tag = Convert.FromBase64String(tag64);
        var ciphertext = Convert.FromBase64String(ciphertext64);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key.Key, tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}

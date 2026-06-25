using System.Security.Cryptography;
using System.Text;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Secrets.Services;

public sealed class DefaultSecretValueProtector(IOptions<SecretsOptions> options) : ISecretValueProtector
{
    private const string Prefix = "v1";

    public string Protect(string value)
    {
        var key = GetKey();
        Span<byte> nonce = stackalloc byte[12];
        RandomNumberGenerator.Fill(nonce);

        var plaintext = Encoding.UTF8.GetBytes(value);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return string.Join(':', Prefix, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext));
    }

    public string Unprotect(string value)
    {
        var parts = value.Split(':');

        if (parts.Length != 4 || parts[0] != Prefix)
            throw new InvalidOperationException("The protected secret payload has an unsupported format.");

        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var ciphertext = Convert.FromBase64String(parts[3]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(GetKey(), tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }

    private byte[] GetKey()
    {
        var configuredKey = options.Value.EncryptionKey;
        var keyMaterial = string.IsNullOrWhiteSpace(configuredKey)
            ? "elsa-foundation-default-secrets-key"
            : configuredKey;

        return SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }
}

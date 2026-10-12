namespace Elsa.Secrets.Core.Models;

/// <summary>
/// A single entry in the secret encryption key-ring: a stable identifier plus the derived
/// symmetric key material used to encrypt/decrypt secret values.
/// </summary>
public sealed class SecretEncryptionKey(string keyId, byte[] key)
{
    /// <summary>
    /// Reserved key id assigned to the legacy <c>Elsa:Secrets:EncryptionKey</c> so that values
    /// written before the key-ring existed remain decryptable after rotation.
    /// </summary>
    public const string LegacyKeyId = "legacy";

    /// <summary>Stable identifier embedded in the protected payload (v2 format).</summary>
    public string KeyId { get; } = keyId;

    /// <summary>Derived symmetric key material (AES-256).</summary>
    public byte[] Key { get; } = key;
}

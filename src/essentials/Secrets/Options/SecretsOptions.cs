namespace Elsa.Secrets.Options;

public sealed class SecretsOptions
{
    public const string SectionName = "Elsa:Secrets";

    /// <summary>
    /// Legacy single encryption key. When set it is added to the key-ring under the reserved id
    /// <c>legacy</c> so values written before the key-ring existed remain decryptable.
    /// </summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    /// Identifier of the key in <see cref="Keys"/> (or the reserved <c>legacy</c> key) used to
    /// encrypt new values. Must reference a key that is present in the ring.
    /// </summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>
    /// The rotation key-ring. Each entry has a stable <see cref="SecretEncryptionKeyOptions.KeyId"/>
    /// and key material. Adding a key and pointing <see cref="ActiveKeyId"/> at it rotates the
    /// active encryption key while keeping older keys available for decryption.
    /// </summary>
    public IList<SecretEncryptionKeyOptions> Keys { get; set; } = new List<SecretEncryptionKeyOptions>();
}

public sealed class SecretEncryptionKeyOptions
{
    /// <summary>
    /// Stable key identifier embedded in protected payloads. Allowed characters are ASCII letters,
    /// digits, '.', '-' and '_'; ':' and empty/whitespace values are rejected because the protected
    /// payload format is colon-delimited.
    /// </summary>
    public string? KeyId { get; set; }

    /// <summary>The secret key material from which the symmetric key is derived.</summary>
    public string? Key { get; set; }
}

using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

/// <summary>
/// The set of symmetric keys available to protect and unprotect secret values. Exactly one key is
/// active for encryption; any key in the ring can be used for decryption, so values written under a
/// previous key remain readable after the active key is rotated.
/// </summary>
public interface ISecretKeyRing
{
    /// <summary>
    /// The key new values are encrypted with. Throws when no encryption key is configured.
    /// </summary>
    SecretEncryptionKey ActiveKey { get; }

    /// <summary>
    /// Resolves a key by its identifier. Returns <c>false</c> when the ring holds no such key.
    /// </summary>
    bool TryGetKey(string keyId, out SecretEncryptionKey key);
}

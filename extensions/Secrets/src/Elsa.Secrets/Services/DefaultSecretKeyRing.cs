using System.Security.Cryptography;
using System.Text;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Secrets.Services;

/// <summary>
/// Builds the secret encryption key-ring from <see cref="SecretsOptions"/> and validates it eagerly
/// so misconfiguration fails at startup rather than at the first encrypt. See
/// <c>docs/secrets-key-rotation.md</c> for the rotation procedure.
/// </summary>
public sealed class DefaultSecretKeyRing : ISecretKeyRing
{
    private readonly Dictionary<string, SecretEncryptionKey> _keys = new(StringComparer.Ordinal);
    private readonly SecretEncryptionKey? _activeKey;

    public DefaultSecretKeyRing(IOptions<SecretsOptions> options)
    {
        var value = options.Value;

        foreach (var entry in value.Keys)
        {
            var keyId = entry.KeyId;
            ValidateKeyId(keyId);

            if (string.IsNullOrWhiteSpace(entry.Key))
                throw new InvalidOperationException($"Secret encryption key '{keyId}' has no key material configured.");

            if (!_keys.TryAdd(keyId!, new SecretEncryptionKey(keyId!, DeriveKey(entry.Key))))
                throw new InvalidOperationException($"Duplicate secret encryption key id '{keyId}'. Key ids must be unique.");
        }

        if (!string.IsNullOrWhiteSpace(value.EncryptionKey))
        {
            if (!_keys.TryAdd(SecretEncryptionKey.LegacyKeyId, new SecretEncryptionKey(SecretEncryptionKey.LegacyKeyId, DeriveKey(value.EncryptionKey))))
                throw new InvalidOperationException(
                    $"Secret encryption key id '{SecretEncryptionKey.LegacyKeyId}' is reserved for Elsa:Secrets:EncryptionKey and cannot also be declared in Keys.");
        }

        _activeKey = ResolveActiveKey(value);
    }

    public SecretEncryptionKey ActiveKey =>
        _activeKey ?? throw new InvalidOperationException(
            "No secret encryption key is configured. Set Elsa:Secrets:EncryptionKey or configure Elsa:Secrets:Keys with an ActiveKeyId to protect secret values.");

    public bool TryGetKey(string keyId, out SecretEncryptionKey key) => _keys.TryGetValue(keyId, out key!);

    private SecretEncryptionKey? ResolveActiveKey(SecretsOptions value)
    {
        if (!string.IsNullOrWhiteSpace(value.ActiveKeyId))
        {
            if (!_keys.TryGetValue(value.ActiveKeyId, out var active))
                throw new InvalidOperationException(
                    $"Elsa:Secrets:ActiveKeyId '{value.ActiveKeyId}' does not match any configured key.");

            return active;
        }

        // No explicit active key: fall back to the legacy key, then to a single configured key.
        if (_keys.TryGetValue(SecretEncryptionKey.LegacyKeyId, out var legacy))
            return legacy;

        if (_keys.Count == 1)
            return _keys.Values.First();

        if (_keys.Count == 0)
            return null;

        throw new InvalidOperationException(
            "Multiple secret encryption keys are configured but Elsa:Secrets:ActiveKeyId is not set. Set ActiveKeyId to select the key used for encryption.");
    }

    private static void ValidateKeyId(string? keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            throw new InvalidOperationException("A secret encryption key id must be a non-empty, non-whitespace value.");

        foreach (var c in keyId)
        {
            var allowed = char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_';
            if (!allowed)
                throw new InvalidOperationException(
                    $"Secret encryption key id '{keyId}' contains an unsupported character '{c}'. Allowed characters are ASCII letters, digits, '.', '-' and '_'.");
        }
    }

    private static byte[] DeriveKey(string material) => SHA256.HashData(Encoding.UTF8.GetBytes(material));
}

using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Configuration for the restart-stable key ring used to protect deferred alteration envelopes.</summary>
public sealed class WorkflowAlterationPayloadProtectionOptions
{
    /// <summary>The key used for new payloads.</summary>
    public string? ActiveKeyId { get; set; }

    /// <summary>Base64-encoded 256-bit AES keys, retained until payload retention removes their plans.</summary>
    public IDictionary<string, string> Keys { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Allows only the in-memory Runtime default to use a process-local random key.</summary>
    public bool AllowEphemeralDevelopmentKey { get; set; }
}

/// <summary>One process-local development key. It is deliberately not restart-stable.</summary>
public sealed class WorkflowAlterationEphemeralKeyRing
{
    /// <summary>Creates the random AES-256 development key once for this process.</summary>
    public WorkflowAlterationEphemeralKeyRing() => Key = RandomNumberGenerator.GetBytes(32);

    /// <summary>Stable key identifier within this process only.</summary>
    public string KeyId { get; } = "in-memory-development";

    /// <summary>Random AES-256 material retained only in memory.</summary>
    public byte[] Key { get; }
}

/// <summary>Encrypts canonical request payloads with AES-GCM and tenant-bound authenticated associated data.</summary>
public sealed class AesGcmWorkflowAlterationPayloadProtector(
    IOptions<WorkflowAlterationPayloadProtectionOptions> options,
    WorkflowAlterationEphemeralKeyRing ephemeralKeyRing) : IWorkflowAlterationPayloadProtector
{
    private const string Algorithm = "A256GCM";
    private readonly WorkflowAlterationPayloadProtectionOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly WorkflowAlterationEphemeralKeyRing _ephemeralKeyRing = ephemeralKeyRing ?? throw new ArgumentNullException(nameof(ephemeralKeyRing));

    /// <inheritdoc />
    public ProtectedWorkflowAlterationPayload Protect(string planId, string tenantPartition, string canonicalRequestHash, string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantPartition);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestHash);
        ArgumentNullException.ThrowIfNull(plaintext);
        var (keyId, key) = ResolveActiveKey();
        var nonce = RandomNumberGenerator.GetBytes(12);
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[bytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, bytes, ciphertext, tag, AssociatedData(planId, tenantPartition, canonicalRequestHash));
        return new ProtectedWorkflowAlterationPayload(keyId, Algorithm, string.Join(':', Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(ciphertext)));
    }

    /// <inheritdoc />
    public string Unprotect(string planId, string tenantPartition, string canonicalRequestHash, ProtectedWorkflowAlterationPayload payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantPartition);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalRequestHash);
        ArgumentNullException.ThrowIfNull(payload);
        if (!StringComparer.Ordinal.Equals(payload.Algorithm, Algorithm))
            throw new CryptographicException($"Unsupported workflow alteration payload algorithm '{payload.Algorithm}'.");
        var parts = payload.Ciphertext.Split(':');
        if (parts.Length != 3)
            throw new CryptographicException("The protected workflow alteration payload has an invalid ciphertext format.");
        var nonce = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var ciphertext = Convert.FromBase64String(parts[2]);
        if (nonce.Length != 12 || tag.Length != 16)
            throw new CryptographicException("The protected workflow alteration payload has invalid AES-GCM parameters.");
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(ResolveKey(payload.KeyId), tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(planId, tenantPartition, canonicalRequestHash));
        return Encoding.UTF8.GetString(plaintext);
    }

    private (string KeyId, byte[] Key) ResolveActiveKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.ActiveKeyId))
            return (_options.ActiveKeyId, ResolveConfiguredKey(_options.ActiveKeyId));
        if (_options.AllowEphemeralDevelopmentKey)
            return (_ephemeralKeyRing.KeyId, _ephemeralKeyRing.Key);
        throw new CryptographicException("Workflow alteration payload protection requires an explicitly configured restart-stable active key.");
    }

    private byte[] ResolveKey(string keyId)
    {
        if (_options.AllowEphemeralDevelopmentKey && StringComparer.Ordinal.Equals(keyId, _ephemeralKeyRing.KeyId))
            return _ephemeralKeyRing.Key;
        return ResolveConfiguredKey(keyId);
    }

    private byte[] ResolveConfiguredKey(string keyId)
    {
        if (string.IsNullOrWhiteSpace(keyId) || !_options.Keys.TryGetValue(keyId, out var encoded))
            throw new CryptographicException($"Workflow alteration payload key '{keyId}' is not configured.");
        byte[] key;
        try
        {
            key = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException($"Workflow alteration payload key '{keyId}' is not valid base64.", exception);
        }
        if (key.Length != 32)
            throw new CryptographicException($"Workflow alteration payload key '{keyId}' must be 256 bits.");
        return key;
    }

    private static byte[] AssociatedData(string planId, string tenantPartition, string canonicalRequestHash) =>
        Encoding.UTF8.GetBytes($"{planId}\n{tenantPartition}\n{canonicalRequestHash}");
}

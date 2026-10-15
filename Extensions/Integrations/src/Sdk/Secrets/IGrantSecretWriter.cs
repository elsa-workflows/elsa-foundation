using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Sdk.Secrets;

/// <summary>
/// Writes OAuth grants as versioned secrets. Compare-and-swap is the correctness guarantee
/// (#2727 AC 5); a lock is only an optimization. Not a second <c>ISecretStore</c>.
/// </summary>
public interface IGrantSecretWriter
{
    /// <summary>Current secret version, or 0 when the name has no grant.</summary>
    ValueTask<int> GetVersionAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="value"/> only when the stored version equals
    /// <paramref name="expectedVersion"/>. Returns <see langword="false"/> on a lost race.
    /// </summary>
    ValueTask<bool> CompareAndSwapAsync(
        string tenantId,
        string name,
        int expectedVersion,
        string value,
        CancellationToken cancellationToken = default);

    ValueTask DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

public static class GrantStorePolicy
{
    /// <summary>
    /// Grants need a writable store (plan §2.5). Configuration and other read-only stores are refused.
    /// </summary>
    public static void EnsureWritable(SecretStoreDescriptor store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var writable = !store.IsReadOnly && store.Capabilities.HasFlag(SecretStoreCapabilities.Write);
        if (!writable)
            throw new InvalidOperationException(
                $"OAuth2 grants require a writable secret store. '{store.Name}' is read-only (RequiresWritableStore).");
    }
}

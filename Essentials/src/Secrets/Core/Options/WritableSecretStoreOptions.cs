using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Options;

/// <summary>
/// Selects the writable <see cref="ISecretStore"/> used for connection-owned field secrets and
/// OAuth grant and PKCE secrets. Bound from <c>Elsa:Secrets:DefaultWritableStoreName</c>.
/// A host-provided store is selected by this name, not by reusing <c>encrypted</c>.
/// </summary>
public sealed class WritableSecretStoreOptions
{
    /// <summary>
    /// Name of the registered writable store. Defaults to <see cref="SecretStoreNames.Encrypted"/>.
    /// </summary>
    public string DefaultWritableStoreName { get; set; } = SecretStoreNames.Encrypted;
}

/// <summary>
/// Resolves and checks the host-configured default writable secret store.
/// </summary>
public static class DefaultWritableSecretStore
{
    public static string ResolveName(string? configured) =>
        string.IsNullOrWhiteSpace(configured) ? SecretStoreNames.Encrypted : configured.Trim();

    /// <summary>
    /// Returns the configured store when it is registered and writable; otherwise fails immediately.
    /// </summary>
    public static ISecretStore GetRequiredWritable(ISecretStoreRegistry stores, string? configured)
    {
        ArgumentNullException.ThrowIfNull(stores);
        var name = ResolveName(configured);
        if (!stores.TryGet(name, out var store) || store is null)
        {
            throw new InvalidOperationException(
                $"The default writable secret store '{name}' is not registered. Set Elsa:Secrets:DefaultWritableStoreName to a registered writable ISecretStore. A host-provided store is selected by that name, not by reusing '{SecretStoreNames.Encrypted}'.");
        }

        EnsureWritable(store.Descriptor);
        return store;
    }

    public static void EnsureWritable(SecretStoreDescriptor store)
    {
        ArgumentNullException.ThrowIfNull(store);
        var writable = !store.IsReadOnly && store.Capabilities.HasFlag(SecretStoreCapabilities.Write);
        if (!writable)
        {
            throw new InvalidOperationException(
                $"The default writable secret store '{store.Name}' is read-only.");
        }
    }
}

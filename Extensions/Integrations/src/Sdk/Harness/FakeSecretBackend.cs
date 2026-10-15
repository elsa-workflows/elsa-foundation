using System.Collections.Concurrent;
using Elsa.Integrations.Sdk.Secrets;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Sdk.Harness;

/// <summary>
/// Test double of the foundation secret path: <see cref="ISecretValueResolver"/> for reads,
/// <see cref="IGrantSecretWriter"/> for CAS writes. Not a parallel store contract.
/// </summary>
internal sealed class FakeSecretBackend : ISecretValueResolver, IGrantSecretWriter
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private SecretStoreDescriptor _store = WritableEncryptedStore;

    public static SecretStoreDescriptor WritableEncryptedStore { get; } = new(
        SecretStoreNames.Encrypted,
        "Encrypted",
        "Harness writable store.",
        SecretStoreCapabilities.Read | SecretStoreCapabilities.Write | SecretStoreCapabilities.Delete | SecretStoreCapabilities.Versioned,
        IsReadOnly: false);

    public static SecretStoreDescriptor ReadOnlyConfigurationStore { get; } = new(
        SecretStoreNames.Configuration,
        "Configuration",
        "Harness read-only store.",
        SecretStoreCapabilities.Read | SecretStoreCapabilities.Test,
        IsReadOnly: true);

    public SecretStoreDescriptor Store
    {
        get => _store;
        set => _store = value ?? throw new ArgumentNullException(nameof(value));
    }

    public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(reference);
        if (!_entries.TryGetValue(Key(tenantId, reference.Name), out var entry))
            return ValueTask.FromResult(ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "Secret was not found."));

        var metadata = new SecretMetadata
        {
            TenantId = tenantId,
            Name = reference.Name,
            TypeName = SecretTypeNames.Text,
            StoreName = _store.Name,
            Status = SecretStatus.Active,
            CurrentVersion = entry.Version
        };
        return ValueTask.FromResult(ResolvedSecret.Success(entry.Value, metadata));
    }

    public ValueTask<int> GetVersionAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        _entries.TryGetValue(Key(tenantId, name), out var entry);
        return ValueTask.FromResult(entry?.Version ?? 0);
    }

    public ValueTask<bool> CompareAndSwapAsync(
        string tenantId,
        string name,
        int expectedVersion,
        string value,
        CancellationToken cancellationToken = default)
    {
        GrantStorePolicy.EnsureWritable(_store);
        var key = Key(tenantId, name);
        while (true)
        {
            _entries.TryGetValue(key, out var current);
            var currentVersion = current?.Version ?? 0;
            if (currentVersion != expectedVersion)
                return ValueTask.FromResult(false);

            var next = new Entry(expectedVersion + 1, value);
            if (current is null)
            {
                if (_entries.TryAdd(key, next))
                    return ValueTask.FromResult(true);
                continue;
            }

            if (_entries.TryUpdate(key, next, current))
                return ValueTask.FromResult(true);
        }
    }

    public ValueTask DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(Key(tenantId, name), out _);
        return ValueTask.CompletedTask;
    }

    public IReadOnlyDictionary<string, string> Snapshot() =>
        _entries.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

    private static string Key(string tenantId, string name) => $"{tenantId}::{name}";

    private sealed record Entry(int Version, string Value);
}

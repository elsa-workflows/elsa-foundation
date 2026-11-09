using Elsa.Primitives.Persistence;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretManager
{
    /// <summary>Creates a secret with its first version.</summary>
    /// <exception cref="Exceptions.SecretNameInUseException">A secret that is not deleted already holds the name.</exception>
    /// <exception cref="Exceptions.SecretStoreConfigurationException">
    /// The store or type is not registered, or the type does not support the store.
    /// </exception>
    ValueTask<SecretMetadata> CreateAsync(string tenantId, CreateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default);
    ValueTask<Page<SecretMetadata>> ListAsync(string tenantId, SecretQuery query, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata> UpdateAsync(string tenantId, string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default);

    /// <summary>Writes a new version of the secret and retires its active one.</summary>
    /// <exception cref="Exceptions.SecretVersionConflictException">
    /// The request has an expected version that is not the secret's active version, or it has one and a concurrent writer
    /// saved the secret first.
    /// </exception>
    /// <exception cref="Exceptions.SecretStoreConfigurationException">
    /// The secret's store or type is not registered, or the type does not support the store.
    /// </exception>
    ValueTask<SecretMetadata> RotateAsync(string tenantId, string name, RotateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes the secret, freeing its name, or returns false when there is none.</summary>
    /// <exception cref="Exceptions.SecretStoreConfigurationException">The secret's store is not registered.</exception>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Tests the secret's active version against its store. A missing or inactive secret is reported in the result.</summary>
    /// <exception cref="Exceptions.SecretStoreConfigurationException">The secret's store is not registered.</exception>
    ValueTask<SecretTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

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
    ValueTask<SecretMetadata> RotateAsync(string tenantId, string name, RotateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
    ValueTask<SecretTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}

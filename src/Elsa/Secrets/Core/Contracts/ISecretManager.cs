using Elsa.Primitives.Persistence;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretManager
{
    ValueTask<SecretMetadata> CreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> FindAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<Page<SecretMetadata>> ListAsync(SecretQuery query, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata> UpdateAsync(string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata> RotateAsync(string name, RotateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretMetadata?> RevokeAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default);
    ValueTask<SecretTestResult> TestAsync(string name, CancellationToken cancellationToken = default);
}

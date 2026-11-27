using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretTypeProvider
{
    SecretTypeDescriptor Descriptor { get; }
    ValueTask<SecretValidationResult> ValidateCreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default);
    ValueTask<SecretValidationResult> ValidateRotateAsync(RotateSecretRequest request, string storeName, CancellationToken cancellationToken = default);
}

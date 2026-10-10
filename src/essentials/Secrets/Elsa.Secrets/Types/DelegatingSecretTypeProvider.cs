using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Types;

public abstract class DelegatingSecretTypeProvider(SecretTypeDescriptor descriptor) : ISecretTypeProvider
{
    private readonly TextSecretTypeProvider _textProvider = new();

    public SecretTypeDescriptor Descriptor { get; } = descriptor;

    public ValueTask<SecretValidationResult> ValidateCreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default)
        => _textProvider.ValidateCreateAsync(request, cancellationToken);

    public ValueTask<SecretValidationResult> ValidateRotateAsync(RotateSecretRequest request, string storeName, CancellationToken cancellationToken = default)
        => _textProvider.ValidateRotateAsync(request, storeName, cancellationToken);
}

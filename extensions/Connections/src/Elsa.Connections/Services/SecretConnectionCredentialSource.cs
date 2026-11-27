using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Connections.Services;

internal sealed class SecretConnectionCredentialSource(ISecretValueResolver secretValueResolver) : IConnectionCredentialSource
{
    public string Kind => ConnectionCredentialSourceKinds.Secret;

    public async ValueTask<SensitiveValue> ResolveAsync(ConnectionCredentialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SecretName))
        {
            throw new ConnectionUnavailableException(
                request.ConnectionName,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{request.ConnectionName}' field '{request.FieldName}' has no secret reference.");
        }

        var resolved = await secretValueResolver.ResolveAsync(
            request.TenantId,
            new SecretReference(request.SecretName),
            cancellationToken);

        if (!resolved.Succeeded || resolved.Value is null)
        {
            throw new ConnectionUnavailableException(
                request.ConnectionName,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{request.ConnectionName}' could not resolve secret field '{request.FieldName}'.");
        }

        return new SensitiveValue(resolved.Value);
    }
}

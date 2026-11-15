using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Elsa.Connections.Services;

/// <summary>
/// Resolves a connection field from a named credential (<c>Elsa:Credentials:&lt;name&gt;</c>) through
/// <see cref="IAccessTokenSourceRegistry"/>. The credential's <c>Kind</c> selects the source; hosts
/// add kinds without changing this type.
/// </summary>
internal sealed class NamedCredentialConnectionCredentialSource(
    IAccessTokenSourceRegistry registry,
    IConfiguration configuration) : IConnectionCredentialSource
{
    public string Kind => ConnectionCredentialSourceKinds.NamedCredential;

    public async ValueTask<SensitiveValue> ResolveAsync(ConnectionCredentialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.NamedCredentialName))
        {
            throw new ConnectionUnavailableException(
                request.ConnectionName,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{request.ConnectionName}' field '{request.FieldName}' has no named credential.");
        }

        var kind = configuration[$"{NamedCredentialOptionKeys.SectionName}:{request.NamedCredentialName}:{NamedCredentialOptionKeys.Kind}"];
        if (string.IsNullOrWhiteSpace(kind))
            kind = ConfigurationAccessTokenSource.KindName;

        var source = registry.Find(kind)
            ?? throw new ConnectionUnavailableException(
                request.ConnectionName,
                ConnectionFaultCodes.Unavailable,
                $"No named-credential source of kind '{kind}' is registered for connection '{request.ConnectionName}'.");

        var token = await source.GetTokenAsync(
            new AccessTokenRequest(request.NamedCredentialName, request.Audience),
            cancellationToken);
        return new SensitiveValue(token.Token);
    }
}

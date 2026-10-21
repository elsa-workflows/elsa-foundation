using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Services;

internal sealed class DefaultConnectionResolver(
    IConnectionRepository repository,
    IConnectionTypeRegistry types,
    IConnectionCredentialSourceRegistry credentials) : IConnectionResolver
{
    public async ValueTask<IResolvedConnection> ResolveAsync(ConnectionResolveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        if (!string.Equals(request.Owner, ConnectionOwners.Tenant, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConnectionUnavailableException(
                request.Name,
                ConnectionFaultCodes.OwnerNotSupported,
                $"Owner '{request.Owner}' is not valid in v1; only '{ConnectionOwners.Tenant}' is.");
        }

        var name = ConnectionName.Normalize(request.Name);
        var document = await repository.FindAsync(request.TenantId, name, cancellationToken);
        if (document is null)
        {
            throw new ConnectionUnavailableException(
                name,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{name}' was not found in this tenant.");
        }

        if (request.RequiredType is { Length: > 0 } &&
            !string.Equals(document.Definition.TypeName, request.RequiredType, StringComparison.OrdinalIgnoreCase))
        {
            throw new ConnectionUnavailableException(
                name,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{name}' is type '{document.Definition.TypeName}', not '{request.RequiredType}'.");
        }

        if (string.Equals(document.State.Status, ConnectionStatuses.Revoked, StringComparison.Ordinal) ||
            string.Equals(document.State.Status, ConnectionStatuses.NeedsAuthorization, StringComparison.Ordinal) ||
            string.Equals(document.State.Status, ConnectionStatuses.Error, StringComparison.Ordinal))
        {
            throw new ConnectionUnavailableException(
                name,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{name}' is '{document.State.Status}'.");
        }

        var type = types.Find(document.Definition.TypeName)
            ?? throw new ConnectionUnavailableException(
                name,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{name}' type '{document.Definition.TypeName}' is not registered.");

        var hosts = type.ResolveHosts(new Dictionary<string, string>(document.Definition.Settings, StringComparer.OrdinalIgnoreCase));
        return new ResolvedConnection(document, hosts, credentials);
    }
}

internal sealed class ResolvedConnection(
    ConnectionDocument document,
    AllowedHostsPolicy hosts,
    IConnectionCredentialSourceRegistry credentials) : IResolvedConnection
{
    public string TenantId => document.Definition.TenantId;
    public string Name => document.Definition.Name;
    public string TypeName => document.Definition.TypeName;
    public string AuthScheme => document.Definition.AuthScheme;
    public string Owner => document.Definition.Owner;
    public string Status => document.State.Status;
    public IReadOnlyDictionary<string, string> Settings { get; } = new Dictionary<string, string>(document.Definition.Settings, StringComparer.OrdinalIgnoreCase);
    public AllowedHostsPolicy Hosts => hosts;

    public async ValueTask<SensitiveValue> GetSensitiveAsync(string fieldName, CancellationToken cancellationToken = default)
    {
        if (!document.Definition.SecretFields.TryGetValue(fieldName, out var field))
        {
            throw new ConnectionUnavailableException(
                Name,
                ConnectionFaultCodes.Unavailable,
                $"Connection '{Name}' has no secret field '{fieldName}'.");
        }

        var source = credentials.Get(field.SourceKind);
        return await source.ResolveAsync(new ConnectionCredentialRequest
        {
            TenantId = TenantId,
            ConnectionName = Name,
            FieldName = fieldName,
            SourceKind = field.SourceKind,
            SecretName = field.SecretName,
            NamedCredentialName = field.NamedCredentialName
        }, cancellationToken);
    }

    public override string ToString() => $"Connection({TenantId}/{Name})";

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

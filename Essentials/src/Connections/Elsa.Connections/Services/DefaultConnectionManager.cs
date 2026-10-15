using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Connections.Services;

internal sealed class DefaultConnectionManager(
    IConnectionRepository repository,
    IConnectionTypeRegistry types,
    IAuthenticationSchemeRegistry schemes,
    IConnectionResolver resolver,
    ISecretManager secretManager,
    IConnectionPermissionGate permissionGate,
    TimeProvider timeProvider) : IConnectionManager
{
    public async ValueTask<ConnectionMetadata> CreateAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        ConnectionName.Validate(request.Name);
        ValidateOwner(request.Owner);

        var name = ConnectionName.Normalize(request.Name);
        var type = types.Find(request.TypeName)
            ?? throw new ArgumentException($"Unknown connection type '{request.TypeName}'.", nameof(request));
        if (!type.Descriptor.SupportedSchemes.Contains(request.AuthScheme, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Connection type '{type.Descriptor.Name}' does not support scheme '{request.AuthScheme}'.", nameof(request));
        if (schemes.Find(request.AuthScheme) is null)
            throw new ArgumentException($"Unknown authentication scheme '{request.AuthScheme}'.", nameof(request));
        ValidateSettings(type, request.Settings);
        await EnsureBindSecretAsync(tenantId, request.SecretFields, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var document = new ConnectionDocument
        {
            Definition = new ConnectionDefinition
            {
                TenantId = tenantId,
                Name = name,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? name : request.DisplayName.Trim(),
                TypeName = type.Descriptor.Name,
                AuthScheme = request.AuthScheme,
                Settings = new Dictionary<string, string>(request.Settings, StringComparer.OrdinalIgnoreCase),
                ManagedBy = ConnectionManagedBy.Studio,
                Owner = ConnectionOwners.Tenant,
                Tags = [..request.Tags]
            },
            State = new ConnectionState
            {
                Status = ConnectionStatuses.Ready,
                CreatedAt = now
            }
        };

        if (!await repository.TryAddAsync(document, cancellationToken))
            throw new ConnectionConflictException(name);

        try
        {
            document.Definition.SecretFields = await WriteSecretFieldsAsync(tenantId, name, request.SecretFields, cancellationToken);
            await repository.SaveAsync(document, cancellationToken);
        }
        catch
        {
            await DeleteOwnedSecretsAsync(tenantId, name, document.Definition.SecretFields, cancellationToken);
            await repository.DeleteAsync(tenantId, name, cancellationToken);
            throw;
        }

        return ToMetadata(document);
    }

    public async ValueTask<ConnectionMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        var document = await repository.FindAsync(tenantId, ConnectionName.Normalize(name), cancellationToken);
        return document is null ? null : ToMetadata(document);
    }

    public async ValueTask<IReadOnlyList<ConnectionMetadata>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default)
    {
        var documents = await repository.ListAsync(tenantId, query, cancellationToken);
        return documents.Select(ToMetadata).ToArray();
    }

    public async ValueTask<ConnectionMetadata?> UpdateAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = await repository.FindAsync(tenantId, ConnectionName.Normalize(name), cancellationToken);
        if (document is null)
            return null;

        var type = types.Find(document.Definition.TypeName)
            ?? throw new ArgumentException($"Unknown connection type '{document.Definition.TypeName}'.", nameof(request));
        if (schemes.Find(document.Definition.AuthScheme) is null)
            throw new ArgumentException($"Unknown authentication scheme '{document.Definition.AuthScheme}'.", nameof(request));

        if (request.DisplayName is not null)
            document.Definition.DisplayName = request.DisplayName.Trim();
        if (request.Settings is not null)
        {
            ValidateSettings(type, request.Settings);
            document.Definition.Settings = new Dictionary<string, string>(request.Settings, StringComparer.OrdinalIgnoreCase);
        }
        if (request.Tags is not null)
            document.Definition.Tags = [..request.Tags];
        if (request.SecretFields is not null)
        {
            await EnsureBindSecretAsync(tenantId, request.SecretFields, cancellationToken);
            var previous = document.Definition.SecretFields;
            document.Definition.SecretFields = await WriteSecretFieldsAsync(tenantId, document.Definition.Name, request.SecretFields, cancellationToken);
            await DeleteRemovedOwnedSecretsAsync(tenantId, document.Definition.Name, previous, document.Definition.SecretFields, cancellationToken);
        }

        document.State.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(document, cancellationToken);
        return ToMetadata(document);
    }

    public async ValueTask<ConnectionTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        var document = await repository.FindAsync(tenantId, ConnectionName.Normalize(name), cancellationToken);
        if (document is null)
            return new ConnectionTestResult { Succeeded = false, Message = "Connection not found." };

        var type = types.Find(document.Definition.TypeName);
        if (type is null)
            return new ConnectionTestResult { Succeeded = false, Message = $"Unknown connection type '{document.Definition.TypeName}'." };

        try
        {
            await using var resolved = await resolver.ResolveAsync(new ConnectionResolveRequest
            {
                TenantId = tenantId,
                Name = document.Definition.Name,
                RequiredType = document.Definition.TypeName
            }, cancellationToken);

            var result = await type.TestAsync(resolved, cancellationToken);
            document.State.LastTestedAt = timeProvider.GetUtcNow();
            await repository.SaveAsync(document, cancellationToken);
            return result;
        }
        catch (ConnectionUnavailableException exception)
        {
            return new ConnectionTestResult { Succeeded = false, Message = exception.Message };
        }
    }

    public async ValueTask<ConnectionMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        var document = await repository.FindAsync(tenantId, ConnectionName.Normalize(name), cancellationToken);
        if (document is null)
            return null;

        document.State.Status = ConnectionStatuses.Revoked;
        document.State.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(document, cancellationToken);
        return ToMetadata(document);
    }

    public async ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        var document = await repository.FindAsync(tenantId, ConnectionName.Normalize(name), cancellationToken);
        if (document is null)
            return false;

        await DeleteOwnedSecretsAsync(tenantId, document.Definition.Name, document.Definition.SecretFields, cancellationToken);
        return await repository.DeleteAsync(tenantId, document.Definition.Name, cancellationToken);
    }

    private async ValueTask EnsureBindSecretAsync(
        string tenantId,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        CancellationToken cancellationToken)
    {
        foreach (var input in inputs.Values)
        {
            var bindsExisting = !string.IsNullOrWhiteSpace(input.ExistingSecretName) ||
                (!string.IsNullOrWhiteSpace(input.NamedCredentialName) &&
                 string.Equals(input.SourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase));
            if (bindsExisting)
            {
                await permissionGate.EnsureBindSecretAsync(tenantId, cancellationToken);
                return;
            }
        }
    }

    private async ValueTask<IDictionary<string, ConnectionSecretField>> WriteSecretFieldsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, ConnectionSecretField>(StringComparer.Ordinal);
        foreach (var (fieldName, input) in inputs)
        {
            var field = new ConnectionSecretField
            {
                FieldName = fieldName,
                SourceKind = string.IsNullOrWhiteSpace(input.SourceKind)
                    ? ConnectionCredentialSourceKinds.Secret
                    : input.SourceKind
            };

            if (string.Equals(field.SourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase))
            {
                field.NamedCredentialName = input.NamedCredentialName;
            }
            else if (!string.IsNullOrWhiteSpace(input.ExistingSecretName))
            {
                field.SecretName = input.ExistingSecretName.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(input.Value))
            {
                var secretName = ConnectionName.OwnedSecretName(connectionName, fieldName);
                var existing = await secretManager.FindAsync(tenantId, secretName, cancellationToken);
                if (existing is null)
                {
                    await secretManager.CreateAsync(tenantId, new CreateSecretRequest
                    {
                        Name = secretName,
                        DisplayName = $"{connectionName} {fieldName}",
                        TypeName = SecretTypeNames.Text,
                        StoreName = SecretStoreNames.Encrypted,
                        Value = input.Value,
                        Tags = ["owned-by-connection", connectionName]
                    }, cancellationToken);
                }
                else
                {
                    await secretManager.RotateAsync(tenantId, secretName, new RotateSecretRequest { Value = input.Value }, cancellationToken);
                }

                field.SecretName = secretName;
            }

            fields[fieldName] = field;
        }

        return fields;
    }

    private async ValueTask DeleteRemovedOwnedSecretsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretField> previous,
        IDictionary<string, ConnectionSecretField> next,
        CancellationToken cancellationToken)
    {
        foreach (var (fieldName, field) in previous)
        {
            if (next.ContainsKey(fieldName))
                continue;
            await DeleteOwnedSecretAsync(tenantId, connectionName, field, cancellationToken);
        }
    }

    private async ValueTask DeleteOwnedSecretsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretField> fields,
        CancellationToken cancellationToken)
    {
        foreach (var field in fields.Values)
            await DeleteOwnedSecretAsync(tenantId, connectionName, field, cancellationToken);
    }

    private async ValueTask DeleteOwnedSecretAsync(
        string tenantId,
        string connectionName,
        ConnectionSecretField field,
        CancellationToken cancellationToken)
    {
        if (field.SecretName is { Length: > 0 } &&
            field.SecretName.StartsWith($"connection:{connectionName}:", StringComparison.Ordinal))
        {
            await secretManager.DeleteAsync(tenantId, field.SecretName, cancellationToken);
        }
    }

    private static void ValidateSettings(IConnectionTypeProvider type, IDictionary<string, string> settings)
    {
        if (!string.Equals(type.Descriptor.Name, ConnectionTypeNames.Http, StringComparison.OrdinalIgnoreCase))
            return;

        var hasBaseUrl = settings.TryGetValue(HttpConnectionSettingKeys.BaseUrl, out var baseUrl) && !string.IsNullOrWhiteSpace(baseUrl);
        var hasHosts = settings.TryGetValue(HttpConnectionSettingKeys.AllowedHosts, out var hosts) && !string.IsNullOrWhiteSpace(hosts);
        if (!hasBaseUrl && !hasHosts)
        {
            throw new ArgumentException(
                "An http connection requires at least one of baseUrl or allowedHosts.",
                nameof(settings));
        }
    }

    private static void ValidateOwner(string owner)
    {
        if (!string.Equals(owner, ConnectionOwners.Tenant, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Only owner '{ConnectionOwners.Tenant}' is valid in v1.", nameof(owner));
    }

    private static ConnectionMetadata ToMetadata(ConnectionDocument document) => new()
    {
        TenantId = document.Definition.TenantId,
        Name = document.Definition.Name,
        DisplayName = document.Definition.DisplayName,
        TypeName = document.Definition.TypeName,
        AuthScheme = document.Definition.AuthScheme,
        Settings = document.Definition.Settings,
        SecretFieldNames = document.Definition.SecretFields.Keys.ToArray(),
        ManagedBy = document.Definition.ManagedBy,
        Owner = document.Definition.Owner,
        Status = document.State.Status,
        LastTestedAt = document.State.LastTestedAt,
        LastUsedAt = document.State.LastUsedAt,
        CreatedAt = document.State.CreatedAt,
        Tags = document.Definition.Tags
    };
}

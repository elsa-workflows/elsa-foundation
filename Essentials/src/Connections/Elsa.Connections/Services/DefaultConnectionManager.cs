using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
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
        await permissionGate.EnsureManageAsync(tenantId, cancellationToken);
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
            await WriteSecretFieldsAsync(tenantId, name, request.SecretFields, document.Definition.SecretFields, cancellationToken);
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

        await permissionGate.EnsureManageAsync(tenantId, cancellationToken);

        if (request.DisplayName is not null)
            document.Definition.DisplayName = request.DisplayName.Trim();
        if (request.Settings is not null)
        {
            ValidateSettings(type, request.Settings);
            if (RetargetsBoundCredential(document, request.Settings))
                await permissionGate.EnsureBindSecretAsync(tenantId, cancellationToken);
            document.Definition.Settings = new Dictionary<string, string>(request.Settings, StringComparer.OrdinalIgnoreCase);
        }
        if (request.Tags is not null)
            document.Definition.Tags = [..request.Tags];
        if (request.SecretFields is not null)
        {
            await EnsureBindSecretAsync(tenantId, request.SecretFields, cancellationToken);
            var previous = document.Definition.SecretFields;
            document.Definition.SecretFields = await MergeSecretFieldsAsync(
                tenantId, document.Definition.Name, previous, request.SecretFields, cancellationToken);
            await DeleteReplacedOwnedSecretsAsync(tenantId, document.Definition.Name, previous, document.Definition.SecretFields, cancellationToken);
        }

        document.State.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(document, cancellationToken);
        return ToMetadata(document);
    }

    public async ValueTask<ConnectionTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default)
    {
        await permissionGate.EnsureTestAsync(tenantId, cancellationToken);

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
                RequiredType = document.Definition.TypeName,
                CheckUsePermission = false
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
        await permissionGate.EnsureManageAsync(tenantId, cancellationToken);
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
        await permissionGate.EnsureManageAsync(tenantId, cancellationToken);
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

    private async ValueTask WriteSecretFieldsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        IDictionary<string, ConnectionSecretField> fields,
        CancellationToken cancellationToken)
    {
        foreach (var (fieldName, input) in inputs)
        {
            var field = await WriteSecretFieldAsync(tenantId, connectionName, fieldName, input, cancellationToken);
            if (field is not null)
                fields[fieldName] = field;
        }
    }

    private async ValueTask<IDictionary<string, ConnectionSecretField>> MergeSecretFieldsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretField> previous,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, ConnectionSecretField>(previous, StringComparer.Ordinal);
        foreach (var (fieldName, input) in inputs)
        {
            if (!CarriesNewSecretValue(input))
                continue;

            var field = await WriteSecretFieldAsync(tenantId, connectionName, fieldName, input, cancellationToken);
            if (field is not null)
                fields[fieldName] = field;
        }

        return fields;
    }

    private async ValueTask<ConnectionSecretField?> WriteSecretFieldAsync(
        string tenantId,
        string connectionName,
        string fieldName,
        ConnectionSecretFieldInput input,
        CancellationToken cancellationToken)
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
            if (string.IsNullOrWhiteSpace(input.NamedCredentialName))
                return null;
            field.NamedCredentialName = input.NamedCredentialName;
            return field;
        }

        if (!string.IsNullOrWhiteSpace(input.ExistingSecretName))
        {
            field.SecretName = input.ExistingSecretName.Trim();
            return field;
        }

        if (string.IsNullOrWhiteSpace(input.Value))
            return null;

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
        return field;
    }

    private static bool CarriesNewSecretValue(ConnectionSecretFieldInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Value) || !string.IsNullOrWhiteSpace(input.ExistingSecretName))
            return true;

        return !string.IsNullOrWhiteSpace(input.NamedCredentialName) &&
            string.Equals(input.SourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RetargetsBoundCredential(ConnectionDocument document, IDictionary<string, string> nextSettings)
    {
        if (!HasBoundExistingCredential(document))
            return false;

        return !SettingEquals(document.Definition.Settings, nextSettings, HttpConnectionSettingKeys.AllowedHosts)
            || !OriginsEqual(document.Definition.Settings, nextSettings)
            || !InsecureFlagsEqual(document.Definition.Settings, nextSettings);
    }

    private static bool HasBoundExistingCredential(ConnectionDocument document)
    {
        var ownedPrefix = $"connection:{document.Definition.Name}:";
        foreach (var field in document.Definition.SecretFields.Values)
        {
            if (!string.IsNullOrWhiteSpace(field.NamedCredentialName) ||
                string.Equals(field.SourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(field.SecretName) &&
                !field.SecretName.StartsWith(ownedPrefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool OriginsEqual(IDictionary<string, string> previous, IDictionary<string, string> next)
    {
        var hasPrevious = TryBaseUri(previous, out var previousUri);
        var hasNext = TryBaseUri(next, out var nextUri);
        if (hasPrevious && hasNext)
            return !HttpOrigin.IsCrossOrigin(previousUri, nextUri);
        return hasPrevious == hasNext;
    }

    private static bool InsecureFlagsEqual(IDictionary<string, string> previous, IDictionary<string, string> next) =>
        AllowInsecure(previous) == AllowInsecure(next);

    private static bool SettingEquals(IDictionary<string, string> previous, IDictionary<string, string> next, string key)
    {
        previous.TryGetValue(key, out var left);
        next.TryGetValue(key, out var right);
        return string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool AllowInsecure(IDictionary<string, string> settings) =>
        settings.TryGetValue(HttpConnectionSettingKeys.AllowInsecureHttp, out var flag) &&
        bool.TryParse(flag, out var allowed) &&
        allowed;

    private static bool TryBaseUri(IDictionary<string, string> settings, out Uri uri)
    {
        uri = null!;
        return settings.TryGetValue(HttpConnectionSettingKeys.BaseUrl, out var baseUrl) &&
            !string.IsNullOrWhiteSpace(baseUrl) &&
            Uri.TryCreate(baseUrl, UriKind.Absolute, out uri!);
    }

    private async ValueTask DeleteReplacedOwnedSecretsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretField> previous,
        IDictionary<string, ConnectionSecretField> next,
        CancellationToken cancellationToken)
    {
        foreach (var (fieldName, field) in previous)
        {
            if (next.TryGetValue(fieldName, out var current) &&
                string.Equals(current.SecretName, field.SecretName, StringComparison.Ordinal))
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
        CreatedAt = document.State.CreatedAt,
        Tags = document.Definition.Tags
    };
}

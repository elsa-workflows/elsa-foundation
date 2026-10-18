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
        ValidateCredentialSources(request.SecretFields);
        await EnsureBindSecretAsync(tenantId, request.SecretFields, cancellationToken);
        await ValidateExistingSecretReferencesAsync(tenantId, request.SecretFields, cancellationToken);

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
            await ValidateOwnedSecretWriteTargetsAsync(tenantId, name, request.SecretFields, previous: null, cancellationToken);
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

        IDictionary<string, ConnectionSecretField>? previousSecretFields = null;
        IDictionary<string, ConnectionSecretField>? nextSecretFields = null;
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
            ValidateCredentialSources(request.SecretFields);
            await EnsureBindSecretAsync(tenantId, request.SecretFields, cancellationToken);
            await ValidateExistingSecretReferencesAsync(tenantId, request.SecretFields, cancellationToken);
            previousSecretFields = document.Definition.SecretFields;
            await ValidateOwnedSecretWriteTargetsAsync(tenantId, document.Definition.Name, request.SecretFields, previousSecretFields, cancellationToken);
            nextSecretFields = await MergeSecretFieldsAsync(
                tenantId, document.Definition.Name, previousSecretFields, request.SecretFields, cancellationToken);
            document.Definition.SecretFields = nextSecretFields;
        }

        document.State.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(document, cancellationToken);
        if (previousSecretFields is not null && nextSecretFields is not null)
            await DeleteReplacedOwnedSecretsAsync(tenantId, document.Definition.Name, previousSecretFields, nextSecretFields, cancellationToken);
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
                 string.Equals(input.SourceKind?.Trim(), ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase));
            if (bindsExisting)
            {
                await permissionGate.EnsureBindSecretAsync(tenantId, cancellationToken);
                return;
            }
        }
    }

    private static void ValidateCredentialSources(IDictionary<string, ConnectionSecretFieldInput> inputs)
    {
        if (inputs is null)
            throw new ArgumentException("Connection credential fields cannot be null.", "secretFields");
        foreach (var (fieldName, input) in inputs)
        {
            if (input is null)
                throw InvalidCredentialSource(fieldName);

            var hasExistingSecret = !string.IsNullOrWhiteSpace(input.ExistingSecretName);
            var hasValue = !string.IsNullOrWhiteSpace(input.Value);
            var hasNamedCredential = !string.IsNullOrWhiteSpace(input.NamedCredentialName);
            var sourceKind = input.SourceKind?.Trim();

            if (string.IsNullOrWhiteSpace(sourceKind))
            {
                if (hasNamedCredential || (hasExistingSecret && hasValue))
                    throw InvalidCredentialSource(fieldName);

                continue;
            }

            if (string.Equals(sourceKind, ConnectionCredentialSourceKinds.Secret, StringComparison.OrdinalIgnoreCase))
            {
                if (hasNamedCredential || (hasExistingSecret && hasValue) || (!hasExistingSecret && !hasValue))
                    throw InvalidCredentialSource(fieldName);
                continue;
            }

            if (string.Equals(sourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase))
            {
                if (!hasNamedCredential || hasExistingSecret || hasValue)
                    throw InvalidCredentialSource(fieldName);
                continue;
            }

            throw InvalidCredentialSource(fieldName);
        }
    }

    private async ValueTask ValidateExistingSecretReferencesAsync(
        string tenantId,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var input in inputs.Values)
        {
            if (string.IsNullOrWhiteSpace(input.ExistingSecretName))
                continue;

            var secret = await secretManager.FindAsync(tenantId, input.ExistingSecretName.Trim(), cancellationToken);
            if (secret is null ||
                !string.Equals(secret.TenantId, tenantId, StringComparison.Ordinal) ||
                !string.Equals(secret.TypeName, SecretTypeNames.Text, StringComparison.OrdinalIgnoreCase) ||
                secret.Status != SecretStatus.Active ||
                secret.CurrentVersion is null ||
                secret.ExpiresAt is { } expiresAt && expiresAt <= now)
            {
                throw new ArgumentException("The selected secret is unavailable for connection credentials.", nameof(inputs));
            }
        }
    }

    private static ArgumentException InvalidCredentialSource(string fieldName) =>
        new($"Connection credential field '{fieldName}' must specify exactly one supported source.", "secretFields");

    private async ValueTask ValidateOwnedSecretWriteTargetsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        IDictionary<string, ConnectionSecretField>? previous,
        CancellationToken cancellationToken)
    {
        foreach (var (fieldName, input) in inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Value))
                continue;

            var ownedSecretName = ConnectionName.OwnedSecretName(connectionName, fieldName);
            if (await secretManager.FindAsync(tenantId, ownedSecretName, cancellationToken) is null)
                continue;

            var canRotateExistingOwnedSecret = previous is not null &&
                WasOwnedSecret(previous, fieldName, ownedSecretName);
            if (!canRotateExistingOwnedSecret)
                throw OwnedSecretNameConflict();
        }
    }

    private static ArgumentException OwnedSecretNameConflict() =>
        new("An existing secret conflicts with this connection's managed credential name.", "input");

    private async ValueTask WriteSecretFieldsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        IDictionary<string, ConnectionSecretField> fields,
        CancellationToken cancellationToken)
    {
        foreach (var (fieldName, input) in inputs)
        {
            var field = await WriteSecretFieldAsync(tenantId, connectionName, fieldName, input, false, false, cancellationToken);
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

            var ownedSecretName = ConnectionName.OwnedSecretName(connectionName, fieldName);
            var canRotateExistingOwnedSecret = WasOwnedSecret(previous, fieldName, ownedSecretName);
            var retainExistingOwnedSecret = WasOwnedSecret(previous, fieldName, input.ExistingSecretName);
            var field = await WriteSecretFieldAsync(
                tenantId, connectionName, fieldName, input, canRotateExistingOwnedSecret, retainExistingOwnedSecret, cancellationToken);
            if (field is not null)
                fields[fieldName] = field;
        }

        return fields;
    }

    private static bool WasOwnedSecret(
        IDictionary<string, ConnectionSecretField> previous,
        string fieldName,
        string? selectedSecretName) =>
        previous.TryGetValue(fieldName, out var previousField) &&
        previousField.IsOwnedSecret &&
        SameSecretName(previousField.SecretName, selectedSecretName);

    private static bool SameSecretName(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private async ValueTask<ConnectionSecretField?> WriteSecretFieldAsync(
        string tenantId,
        string connectionName,
        string fieldName,
        ConnectionSecretFieldInput input,
        bool canRotateExistingOwnedSecret,
        bool retainExistingOwnedSecret,
        CancellationToken cancellationToken)
    {
        var field = new ConnectionSecretField
        {
            FieldName = fieldName,
            SourceKind = string.IsNullOrWhiteSpace(input.SourceKind)
                ? ConnectionCredentialSourceKinds.Secret
                : input.SourceKind.Trim()
        };

        if (string.Equals(field.SourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase))
        {
            field.NamedCredentialName = input.NamedCredentialName;
            return field;
        }

        if (!string.IsNullOrWhiteSpace(input.ExistingSecretName))
        {
            field.SecretName = input.ExistingSecretName.Trim();
            field.IsOwnedSecret = retainExistingOwnedSecret;
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
        else if (canRotateExistingOwnedSecret)
        {
            await secretManager.RotateAsync(tenantId, secretName, new RotateSecretRequest { Value = input.Value }, cancellationToken);
        }
        else
            throw OwnedSecretNameConflict();

        field.SecretName = secretName;
        field.IsOwnedSecret = true;
        return field;
    }

    private static bool CarriesNewSecretValue(ConnectionSecretFieldInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Value) || !string.IsNullOrWhiteSpace(input.ExistingSecretName))
            return true;

        return !string.IsNullOrWhiteSpace(input.NamedCredentialName) &&
            string.Equals(input.SourceKind?.Trim(), ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase);
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
        foreach (var field in document.Definition.SecretFields.Values)
        {
            if (!string.IsNullOrWhiteSpace(field.NamedCredentialName) ||
                string.Equals(field.SourceKind, ConnectionCredentialSourceKinds.NamedCredential, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(field.SecretName) && !field.IsOwnedSecret)
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
                SameSecretName(current.SecretName, field.SecretName))
                continue;
            await DeleteOwnedSecretAsync(tenantId, connectionName, field, next.Values, cancellationToken);
        }
    }

    private async ValueTask DeleteOwnedSecretsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretField> fields,
        CancellationToken cancellationToken)
    {
        foreach (var field in fields.Values)
            await DeleteOwnedSecretAsync(tenantId, connectionName, field, retainedFields: null, cancellationToken);
    }

    private async ValueTask DeleteOwnedSecretAsync(
        string tenantId,
        string connectionName,
        ConnectionSecretField field,
        IEnumerable<ConnectionSecretField>? retainedFields,
        CancellationToken cancellationToken)
    {
        if (!field.IsOwnedSecret || string.IsNullOrWhiteSpace(field.SecretName))
            return;

        if (retainedFields?.Any(retained =>
                SameSecretName(retained.SecretName, field.SecretName)) == true)
            return;

        if (await repository.IsSecretReferencedAsync(tenantId, field.SecretName, connectionName, cancellationToken))
            return;

        await secretManager.DeleteAsync(tenantId, field.SecretName, cancellationToken);
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

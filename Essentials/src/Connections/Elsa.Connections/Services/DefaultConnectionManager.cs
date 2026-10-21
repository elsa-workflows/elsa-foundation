using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Http;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Exceptions;
using Elsa.Secrets.Core.Models;
using Microsoft.Extensions.Logging;

namespace Elsa.Connections.Services;

/// <summary>
/// Default connection lifecycle. How it orders its writes, and how a failure surfaces (see issue #2855):
/// <list type="bullet">
/// <item>
/// <description>
/// Create: the connection row is added first, then the owned secrets are written and the row saved. A failure rolls
/// the row and the secrets back best-effort; each failed step is logged and the create's own failure is the one thrown.
/// </description>
/// </item>
/// <item>
/// <description>
/// Update: a new value for an owned secret the stored connection already resolves is written only after the update's
/// <c>SaveAsync</c> wins, expecting the secret version read first. A request that loses the save leaves what the stored
/// connection resolves unchanged. A secret rotated after that read gives <see cref="ConnectionConflictException"/>
/// (HTTP 409) instead of an overwrite, and so does one soft-deleted or created under the same name by a concurrent
/// writer; for a deleted one the message says a plain retry will not work until Secrets releases deleted names, so the
/// field must be re-pointed with <c>ExistingSecretName</c>. A secret with no active version (revoked or expired) is
/// rotated without an expected version; if Secrets then reports a concurrent writer as a plain
/// <see cref="InvalidOperationException"/> and the secret's version, status or update time differs from what was read,
/// that is a 409 too.
/// </description>
/// </item>
/// <item>
/// <description>
/// After the save every changed field is attempted and the secrets the save stopped referencing are deleted even when a
/// rotation failed. A field that failed for another reason is not hidden behind a 409: it throws
/// <see cref="ConnectionCredentialWriteException"/> (HTTP 500) carrying the field names and the store's failure (an
/// <see cref="AggregateException"/> when several failed, each also logged). A replaced secret that cannot be deleted
/// after a successful rotation throws that exception too; after a failed rotation the cleanup failure is logged and
/// swallowed so the rotation failure is the one reported. The cleanup counts the connection's stored document as a
/// reference, so a concurrent update that re-bound a replaced secret keeps it.
/// </description>
/// </item>
/// <item>
/// <description>
/// Before the save, a store fault throws <see cref="ConnectionStoreException"/> (HTTP 500). A name that Secrets refuses
/// as taken is read back: a secret that exists is a concurrent writer (400, like the managed-name check); one that does
/// not is a deleted secret still holding its name (409, use another connection name or bind an existing secret). A
/// store that cannot hold the credential type is a non-retryable <see cref="ConnectionStoreException"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// A request that loses the save does not roll back a new <c>connection:&lt;name&gt;:&lt;field&gt;</c> secret it created
/// beforehand for a field moving to inline storage: Secrets deletes softly and keeps the name, so removing it would
/// block that field for good. The secret stays Active and unreferenced, and a retried inline write for that field is
/// rejected as a managed-name conflict.
/// </description>
/// </item>
/// </list>
/// </summary>
internal sealed class DefaultConnectionManager(
    IConnectionRepository repository,
    IConnectionTypeRegistry types,
    IAuthenticationSchemeRegistry schemes,
    IConnectionResolver resolver,
    ISecretManager secretManager,
    IConnectionPermissionGate permissionGate,
    TimeProvider timeProvider,
    ILogger<DefaultConnectionManager> logger) : IConnectionManager
{
    public async ValueTask<ConnectionMetadata> CreateAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        ConnectionName.Validate(request.Name);
        try
        {
            return await CreateCoreAsync(tenantId, request, cancellationToken);
        }
        catch (Exception exception) when (IsStoreFault(exception))
        {
            throw StoreFailure(ConnectionName.Normalize(request.Name), "created", exception);
        }
    }

    private async ValueTask<ConnectionMetadata> CreateCoreAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken)
    {
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
            // The request's own failure is the one to report: the rollback never throws and ignores the request's cancellation.
            await RollBackCreateAsync(tenantId, name, document.Definition.SecretFields);
            throw;
        }

        return ToMetadata(document);
    }

    /// <summary>
    /// Removes what a failed create left behind. Every step is attempted whatever the earlier ones did (each owned
    /// secret, then the connection row), each failure is logged, and none escapes, so the create's original failure
    /// is the one the caller sees. A secret that could not be deleted stays Active and unreferenced.
    /// </summary>
    private async ValueTask RollBackCreateAsync(string tenantId, string name, IDictionary<string, ConnectionSecretField> fields)
    {
        foreach (var (fieldName, field) in fields)
        {
            try
            {
                await DeleteOwnedSecretAsync(tenantId, name, field, retainedFields: null, excludedConnectionName: name, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not roll back secret '{SecretName}' of connection '{ConnectionName}' field '{FieldName}' after its create failed; it stays Active and unreferenced.",
                    field.SecretName, name, fieldName);
            }
        }

        try
        {
            await repository.DeleteAsync(tenantId, name, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not remove connection '{ConnectionName}' after its create failed; delete it before creating it again.", name);
        }
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
        try
        {
            return await UpdateCoreAsync(tenantId, name, request, cancellationToken);
        }
        catch (Exception exception) when (IsStoreFault(exception))
        {
            // Nothing after the save escapes raw (the post-save steps throw only the exceptions excluded here), so this is a pre-save failure.
            throw StoreFailure(ConnectionName.Normalize(name), "updated", exception);
        }
    }

    private static bool IsStoreFault(Exception exception) =>
        exception is not (ArgumentException or ConnectionConflictException or ConnectionUnavailableException
            or ConnectionCredentialWriteException or ConnectionStoreException or OperationCanceledException);

    private static ConnectionStoreException StoreFailure(string connectionName, string verb, Exception exception) =>
        new(connectionName,
            $"Connection '{connectionName}' was not {verb}: the connection or secret store failed before the change was confirmed saved. Retry; if the retry reports a credential secret name as taken or held, use a different connection name or bind an existing secret.",
            exception);

    private async ValueTask<ConnectionMetadata?> UpdateCoreAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken)
    {
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
        var ownedSecretValues = new List<OwnedSecretValue>();
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
                tenantId, document.Definition.Name, previousSecretFields, request.SecretFields, ownedSecretValues, cancellationToken);
            document.Definition.SecretFields = nextSecretFields;
        }

        document.State.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(document, cancellationToken);

        // The save has committed, so finish its secret writes even when the request is cancelled now.
        // Cleanup of the secrets the save replaced must run even when a rotation fails, or they stay Active and unreferenced.
        var rotationFailed = true;
        try
        {
            await WriteOwnedSecretValuesAsync(tenantId, document.Definition.Name, ownedSecretValues);
            rotationFailed = false;
        }
        finally
        {
            try
            {
                if (previousSecretFields is not null && nextSecretFields is not null)
                    await DeleteReplacedOwnedSecretsAsync(tenantId, document.Definition.Name, previousSecretFields, nextSecretFields, CancellationToken.None);
            }
            catch when (rotationFailed)
            {
                // The rotation failure is the one the caller needs to see; it must not be replaced by a cleanup failure.
                // DeleteReplacedOwnedSecretsAsync has already logged each secret it could not delete.
            }
        }

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

        foreach (var field in document.Definition.SecretFields.Values)
            await DeleteOwnedSecretAsync(tenantId, document.Definition.Name, field, retainedFields: null, excludedConnectionName: document.Definition.Name, cancellationToken);
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
            var field = await WriteSecretFieldAsync(tenantId, connectionName, fieldName, input, false, cancellationToken);
            if (field is not null)
                fields[fieldName] = field;
        }
    }

    private async ValueTask<IDictionary<string, ConnectionSecretField>> MergeSecretFieldsAsync(
        string tenantId,
        string connectionName,
        IDictionary<string, ConnectionSecretField> previous,
        IDictionary<string, ConnectionSecretFieldInput> inputs,
        ICollection<OwnedSecretValue> ownedSecretValues,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, ConnectionSecretField>(previous, StringComparer.Ordinal);
        foreach (var (fieldName, input) in inputs)
        {
            if (!CarriesNewSecretValue(input))
                continue;

            var ownedSecretName = ConnectionName.OwnedSecretName(connectionName, fieldName);
            if (!string.IsNullOrWhiteSpace(input.Value) && WasOwnedSecret(previous, fieldName, ownedSecretName))
            {
                // The stored document already resolves this secret, so its new value waits for the save to win.
                // A request that loses the save then leaves what the stored document resolves untouched.
                var current = await secretManager.FindAsync(tenantId, ownedSecretName, cancellationToken);
                ownedSecretValues.Add(new OwnedSecretValue(fieldName, ownedSecretName, input.Value, current));
                fields[fieldName] = new ConnectionSecretField
                {
                    FieldName = fieldName,
                    SourceKind = SourceKindOf(input),
                    SecretName = ownedSecretName,
                    IsOwnedSecret = true
                };
                continue;
            }

            var retainExistingOwnedSecret = WasOwnedSecret(previous, fieldName, input.ExistingSecretName);
            var field = await WriteSecretFieldAsync(
                tenantId, connectionName, fieldName, input, retainExistingOwnedSecret, cancellationToken);
            if (field is not null)
                fields[fieldName] = field;
        }

        return fields;
    }

    private async ValueTask WriteOwnedSecretValuesAsync(string tenantId, string connectionName, IEnumerable<OwnedSecretValue> values)
    {
        // Every field is attempted: one that lost a race must not stop the others from taking their new values.
        var conflictedFields = new List<string>();
        var deletedFields = new List<string>();
        var faultedFields = new List<string>();
        var faults = new List<Exception>();
        foreach (var value in values)
        {
            try
            {
                if (value.Current is null)
                {
                    await CreateOwnedSecretAsync(tenantId, connectionName, value.FieldName, value.SecretName, value.Value, CancellationToken.None);
                    continue;
                }

                // Expecting the version read before the save keeps a later writer's value instead of overwriting it.
                await secretManager.RotateAsync(tenantId, value.SecretName, new RotateSecretRequest
                {
                    Value = value.Value,
                    ExpectedVersion = value.Current.CurrentVersion
                }, CancellationToken.None);
            }
            catch (SecretVersionConflictException)
            {
                conflictedFields.Add(value.FieldName);
            }
            catch (Exception exception)
            {
                if (exception is InvalidOperationException)
                {
                    var change = await ConcurrentChangeAsync(tenantId, value);
                    if (change != ConcurrentChange.None)
                    {
                        conflictedFields.Add(value.FieldName);
                        if (change == ConcurrentChange.Deleted)
                            deletedFields.Add(value.FieldName);
                        continue;
                    }
                }

                logger.LogWarning(exception,
                    "Could not write the new value of secret '{SecretName}' for connection '{ConnectionName}' field '{FieldName}' after its update was saved.",
                    value.SecretName, connectionName, value.FieldName);
                faultedFields.Add(value.FieldName);
                faults.Add(exception);
            }
        }

        if (faults.Count > 0)
        {
            // A genuine fault outranks a conflict: reporting a retryable 409 would hide it.
            // The message names every field that was not written; the store's failure(s) travel as the inner exception.
            var unwritten = faultedFields.Concat(conflictedFields).ToArray();
            throw new ConnectionCredentialWriteException(
                connectionName,
                unwritten,
                $"Connection '{connectionName}' credential(s) {QuoteAll(unwritten)} were not written. Its other changes were saved.",
                faults.Count == 1 ? faults[0] : new AggregateException(faults));
        }

        if (conflictedFields.Count > 0)
        {
            throw new ConnectionConflictException(
                connectionName,
                $"Connection '{connectionName}' credential(s) {QuoteAll(conflictedFields)} changed by another request, so this update did not write the new value(s). Its other changes were saved."
                + (deletedFields.Count == 0
                    ? ""
                    : $" Credential(s) {QuoteAll(deletedFields)} were deleted, and a deleted secret's name is not released yet: retrying an inline value fails, so point them at an existing secret instead."));
        }
    }

    private static string QuoteAll(IEnumerable<string> fields) => string.Join(", ", fields.Select(field => $"'{field}'"));

    /// <summary>
    /// Secrets reports a vanished secret (rotate) and a taken name (create) as a plain <see cref="InvalidOperationException"/>.
    /// Reading the secret again tells a concurrent writer apart from a genuine fault.
    /// </summary>
    private async ValueTask<ConcurrentChange> ConcurrentChangeAsync(string tenantId, OwnedSecretValue value)
    {
        try
        {
            var current = await secretManager.FindAsync(tenantId, value.SecretName, CancellationToken.None);
            if (value.Current is null)
                return current is not null ? ConcurrentChange.Changed : ConcurrentChange.None;
            if (current is null)
                return ConcurrentChange.Deleted;
            return ChangedSince(value.Current, current) ? ConcurrentChange.Changed : ConcurrentChange.None;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not re-read secret '{SecretName}' to classify a failed write; reporting the original failure.", value.SecretName);
            return ConcurrentChange.None;
        }
    }

    private enum ConcurrentChange { None, Changed, Deleted }

    /// <summary>
    /// Without an expected version (the secret had no active version when read), Secrets detects a concurrent writer
    /// only through its internal revision and reports it as a plain <see cref="InvalidOperationException"/>. A
    /// change in what the metadata exposes tells that apart from a fault on a secret that did not move.
    /// </summary>
    private static bool ChangedSince(SecretMetadata read, SecretMetadata current) =>
        read.CurrentVersion != current.CurrentVersion || read.Status != current.Status || read.UpdatedAt != current.UpdatedAt;

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
        bool retainExistingOwnedSecret,
        CancellationToken cancellationToken)
    {
        var field = new ConnectionSecretField
        {
            FieldName = fieldName,
            SourceKind = SourceKindOf(input)
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
        if (await secretManager.FindAsync(tenantId, secretName, cancellationToken) is not null)
            throw OwnedSecretNameConflict();

        try
        {
            await CreateOwnedSecretAsync(tenantId, connectionName, fieldName, secretName, input.Value, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsNameTaken(exception))
        {
            throw await TakenOwnedSecretNameAsync(tenantId, connectionName, fieldName, secretName, exception, cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsStoreMisconfiguration(exception))
        {
            throw new ConnectionStoreException(connectionName,
                $"Connection '{connectionName}' was not saved: the secret store cannot hold its credentials as configured. Retrying will not help; the secret store configuration must be fixed.",
                exception);
        }
        field.SecretName = secretName;
        field.IsOwnedSecret = true;
        return field;
    }

    // Secrets reports both as a plain InvalidOperationException, so the message is the only thing that tells them apart.
    private static bool IsNameTaken(InvalidOperationException exception) =>
        exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private static bool IsStoreMisconfiguration(InvalidOperationException exception) =>
        exception.Message.Contains("not compatible with the selected store", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Secrets refused to create an owned secret because its name is taken, although the pre-save check found none.
    /// A secret that reads back now was created by a concurrent writer. One that does not is a deleted secret whose
    /// name Secrets still holds, and retrying the same inline value cannot succeed.
    /// </summary>
    private async ValueTask<Exception> TakenOwnedSecretNameAsync(
        string tenantId,
        string connectionName,
        string fieldName,
        string secretName,
        InvalidOperationException nameTaken,
        CancellationToken cancellationToken)
    {
        if (await secretManager.FindAsync(tenantId, secretName, cancellationToken) is not null)
            return OwnedSecretNameConflict();

        logger.LogWarning(nameTaken,
            "Secret name '{SecretName}' for connection '{ConnectionName}' field '{FieldName}' is held by a deleted secret.",
            secretName, connectionName, fieldName);
        return new ConnectionConflictException(
            connectionName,
            $"Connection '{connectionName}' was not saved: the secret name for credential '{fieldName}' is still held by a deleted secret. Use a different connection name, or bind an existing secret to that credential.");
    }

    private static string SourceKindOf(ConnectionSecretFieldInput input) =>
        string.IsNullOrWhiteSpace(input.SourceKind)
            ? ConnectionCredentialSourceKinds.Secret
            : input.SourceKind.Trim();

    private async ValueTask CreateOwnedSecretAsync(
        string tenantId,
        string connectionName,
        string fieldName,
        string secretName,
        string value,
        CancellationToken cancellationToken) =>
        await secretManager.CreateAsync(tenantId, new CreateSecretRequest
        {
            Name = secretName,
            DisplayName = $"{connectionName} {fieldName}",
            TypeName = SecretTypeNames.Text,
            StoreName = SecretStoreNames.Encrypted,
            Value = value,
            Tags = ["owned-by-connection", connectionName]
        }, cancellationToken);

    /// <summary>A new value for an owned secret the stored document already resolves, written only after the save wins.</summary>
    private sealed record OwnedSecretValue(string FieldName, string SecretName, string Value, SecretMetadata? Current);

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
        // Every replaced field is attempted: one secret that cannot be deleted must not leave the others Active and unreferenced.
        var failedFields = new List<string>();
        var failures = new List<Exception>();
        foreach (var (fieldName, field) in previous)
        {
            if (next.TryGetValue(fieldName, out var current) &&
                SameSecretName(current.SecretName, field.SecretName))
                continue;

            try
            {
                // No connection is excluded: the stored document (this save, or a concurrent update after it) counts as a reference.
                await DeleteOwnedSecretAsync(tenantId, connectionName, field, next.Values, excludedConnectionName: null, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not delete secret '{SecretName}' that connection '{ConnectionName}' field '{FieldName}' no longer references; it stays Active and unreferenced.",
                    field.SecretName, connectionName, fieldName);
                failedFields.Add(fieldName);
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new ConnectionCredentialWriteException(
                connectionName,
                failedFields,
                $"Connection '{connectionName}' was updated, but the replaced secret(s) of credential(s) {QuoteAll(failedFields)} could not be deleted and stay Active and unreferenced.",
                failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }
    }

    private async ValueTask DeleteOwnedSecretAsync(
        string tenantId,
        string connectionName,
        ConnectionSecretField field,
        IEnumerable<ConnectionSecretField>? retainedFields,
        string? excludedConnectionName,
        CancellationToken cancellationToken)
    {
        if (!field.IsOwnedSecret || string.IsNullOrWhiteSpace(field.SecretName))
            return;

        if (retainedFields?.Any(retained =>
                SameSecretName(retained.SecretName, field.SecretName)) == true)
            return;

        if (await repository.IsSecretReferencedAsync(tenantId, field.SecretName, excludedConnectionName, cancellationToken))
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

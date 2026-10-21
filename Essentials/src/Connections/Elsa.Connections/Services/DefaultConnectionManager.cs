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
/// Create and Update check the request's shape, then ask the permission gate, then run the checks that read secrets and
/// write (Create adds its row before the last of those checks). Before the gate, Create checks the name, owner, type,
/// scheme, settings and credential sources. Update first reads the stored connection (a missing one returns null) and
/// checks its type and scheme, then the request's settings, against that type, and its credential sources. After the
/// gate come the bind-secret gate and whether each referenced secret is available. Update then checks whether an
/// existing secret holds a managed credential name before it writes. Create first adds the connection row, so a taken
/// name is a 409, then makes that managed-name check (a 400 rolls the row back), writes the owned secrets and saves the
/// row.
/// </description>
/// </item>
/// <item>
/// <description>
/// A create or update that fails before its save is confirmed rolls back best-effort, without the request's
/// cancellation. A create deletes its row only while the row is still at the version the create added it at, so a
/// connection another request has saved since is kept. Both then delete each owned secret they created, unless a stored
/// connection references it (a save that committed and then failed). Each failed step is logged, and the request's own
/// failure is the one thrown.
/// </description>
/// </item>
/// <item>
/// <description>
/// Update: a new value for an owned secret the stored connection already resolves is written only after the update's
/// <c>SaveAsync</c> wins, expecting the secret version read first. A request that loses the save leaves what the stored
/// connection resolves unchanged. A secret rotated after that read gives <see cref="ConnectionConflictException"/>
/// (HTTP 409) instead of an overwrite, and so does one deleted, or created under the same name, by a concurrent
/// writer. A secret with no active version (revoked or expired) is rotated without an expected version; if Secrets
/// then reports a concurrent writer as a plain <see cref="InvalidOperationException"/> and the secret's version, status
/// or update time differs from what was read, that is a 409 too.
/// </description>
/// </item>
/// <item>
/// <description>
/// After the save every changed field is attempted and the secrets the save stopped referencing are deleted even when a
/// rotation failed. A field that failed for another reason is not hidden behind a 409: it throws
/// <see cref="ConnectionCredentialWriteException"/> (HTTP 500) carrying the field names and the store's failure (an
/// <see cref="AggregateException"/> when several failed), logged at Error. A replaced secret that cannot be deleted
/// after a successful rotation throws that exception too; after a failed rotation the cleanup failure is logged as a
/// warning and not thrown, so the rotation failure is the one reported. The cleanup counts the connection's stored
/// document as a reference, so a concurrent update that re-bound a replaced secret keeps it.
/// </description>
/// </item>
/// <item>
/// <description>
/// Before the save, a store fault throws <see cref="ConnectionStoreException"/> (HTTP 500), and the store's failure is
/// logged at Error. A name Secrets refuses with <see cref="SecretNameInUseException"/> was taken by a concurrent writer
/// after the managed-name check, so it is that check's <see cref="ArgumentException"/> (HTTP 400). A store or type
/// Secrets cannot use (<see cref="SecretStoreConfigurationException"/>) is a <see cref="ConnectionStoreException"/>
/// saying that retrying will not help. A programmer error (<see cref="NullReferenceException"/>,
/// <see cref="InvalidCastException"/>, <see cref="NotImplementedException"/>, <see cref="NotSupportedException"/>,
/// <see cref="ObjectDisposedException"/>) is not a store fault and propagates unwrapped.
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
            throw PreSaveFailure(ConnectionName.Normalize(request.Name), "create", exception);
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
        ValidateCredentialSources(request.SecretFields);
        await permissionGate.EnsureManageAsync(tenantId, cancellationToken);
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

        // Read before the save, which may write the next version back onto the document.
        var addedVersion = document.State.Version;
        if (!await repository.TryAddAsync(document, cancellationToken))
            throw new ConnectionConflictException(name);

        var createdOwnedSecrets = new List<ConnectionSecretField>();
        try
        {
            await ValidateOwnedSecretWriteTargetsAsync(tenantId, name, request.SecretFields, previous: null, cancellationToken);
            await WriteSecretFieldsAsync(tenantId, name, request.SecretFields, document.Definition.SecretFields, createdOwnedSecrets, cancellationToken);
            await repository.SaveAsync(document, cancellationToken);
        }
        catch
        {
            // The request's own failure is the one to report: the rollback never throws and ignores the request's cancellation.
            await RollBackCreateAsync(tenantId, name, addedVersion, createdOwnedSecrets);
            throw;
        }

        return ToMetadata(document);
    }

    /// <summary>
    /// Removes what a failed create left behind: first the connection row, but only while it is still at the version
    /// this create added it at, then the owned secrets it created. Every step is attempted whatever the earlier ones
    /// did, each failure is logged, and none escapes, so the create's original failure is the one the caller sees.
    /// </summary>
    private async ValueTask RollBackCreateAsync(string tenantId, string name, int addedVersion, IReadOnlyCollection<ConnectionSecretField> createdOwnedSecrets)
    {
        try
        {
            // A row at another version was saved since (this create's own save committing before it failed, or another
            // request's connection after a concurrent delete), so it stays, and so do the secrets it references.
            await repository.DeleteAsync(tenantId, name, addedVersion, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception,
                "Could not remove connection '{ConnectionName}' after its create failed; delete it before creating it again.", name);
        }

        await RollBackOwnedSecretsAsync(tenantId, name, createdOwnedSecrets, "create");
    }

    /// <summary>
    /// Deletes the owned secrets a failed create or update created before its save was confirmed. Each is attempted
    /// whatever the others did, and one that a stored connection references (a save that committed and then failed)
    /// is kept. Each failure is logged and none escapes. A secret that could not be deleted stays Active and
    /// unreferenced.
    /// </summary>
    private async ValueTask RollBackOwnedSecretsAsync(
        string tenantId,
        string connectionName,
        IReadOnlyCollection<ConnectionSecretField> createdOwnedSecrets,
        string operation)
    {
        foreach (var field in createdOwnedSecrets)
        {
            try
            {
                await DeleteOwnedSecretAsync(tenantId, connectionName, field, retainedFields: null, excludedConnectionName: null, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception,
                    "Could not roll back secret '{SecretName}' of connection '{ConnectionName}' field '{FieldName}' after its {Operation} failed; it stays Active and unreferenced.",
                    field.SecretName, connectionName, field.FieldName, operation);
            }
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
        catch (ConnectionCredentialWriteException failure)
        {
            logger.LogError(failure.InnerException,
                "Could not finish updating connection '{ConnectionName}': credential fields {FieldNames} failed after the update was saved.",
                failure.ConnectionName, failure.FieldNames);
            throw;
        }
        catch (Exception exception) when (IsStoreFault(exception))
        {
            // Nothing after the save escapes raw (the post-save steps throw only the exceptions excluded here), so this is a pre-save failure.
            throw PreSaveFailure(ConnectionName.Normalize(name), "update", exception);
        }
    }

    /// <summary>
    /// Decides by exclusion, so a store's own exception types (which Connections cannot name) are wrapped and never
    /// cross the connection boundary raw. The connection outcomes and cancellation are not faults, and neither are
    /// programmer errors, which propagate unwrapped instead of carrying retry advice.
    /// </summary>
    private static bool IsStoreFault(Exception exception) =>
        exception is not (ArgumentException or ConnectionConflictException or ConnectionUnavailableException
            or ConnectionCredentialWriteException or ConnectionStoreException or OperationCanceledException
            or NullReferenceException or InvalidCastException or NotImplementedException or NotSupportedException
            or ObjectDisposedException);

    /// <param name="operation"><c>create</c> or <c>update</c>.</param>
    private ConnectionStoreException PreSaveFailure(string connectionName, string operation, Exception exception)
    {
        var retryable = exception is not SecretStoreConfigurationException;
        logger.LogError(exception,
            "Could not {Operation} connection '{ConnectionName}': a store failed before the change was confirmed saved. Retryable: {Retryable}.",
            operation, connectionName, retryable);
        return new ConnectionStoreException(
            connectionName,
            retryable
                ? $"Connection '{connectionName}' may not have been {operation}d: the connection or secret store failed before the change was confirmed saved. Retry; if the retry reports a credential secret name as taken, use a different connection name or bind an existing secret."
                : $"Connection '{connectionName}' was not {operation}d: the secret store cannot hold its credentials as configured. Retrying will not help; the secret store configuration must be fixed.",
            exception);
    }

    private async ValueTask<ConnectionMetadata?> UpdateCoreAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken)
    {
        var document = await repository.FindAsync(tenantId, ConnectionName.Normalize(name), cancellationToken);
        if (document is null)
            return null;

        var type = types.Find(document.Definition.TypeName)
            ?? throw new ArgumentException($"Unknown connection type '{document.Definition.TypeName}'.", nameof(request));
        if (schemes.Find(document.Definition.AuthScheme) is null)
            throw new ArgumentException($"Unknown authentication scheme '{document.Definition.AuthScheme}'.", nameof(request));
        if (request.Settings is not null)
            ValidateSettings(type, request.Settings);
        if (request.SecretFields is not null)
            ValidateCredentialSources(request.SecretFields);

        await permissionGate.EnsureManageAsync(tenantId, cancellationToken);

        IDictionary<string, ConnectionSecretField>? previousSecretFields = null;
        IDictionary<string, ConnectionSecretField>? nextSecretFields = null;
        var ownedSecretValues = new List<OwnedSecretValue>();
        if (request.DisplayName is not null)
            document.Definition.DisplayName = request.DisplayName.Trim();
        if (request.Settings is not null)
        {
            if (RetargetsBoundCredential(document, request.Settings))
                await permissionGate.EnsureBindSecretAsync(tenantId, cancellationToken);
            document.Definition.Settings = new Dictionary<string, string>(request.Settings, StringComparer.OrdinalIgnoreCase);
        }
        if (request.Tags is not null)
            document.Definition.Tags = [..request.Tags];

        var createdOwnedSecrets = new List<ConnectionSecretField>();
        try
        {
            if (request.SecretFields is not null)
            {
                await EnsureBindSecretAsync(tenantId, request.SecretFields, cancellationToken);
                await ValidateExistingSecretReferencesAsync(tenantId, request.SecretFields, cancellationToken);
                previousSecretFields = document.Definition.SecretFields;
                await ValidateOwnedSecretWriteTargetsAsync(tenantId, document.Definition.Name, request.SecretFields, previousSecretFields, cancellationToken);
                nextSecretFields = await MergeSecretFieldsAsync(
                    tenantId, document.Definition.Name, previousSecretFields, request.SecretFields, ownedSecretValues, createdOwnedSecrets, cancellationToken);
                document.Definition.SecretFields = nextSecretFields;
            }

            document.State.UpdatedAt = timeProvider.GetUtcNow();
            await repository.SaveAsync(document, cancellationToken);
        }
        catch
        {
            // Not confirmed saved, so the secrets created for fields moving to inline storage go. One the stored
            // connection references, after a save that committed and then failed, is kept.
            await RollBackOwnedSecretsAsync(tenantId, document.Definition.Name, createdOwnedSecrets, "update");
            throw;
        }

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
            catch (ConnectionCredentialWriteException cleanupFailure) when (rotationFailed)
            {
                // The rotation failure is the one the caller needs to see; it must not be replaced by a cleanup failure.
                logger.LogWarning(cleanupFailure.InnerException,
                    "Could not delete the replaced secrets of connection '{ConnectionName}' credential fields {FieldNames}; not reported because a credential write failed first. They stay Active and unreferenced.",
                    cleanupFailure.ConnectionName, cleanupFailure.FieldNames);
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
        ICollection<ConnectionSecretField> createdOwnedSecrets,
        CancellationToken cancellationToken)
    {
        foreach (var (fieldName, input) in inputs)
        {
            var field = await WriteSecretFieldAsync(tenantId, connectionName, fieldName, input, false, createdOwnedSecrets, cancellationToken);
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
        ICollection<ConnectionSecretField> createdOwnedSecrets,
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
                tenantId, connectionName, fieldName, input, retainExistingOwnedSecret, createdOwnedSecrets, cancellationToken);
            if (field is not null)
                fields[fieldName] = field;
        }

        return fields;
    }

    private async ValueTask WriteOwnedSecretValuesAsync(string tenantId, string connectionName, IEnumerable<OwnedSecretValue> values)
    {
        // Every field is attempted: one that lost a race must not stop the others from taking their new values.
        var conflictedFields = new List<string>();
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
            catch (Exception exception) when (exception is SecretVersionConflictException or SecretNameInUseException)
            {
                // A later rotation, or (for a deferred create) a concurrent writer that took the name.
                conflictedFields.Add(value.FieldName);
            }
            catch (Exception exception)
            {
                if (exception is InvalidOperationException and not SecretStoreConfigurationException &&
                    value.Current is not null &&
                    await ChangedConcurrentlyAsync(tenantId, value.SecretName, value.Current))
                {
                    conflictedFields.Add(value.FieldName);
                    continue;
                }

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
                $"Connection '{connectionName}' credential(s) {QuoteAll(conflictedFields)} changed by another request, so this update did not write the new value(s). Its other changes were saved.");
        }
    }

    private static string QuoteAll(IEnumerable<string> fields) => string.Join(", ", fields.Select(field => $"'{field}'"));

    /// <summary>
    /// Secrets reports a rotate of a secret that vanished, or (without an expected version) one a concurrent writer
    /// moved, as a plain <see cref="InvalidOperationException"/>. Reading the secret again tells a concurrent writer
    /// apart from a genuine fault: it is gone, or its version, status or update time differs from what was read.
    /// </summary>
    private async ValueTask<bool> ChangedConcurrentlyAsync(string tenantId, string secretName, SecretMetadata read)
    {
        try
        {
            var current = await secretManager.FindAsync(tenantId, secretName, CancellationToken.None);
            return current is null ||
                read.CurrentVersion != current.CurrentVersion || read.Status != current.Status || read.UpdatedAt != current.UpdatedAt;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not re-read secret '{SecretName}' to classify a failed write; reporting the original failure.", secretName);
            return false;
        }
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
        bool retainExistingOwnedSecret,
        ICollection<ConnectionSecretField> createdOwnedSecrets,
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
        catch (SecretNameInUseException)
        {
            // A deleted secret frees its name, so the holder is live: a concurrent writer created it after the check above.
            throw OwnedSecretNameConflict();
        }

        field.SecretName = secretName;
        field.IsOwnedSecret = true;
        createdOwnedSecrets.Add(field);
        return field;
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

using System.Diagnostics.CodeAnalysis;
using Elsa.Primitives.Persistence;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Events;
using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Services;

public sealed class DefaultSecretManager(
    ISecretRepository repository,
    ISecretNameValidator nameValidator,
    ISecretStoreRegistry storeRegistry,
    ISecretTypeRegistry typeRegistry,
    ISecretAuditSink auditSink,
    SecretLifecyclePolicy lifecyclePolicy,
    SecretModelMapper mapper,
    TimeProvider timeProvider) : ISecretManager
{
    public ValueTask<SecretMetadata> CreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default)
        => AuditFailureAsync("create", request.Name, async () =>
        {
            if (!nameValidator.IsValid(request.Name, out var error))
                throw new ArgumentException(error, nameof(request));

            var normalizedName = nameValidator.Normalize(request.Name);
            var store = storeRegistry.Get(request.StoreName);
            var typeProvider = typeRegistry.Get(request.TypeName);
            EnsureStoreIsSupported(typeProvider, store.Descriptor.Name);
            var validation = await typeProvider.ValidateCreateAsync(request, cancellationToken);

            if (!validation.Succeeded)
                throw new ArgumentException(validation.Error, nameof(request));

            var now = timeProvider.GetUtcNow();
            var secret = new Secret
            {
                Name = normalizedName,
                DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Name.Trim() : request.DisplayName.Trim(),
                Description = request.Description,
                TypeName = request.TypeName,
                StoreName = request.StoreName,
                Scope = request.Scope,
                Tags = new HashSet<string>(request.Tags, StringComparer.OrdinalIgnoreCase),
                CreatedAt = now
            };
            var version = new SecretVersion
            {
                Version = 1,
                CreatedAt = now,
                ExpiresAt = request.ExpiresAt,
                Payload = ToPayload(request.Value, request.ConfigurationKey, request.Metadata)
            };

            version.Payload = await store.WriteAsync(new SecretWriteContext(secret, version, version.Payload), cancellationToken);
            secret.Versions.Add(version);

            if (!await repository.TryAddAsync(secret, cancellationToken))
                throw new InvalidOperationException("A secret with the same name already exists.");

            await RecordAsync("create", normalizedName, "succeeded", null, cancellationToken);
            return mapper.Map(secret);
        }, cancellationToken);

    public async ValueTask<SecretMetadata?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);

        if (!IsPubliclyVisible(secret))
            return null;

        return mapper.Map(secret);
    }

    public async ValueTask<Page<SecretMetadata>> ListAsync(SecretQuery query, CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize ?? 50, 1, 250);
        var page = Math.Max(query.Page ?? 0, 0);
        var skip = (int)Math.Min((long)page * pageSize, int.MaxValue);
        var result = await repository.ListPageAsync(
            new SecretRepositoryListRequest(
                query.Search,
                query.TypeName,
                query.TypeNames.ToArray(),
                query.StoreName,
                query.StoreNames.ToArray(),
                query.Scope,
                query.Status,
                excludedStatus: SecretStatus.Deleted,
                activeOnly: query.ActiveOnly,
                now: timeProvider.GetUtcNow(),
                skip: skip,
                take: pageSize),
            cancellationToken);
        var items = result.Items.Select(mapper.Map).ToArray();

        return Page.Of<SecretMetadata>(items, result.TotalCount);
    }

    public ValueTask<SecretMetadata> UpdateAsync(string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default)
        => AuditFailureAsync("update", name, async () =>
        {
            var loaded = await GetExistingSecretWithRevisionAsync(name, cancellationToken);
            var secret = loaded.Secret;

            if (request.DisplayName is not null)
                secret.DisplayName = request.DisplayName.Trim();

            if (request.Description is not null)
                secret.Description = request.Description;

            secret.UpdatedAt = timeProvider.GetUtcNow();
            await SaveExistingSecretAsync(secret, loaded.Revision, cancellationToken);
            await RecordAsync("update", secret.Name, "succeeded", null, cancellationToken);
            return mapper.Map(secret);
        }, cancellationToken);

    public ValueTask<SecretMetadata> RotateAsync(string name, RotateSecretRequest request, CancellationToken cancellationToken = default)
        => AuditFailureAsync("rotate", name, async () =>
        {
            var loaded = await GetExistingSecretWithRevisionAsync(name, cancellationToken);
            var secret = loaded.Secret;
            var store = storeRegistry.Get(secret.StoreName);
            var typeProvider = typeRegistry.Get(secret.TypeName);
            EnsureStoreIsSupported(typeProvider, store.Descriptor.Name);
            var validation = await typeProvider.ValidateRotateAsync(request, secret.StoreName, cancellationToken);

            if (!validation.Succeeded)
                throw new ArgumentException(validation.Error, nameof(request));

            foreach (var activeVersion in secret.Versions.Where(x => x.Status == SecretStatus.Active))
                activeVersion.Status = SecretStatus.Retired;

            var now = timeProvider.GetUtcNow();
            var version = new SecretVersion
            {
                Version = secret.Versions.Count == 0 ? 1 : secret.Versions.Max(x => x.Version) + 1,
                CreatedAt = now,
                ExpiresAt = request.ExpiresAt,
                Payload = ToPayload(request.Value, request.ConfigurationKey, request.Metadata)
            };

            version.Payload = await store.WriteAsync(new SecretWriteContext(secret, version, version.Payload), cancellationToken);
            secret.Versions.Add(version);
            secret.Status = SecretStatus.Active;
            secret.UpdatedAt = now;

            await SaveExistingSecretAsync(secret, loaded.Revision, cancellationToken);
            await RecordAsync("rotate", secret.Name, "succeeded", null, cancellationToken);
            return mapper.Map(secret);
        }, cancellationToken);

    public ValueTask<SecretMetadata?> RevokeAsync(string name, CancellationToken cancellationToken = default)
        => AuditFailureAsync("revoke", name, async () =>
        {
            var loaded = await TryGetExistingSecretWithRevisionAsync(name, cancellationToken);
            if (loaded is null)
                return (SecretMetadata?)null;

            var secret = loaded.Secret;
            secret.Status = SecretStatus.Revoked;
            foreach (var activeVersion in secret.Versions.Where(x => x.Status == SecretStatus.Active))
                activeVersion.Status = SecretStatus.Revoked;

            secret.UpdatedAt = timeProvider.GetUtcNow();
            await SaveExistingSecretAsync(secret, loaded.Revision, cancellationToken);
            await RecordAsync("revoke", secret.Name, "succeeded", null, cancellationToken);
            return mapper.Map(secret);
        }, cancellationToken);

    public ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
        => AuditFailureAsync("delete", name, async () =>
        {
            var loaded = await TryGetExistingSecretWithRevisionAsync(name, cancellationToken);
            if (loaded is null)
                return false;

            var secret = loaded.Secret;
            var store = storeRegistry.Get(secret.StoreName);
            await store.DeleteAsync(new SecretDeleteContext(secret), cancellationToken);

            secret.Status = SecretStatus.Deleted;
            secret.UpdatedAt = timeProvider.GetUtcNow();
            await SaveExistingSecretAsync(secret, loaded.Revision, cancellationToken);
            await RecordAsync("delete", secret.Name, "succeeded", null, cancellationToken);
            return true;
        }, cancellationToken);

    public async ValueTask<SecretTestResult> TestAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);

        if (!IsPublicOperationAllowed(secret))
            return SecretTestResult.Failure("not-found", "Secret not found.");

        var version = secret.LatestActiveVersion;

        if (version is null)
            return SecretTestResult.Failure("inactive", "Secret has no active version.");

        var store = storeRegistry.Get(secret.StoreName);
        var result = await store.TestAsync(new SecretTestContext(secret, version), cancellationToken);
        await RecordAsync("test", secret.Name, result.Succeeded ? "succeeded" : "failed", result.Code, cancellationToken);
        return result;
    }

    private async ValueTask<Secret> GetExistingSecretAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);

        if (!IsPublicOperationAllowed(secret))
            throw new InvalidOperationException("Secret not found.");

        return secret;
    }

    private async ValueTask<SecretRevisionedRecord> GetExistingSecretWithRevisionAsync(string name, CancellationToken cancellationToken)
    {
        var loaded = await TryGetExistingSecretWithRevisionAsync(name, cancellationToken);
        if (loaded is null)
            throw new InvalidOperationException("Secret not found.");

        return loaded;
    }

    private async ValueTask<SecretRevisionedRecord?> TryGetExistingSecretWithRevisionAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = nameValidator.Normalize(name);
        if (repository is IRevisionAwareSecretRepository revisionAware)
        {
            var loaded = await revisionAware.FindWithRevisionAsync(normalizedName, cancellationToken);
            return IsPublicOperationAllowed(loaded?.Secret) ? loaded : null;
        }

        var secret = await repository.FindAsync(normalizedName, cancellationToken);
        return IsPublicOperationAllowed(secret) ? new SecretRevisionedRecord(secret, "") : null;
    }

    private async ValueTask SaveExistingSecretAsync(Secret secret, string? revision, CancellationToken cancellationToken)
    {
        if (repository is not IRevisionAwareSecretRepository revisionAware)
        {
            await repository.SaveAsync(secret, cancellationToken);
            return;
        }

        var result = await revisionAware.SaveWithRevisionAsync(secret, revision, cancellationToken);
        if (result.Status is SecretRevisionSaveStatus.Saved)
            return;

        throw new InvalidOperationException(
            result.Status is SecretRevisionSaveStatus.NotFound
                ? "Secret not found."
                : "Secret changed concurrently.");
    }

    private bool IsPubliclyVisible([NotNullWhen(true)] Secret? secret)
        => secret is not null && lifecyclePolicy.EvaluatePublicVisibility(secret).Allowed;

    private bool IsPublicOperationAllowed([NotNullWhen(true)] Secret? secret)
        => secret is not null && lifecyclePolicy.EvaluatePublicOperation(secret).Allowed;

    private static SecretPayload ToPayload(string? value, string? configurationKey, IDictionary<string, string> metadata)
    {
        var payload = new SecretPayload
        {
            Value = value,
            Metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrWhiteSpace(configurationKey))
            payload.Metadata["configurationKey"] = configurationKey.Trim();

        return payload;
    }

    private ValueTask RecordAsync(string operation, string secretName, string outcome, string? reason, CancellationToken cancellationToken)
        => auditSink.RecordAsync(new SecretOperationAuditRecord(operation, secretName, outcome, timeProvider.GetUtcNow(), Reason: reason), cancellationToken);

    // Wraps a mutating operation so that a thrown exception is recorded as a failed audit event
    // before being rethrown. Only the exception type name is recorded as the reason so no secret
    // material can leak into the audit trail.
    private async ValueTask<T> AuditFailureAsync<T>(string operation, string secretName, Func<ValueTask<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (Exception exception)
        {
            await RecordAsync(operation, secretName, "failed", exception.GetType().Name, cancellationToken);
            throw;
        }
    }

    private static void EnsureStoreIsSupported(ISecretTypeProvider typeProvider, string storeName)
    {
        if (!typeProvider.Descriptor.SupportedStoreNames.Contains(storeName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Secret type is not compatible with the selected store.");
    }
}

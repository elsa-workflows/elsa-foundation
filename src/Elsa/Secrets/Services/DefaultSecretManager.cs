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
    SecretModelMapper mapper,
    TimeProvider timeProvider) : ISecretManager
{
    public async ValueTask<SecretMetadata> CreateAsync(CreateSecretRequest request, CancellationToken cancellationToken = default)
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
    }

    public async ValueTask<SecretMetadata?> FindAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);
        return secret is null ? null : mapper.Map(secret);
    }

    public async ValueTask<Page<SecretMetadata>> ListAsync(SecretQuery query, CancellationToken cancellationToken = default)
    {
        var secrets = await repository.ListAsync(cancellationToken);
        var filtered = secrets.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            filtered = filtered.Where(x => x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.TypeName))
            filtered = filtered.Where(x => string.Equals(x.TypeName, query.TypeName, StringComparison.OrdinalIgnoreCase));

        if (query.TypeNames.Count > 0)
            filtered = filtered.Where(x => query.TypeNames.Contains(x.TypeName, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.StoreName))
            filtered = filtered.Where(x => string.Equals(x.StoreName, query.StoreName, StringComparison.OrdinalIgnoreCase));

        if (query.StoreNames.Count > 0)
            filtered = filtered.Where(x => query.StoreNames.Contains(x.StoreName, StringComparer.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(query.Scope))
            filtered = filtered.Where(x => string.Equals(x.Scope, query.Scope, StringComparison.OrdinalIgnoreCase));

        if (query.Status is not null)
            filtered = filtered.Where(x => x.Status == query.Status);

        if (query.ActiveOnly)
            filtered = filtered.Where(x => x.Status == SecretStatus.Active && x.LatestActiveVersion is not null);

        var ordered = filtered.OrderBy(x => x.Name).ToArray();
        var totalCount = ordered.Length;
        var pageSize = Math.Clamp(query.PageSize ?? 50, 1, 250);
        var page = Math.Max(query.Page ?? 0, 0);
        var items = ordered.Skip(page * pageSize).Take(pageSize).Select(mapper.Map).ToArray();

        return Page.Of<SecretMetadata>(items, totalCount);
    }

    public async ValueTask<SecretMetadata> UpdateAsync(string name, UpdateSecretMetadataRequest request, CancellationToken cancellationToken = default)
    {
        var secret = await GetExistingSecretAsync(name, cancellationToken);

        if (request.DisplayName is not null)
            secret.DisplayName = request.DisplayName.Trim();

        if (request.Description is not null)
            secret.Description = request.Description;

        secret.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(secret, cancellationToken);
        await RecordAsync("update", secret.Name, "succeeded", null, cancellationToken);
        return mapper.Map(secret);
    }

    public async ValueTask<SecretMetadata> RotateAsync(string name, RotateSecretRequest request, CancellationToken cancellationToken = default)
    {
        var secret = await GetExistingSecretAsync(name, cancellationToken);
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

        await repository.SaveAsync(secret, cancellationToken);
        await RecordAsync("rotate", secret.Name, "succeeded", null, cancellationToken);
        return mapper.Map(secret);
    }

    public async ValueTask<SecretMetadata?> RevokeAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);

        if (secret is null)
            return null;

        secret.Status = SecretStatus.Revoked;
        foreach (var activeVersion in secret.Versions.Where(x => x.Status == SecretStatus.Active))
            activeVersion.Status = SecretStatus.Revoked;

        secret.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(secret, cancellationToken);
        await RecordAsync("revoke", secret.Name, "succeeded", null, cancellationToken);
        return mapper.Map(secret);
    }

    public async ValueTask<bool> DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);

        if (secret is null)
            return false;

        var store = storeRegistry.Get(secret.StoreName);
        await store.DeleteAsync(new SecretDeleteContext(secret), cancellationToken);

        secret.Status = SecretStatus.Deleted;
        secret.UpdatedAt = timeProvider.GetUtcNow();
        await repository.SaveAsync(secret, cancellationToken);
        await RecordAsync("delete", secret.Name, "succeeded", null, cancellationToken);
        return true;
    }

    public async ValueTask<SecretTestResult> TestAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);

        if (secret is null)
            return SecretTestResult.Failure("not-found", "Secret not found.");

        var version = secret.LatestActiveVersion;

        if (version is null)
            return SecretTestResult.Failure("inactive", "Secret has no active version.");

        var store = storeRegistry.Get(secret.StoreName);
        var result = await store.TestAsync(new SecretTestContext(secret, version), cancellationToken);
        await RecordAsync("test", secret.Name, result.Succeeded ? "succeeded" : "failed", result.Code, cancellationToken);
        return result;
    }

    public async ValueTask<SecretPayload> ResolvePayloadAsync(Secret secret, CancellationToken cancellationToken = default)
    {
        var version = secret.LatestActiveVersion ?? throw new InvalidOperationException("Secret has no active version.");
        var store = storeRegistry.Get(secret.StoreName);
        var payload = await store.ReadAsync(new SecretReadContext(secret, version), cancellationToken);
        return payload ?? throw new InvalidOperationException("Secret payload could not be resolved.");
    }

    private async ValueTask<Secret> GetExistingSecretAsync(string name, CancellationToken cancellationToken)
    {
        var normalizedName = nameValidator.Normalize(name);
        var secret = await repository.FindAsync(normalizedName, cancellationToken);
        return secret ?? throw new InvalidOperationException("Secret not found.");
    }

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

    private static void EnsureStoreIsSupported(ISecretTypeProvider typeProvider, string storeName)
    {
        if (!typeProvider.Descriptor.SupportedStoreNames.Contains(storeName, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Secret type is not compatible with the selected store.");
    }
}

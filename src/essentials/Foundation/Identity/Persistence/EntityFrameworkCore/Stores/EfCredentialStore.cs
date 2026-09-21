using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF-backed tenant-local credential store. Only the hashed secret supplied by the provider-neutral
/// contract is persisted; plaintext credential material is intentionally not part of this adapter.
/// </summary>
public sealed class EfCredentialStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : ICredentialStore, IRevisionAwareCredentialStore
{
    public async ValueTask<CredentialRecord?> FindAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(credentialId, nameof(credentialId));
        PrepareTenant(tenantId, cancellationToken);
        try
        {
            var row = await context.Credentials.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == StorageId(tenantId, credentialId),
                cancellationToken);
            return row is null || !Matches(row, tenantId, credentialId) ? null : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to read the Identity credential.", exception);
        }
    }

    public ValueTask SaveAsync(CredentialRecord credential, CancellationToken cancellationToken = default) =>
        new(SaveUnconditionallyAsync(credential, cancellationToken));

    public async ValueTask<IamRevisionedRecord<CredentialRecord>?> FindWithRevisionAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(credentialId, nameof(credentialId));
        PrepareTenant(tenantId, cancellationToken);
        try
        {
            var row = await context.Credentials.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == StorageId(tenantId, credentialId),
                cancellationToken);
            return row is null || !Matches(row, tenantId, credentialId)
                ? null
                : new IamRevisionedRecord<CredentialRecord>(
                    Map(row),
                    IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to read the Identity credential revision.", exception);
        }
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        CredentialRecord credential,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ValidateCredential(credential);
        PrepareTenant(credential.TenantId, cancellationToken);

        var expectedVersion = 0L;
        if (expectedRevision is not null &&
            !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion))
            return EfIdentityStoreSupport.InvalidRevision();

        return expectedRevision is null
            ? await EfIdentityRevisionedRowWrite.SaveCreateOnlyAsync(context, Row(credential), cancellationToken)
            : await EfIdentityRevisionedRowWrite.SaveCompareAndSwapAsync(context, Row(credential), expectedVersion, cancellationToken);
    }

    private async Task SaveUnconditionallyAsync(CredentialRecord credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ValidateCredential(credential);
        PrepareTenant(credential.TenantId, cancellationToken);

        await EfIdentityRevisionedRowWrite.SaveUnconditionallyAsync(context, Row(credential), cancellationToken);
    }

    private EfIdentityRevisionedRow<CredentialEntity> Row(CredentialRecord credential) => new(
        "Identity credential",
        cancellationToken => FindEntityForWriteAsync(credential, cancellationToken),
        cancellationToken => ExistsAsync(credential, cancellationToken),
        static () => new CredentialEntity(),
        entity => Apply(entity, credential));

    private async Task<CredentialEntity?> FindEntityForWriteAsync(
        CredentialRecord credential,
        CancellationToken cancellationToken)
    {
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, credential.TenantId);
        cancellationToken.ThrowIfCancellationRequested();
        var row = await context.Credentials.SingleOrDefaultAsync(
            candidate => candidate.Id == StorageId(credential.TenantId, credential.Id),
            cancellationToken);
        return row is null || !Matches(row, credential.TenantId, credential.Id) ? null : row;
    }

    private async Task<bool> ExistsAsync(CredentialRecord credential, CancellationToken cancellationToken) =>
        await FindEntityForReadAsync(credential.TenantId, credential.Id, cancellationToken) is not null;

    private async Task<CredentialEntity?> FindEntityForReadAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken)
    {
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        var row = await context.Credentials.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == StorageId(tenantId, credentialId),
            cancellationToken);
        return row is null || !Matches(row, tenantId, credentialId) ? null : row;
    }

    private void PrepareTenant(string tenantId, CancellationToken cancellationToken)
    {
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void Apply(CredentialEntity entity, CredentialRecord credential)
    {
        entity.Id = StorageId(credential.TenantId, credential.Id);
        entity.TenantId = credential.TenantId;
        entity.TenantLookupKey = IdentityEntityFrameworkKey.Normalize(credential.TenantId);
        entity.CredentialId = credential.Id;
        entity.CredentialLookupKey = IdentityEntityFrameworkKey.Normalize(credential.Id);
        entity.SubjectType = (int)credential.SubjectType;
        entity.SubjectId = credential.SubjectId;
        entity.Kind = (int)credential.Kind;
        entity.HashedSecret = credential.HashedSecret;
        entity.HashAlgorithm = credential.HashAlgorithm;
        entity.Status = (int)credential.Status;
        entity.ExpiresAt = credential.ExpiresAt;
    }

    private static CredentialRecord Map(CredentialEntity entity) => new(
        entity.CredentialId,
        entity.TenantId,
        (CredentialSubjectType)entity.SubjectType,
        entity.SubjectId,
        (CredentialKind)entity.Kind,
        entity.HashedSecret,
        entity.HashAlgorithm,
        (CredentialStatus)entity.Status,
        entity.ExpiresAt);

    private static bool Matches(CredentialEntity entity, string tenantId, string credentialId) =>
        string.Equals(entity.Id, StorageId(tenantId, credentialId), StringComparison.Ordinal) &&
        string.Equals(entity.TenantLookupKey, IdentityEntityFrameworkKey.Normalize(tenantId), StringComparison.Ordinal) &&
        string.Equals(entity.CredentialLookupKey, IdentityEntityFrameworkKey.Normalize(credentialId), StringComparison.Ordinal) &&
        string.Equals(
            IdentityEntityFrameworkKey.Normalize(entity.TenantId),
            IdentityEntityFrameworkKey.Normalize(tenantId),
            StringComparison.Ordinal) &&
        string.Equals(
            IdentityEntityFrameworkKey.Normalize(entity.CredentialId),
            IdentityEntityFrameworkKey.Normalize(credentialId),
            StringComparison.Ordinal);

    private static string StorageId(string tenantId, string credentialId) =>
        IdentityEntityFrameworkKey.TenantRecordId(tenantId, credentialId);

    private static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = IdentityEntityFrameworkKey.Normalize(value);
        _ = parameterName;
    }

    private static void ValidateCredential(CredentialRecord credential)
    {
        ValidateIdentity(credential.Id, nameof(credential.Id));
        ValidateIdentity(credential.TenantId, nameof(credential.TenantId));
        ArgumentNullException.ThrowIfNull(credential.SubjectId);
        ArgumentNullException.ThrowIfNull(credential.HashedSecret);
        ArgumentNullException.ThrowIfNull(credential.HashAlgorithm);
    }

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) =>
        new(message, exception);
}

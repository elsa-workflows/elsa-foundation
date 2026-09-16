using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Persistence.EntityFramework;
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
    private const int MaximumWriteAttempts = 3;

    public async ValueTask<CredentialRecord?> FindAsync(
        string tenantId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(credentialId, nameof(credentialId));
        PrepareTenantRead(tenantId, cancellationToken);
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
        PrepareTenantRead(tenantId, cancellationToken);
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
        PrepareTenantWrite(credential.TenantId, cancellationToken);

        var expectedVersion = 0L;
        if (expectedRevision is not null &&
            !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion))
            return Conflict();

        return expectedRevision is null
            ? await SaveCreateOnlyAsync(credential, cancellationToken)
            : await SaveCompareAndSwapAsync(credential, expectedVersion, cancellationToken);
    }

    private async Task SaveUnconditionallyAsync(CredentialRecord credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ValidateCredential(credential);
        PrepareTenantWrite(credential.TenantId, cancellationToken);

        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = await FindEntityForWriteAsync(credential, cancellationToken);
                if (row is null)
                {
                    context.Add(CreateEntity(credential, revision: 1));
                }
                else
                {
                    Apply(row, credential);
                    row.Revision = checked(row.Revision + 1);
                }

                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return;
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                context.ChangeTracker.Clear();
            }
            catch (Exception exception) when (
                EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw Failure("Unable to save the Identity credential.", exception);
            }
        }

        context.ChangeTracker.Clear();
        throw Failure(
            "Unable to save the Identity credential after bounded concurrency retries.",
            new InvalidOperationException("The Identity credential was concurrently modified."));
    }

    private async Task<IamRevisionSaveResult> SaveCreateOnlyAsync(
        CredentialRecord credential,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                if (await FindEntityForWriteAsync(credential, cancellationToken) is not null)
                {
                    context.ChangeTracker.Clear();
                    return Conflict();
                }

                context.Add(CreateEntity(credential, revision: 1));
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Saved(1);
            }
            catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception))
            {
                context.ChangeTracker.Clear();
                return Conflict();
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                return Conflict();
            }
            catch (Exception exception) when (
                EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw Failure("Unable to create the Identity credential.", exception);
            }
        }

        context.ChangeTracker.Clear();
        throw Failure(
            "Unable to create the Identity credential after bounded transient retries.",
            new InvalidOperationException("The Identity credential could not be created."));
    }

    private async Task<IamRevisionSaveResult> SaveCompareAndSwapAsync(
        CredentialRecord credential,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaximumWriteAttempts; attempt++)
        {
            try
            {
                var row = await FindEntityForWriteAsync(credential, cancellationToken);
                if (row is null)
                {
                    context.ChangeTracker.Clear();
                    return new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound);
                }
                if (row.Revision != expectedVersion)
                {
                    context.ChangeTracker.Clear();
                    return Conflict();
                }

                var nextRevision = checked(row.Revision + 1);
                Apply(row, credential);
                row.Revision = nextRevision;
                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                return Saved(nextRevision);
            }
            catch (DbUpdateConcurrencyException)
            {
                context.ChangeTracker.Clear();
                try
                {
                    var exists = await ExistsAsync(credential, cancellationToken);
                    context.ChangeTracker.Clear();
                    return exists ? Conflict() : new IamRevisionSaveResult(IamRevisionSaveStatus.NotFound);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    context.ChangeTracker.Clear();
                    throw;
                }
                catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
                {
                    context.ChangeTracker.Clear();
                    throw Failure("Unable to classify the Identity credential concurrency conflict.", exception);
                }
            }
            catch (Exception exception) when (
                EfRelationalExceptionClassifier.IsTransientWriteConflict(exception))
            {
                context.ChangeTracker.Clear();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                context.ChangeTracker.Clear();
                throw;
            }
            catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
            {
                context.ChangeTracker.Clear();
                throw Failure("Unable to update the Identity credential.", exception);
            }
        }

        context.ChangeTracker.Clear();
        throw Failure(
            "Unable to update the Identity credential after bounded transient retries.",
            new InvalidOperationException("The Identity credential could not be updated."));
    }

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

    private void PrepareTenantRead(string tenantId, CancellationToken cancellationToken)
    {
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void PrepareTenantWrite(string tenantId, CancellationToken cancellationToken)
    {
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static CredentialEntity CreateEntity(CredentialRecord credential, long revision)
    {
        var entity = new CredentialEntity { Revision = revision };
        Apply(entity, credential);
        return entity;
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

    private static IamRevisionSaveResult Saved(long revision) =>
        new(IamRevisionSaveStatus.Saved, IdentityEntityFrameworkRevisionCodec.FromVersion(revision));

    private static IamRevisionSaveResult Conflict() => new(IamRevisionSaveStatus.Conflict);

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) =>
        new(message, exception);
}

using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF-backed tenant-local application storage for I03.</summary>
public sealed class EfApplicationStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor)
    : IApplicationStore, IRevisionAwareApplicationStore
{
    public async ValueTask<ApplicationRecord?> FindAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(applicationId, nameof(applicationId));
        PrepareTenantRead(tenantId, cancellationToken);
        try
        {
            var row = await context.Applications.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == StorageId(tenantId, applicationId),
                cancellationToken);
            return row is null || !Matches(row, tenantId, applicationId) ? null : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to read the Identity application.", exception);
        }
    }

    public ValueTask SaveAsync(ApplicationRecord application, CancellationToken cancellationToken = default) =>
        new(SaveUnconditionallyAsync(application, cancellationToken));

    public async ValueTask<IamRevisionedRecord<ApplicationRecord>?> FindWithRevisionAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(applicationId, nameof(applicationId));
        PrepareTenantRead(tenantId, cancellationToken);
        try
        {
            var row = await context.Applications.AsNoTracking().SingleOrDefaultAsync(
                candidate => candidate.Id == StorageId(tenantId, applicationId),
                cancellationToken);
            return row is null || !Matches(row, tenantId, applicationId)
                ? null
                : new IamRevisionedRecord<ApplicationRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.ChangeTracker.Clear();
            throw;
        }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException)
        {
            context.ChangeTracker.Clear();
            throw Failure("Unable to read the Identity application revision.", exception);
        }
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(
        ApplicationRecord application,
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        ValidateApplication(application);
        PrepareTenantWrite(application.TenantId, cancellationToken);
        var expectedVersion = 0L;
        if (expectedRevision is not null &&
            !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion))
            return EfIdentityStoreSupport.InvalidRevision();

        return expectedRevision is null
            ? await EfIdentityRevisionedRowWrite.SaveCreateOnlyAsync(context, Row(application), cancellationToken)
            : await EfIdentityRevisionedRowWrite.SaveCompareAndSwapAsync(context, Row(application), expectedVersion, cancellationToken);
    }

    private async Task SaveUnconditionallyAsync(ApplicationRecord application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ValidateApplication(application);
        PrepareTenantWrite(application.TenantId, cancellationToken);
        await EfIdentityRevisionedRowWrite.SaveUnconditionallyAsync(context, Row(application), cancellationToken);
    }

    private EfIdentityRevisionedRow<ApplicationEntity> Row(ApplicationRecord application) => new(
        "Identity application",
        cancellationToken => FindEntityForWriteAsync(application, cancellationToken),
        cancellationToken => ExistsAsync(application, cancellationToken),
        static () => new ApplicationEntity(),
        entity => Apply(entity, application));

    private async Task<ApplicationEntity?> FindEntityForWriteAsync(
        ApplicationRecord application,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = StorageId(application.TenantId, application.Id);
        var row = await context.Applications.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null || !Matches(row, application.TenantId, application.Id) ? null : row;
    }

    private async Task<bool> ExistsAsync(ApplicationRecord application, CancellationToken cancellationToken) =>
        await context.Applications.AsNoTracking().AnyAsync(
            candidate => candidate.Id == StorageId(application.TenantId, application.Id),
            cancellationToken);

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

    private static void Apply(ApplicationEntity entity, ApplicationRecord application)
    {
        entity.Id = StorageId(application.TenantId, application.Id);
        entity.TenantId = application.TenantId;
        entity.ApplicationId = application.Id;
        entity.ClientId = application.ClientId;
        entity.DisplayName = application.DisplayName;
        entity.Type = application.Type;
        entity.Ownership = application.Ownership;
        entity.AllowedGrantTypesJson = IdentityApplicationSetCodec.Serialize(application.AllowedGrantTypes);
        entity.ScopesJson = IdentityApplicationSetCodec.Serialize(application.Scopes);
    }

    private static ApplicationRecord Map(ApplicationEntity entity) =>
        new(
            entity.ApplicationId,
            entity.TenantId,
            entity.ClientId,
            entity.DisplayName,
            (ApplicationType)entity.Type,
            (ResourceOwnership)entity.Ownership,
            IdentityApplicationSetCodec.Deserialize(entity.AllowedGrantTypesJson),
            IdentityApplicationSetCodec.Deserialize(entity.ScopesJson));

    private static bool Matches(ApplicationEntity entity, string tenantId, string applicationId) =>
        string.Equals(entity.Id, IdentityEntityFrameworkKey.TenantRecordId(tenantId, applicationId), StringComparison.Ordinal) &&
        string.Equals(IdentityEntityFrameworkKey.Normalize(entity.TenantId), IdentityEntityFrameworkKey.Normalize(tenantId), StringComparison.Ordinal) &&
        string.Equals(IdentityEntityFrameworkKey.Normalize(entity.ApplicationId), IdentityEntityFrameworkKey.Normalize(applicationId), StringComparison.Ordinal);

    private static string StorageId(string tenantId, string applicationId) =>
        IdentityEntityFrameworkKey.TenantRecordId(tenantId, applicationId);

    private static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = IdentityEntityFrameworkKey.Normalize(value);
    }

    private static void ValidateApplication(ApplicationRecord application)
    {
        ValidateIdentity(application.TenantId, nameof(application.TenantId));
        ValidateIdentity(application.Id, nameof(application.Id));
        ArgumentNullException.ThrowIfNull(application.ClientId);
        ArgumentNullException.ThrowIfNull(application.DisplayName);
        ArgumentNullException.ThrowIfNull(application.AllowedGrantTypes);
        ArgumentNullException.ThrowIfNull(application.Scopes);
    }

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) =>
        new(message, exception);
}

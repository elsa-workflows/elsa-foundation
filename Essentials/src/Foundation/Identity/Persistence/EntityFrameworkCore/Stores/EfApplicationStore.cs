using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>EF-backed tenant-local application storage for I03, with deterministic bounded paging.</summary>
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
        PrepareTenant(tenantId, cancellationToken);
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

    public async ValueTask<IamPage<ApplicationRecord>> ListPageAsync(
        string tenantId,
        IamPageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateIdentity(tenantId, nameof(tenantId));
        PrepareTenant(tenantId, cancellationToken);
        var query = context.Applications.AsNoTracking()
            .Where(candidate => candidate.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId));
        var (total, rows) = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to page Identity applications.",
            async () => (
                await query.LongCountAsync(cancellationToken),
                await query.OrderBy(candidate => candidate.ApplicationIdOrderKey).ThenBy(candidate => candidate.Id)
                    .Skip(request.Skip).Take(request.Take).ToListAsync(cancellationToken)));
        return new IamPage<ApplicationRecord>(rows.Select(Map).ToArray(), total);
    }

    public async ValueTask<bool> DeleteAsync(string tenantId, string applicationId, CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(applicationId, nameof(applicationId));
        PrepareTenant(tenantId, cancellationToken);
        return await EfIdentityRevisionedRowWrite.DeleteAsync(
            context,
            "Identity application",
            token => FindEntityForWriteAsync(tenantId, applicationId, token),
            cancellationToken);
    }

    public async ValueTask<IamRevisionedRecord<ApplicationRecord>?> FindWithRevisionAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(tenantId, nameof(tenantId));
        ValidateIdentity(applicationId, nameof(applicationId));
        PrepareTenant(tenantId, cancellationToken);
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
        PrepareTenant(application.TenantId, cancellationToken);
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
        PrepareTenant(application.TenantId, cancellationToken);
        await EfIdentityRevisionedRowWrite.SaveUnconditionallyAsync(context, Row(application), cancellationToken);
    }

    private EfIdentityRevisionedRow<ApplicationEntity> Row(ApplicationRecord application) => new(
        "Identity application",
        cancellationToken => FindEntityForWriteAsync(application.TenantId, application.Id, cancellationToken),
        cancellationToken => ExistsAsync(application, cancellationToken),
        static () => new ApplicationEntity(),
        entity => Apply(entity, application));

    private async Task<ApplicationEntity?> FindEntityForWriteAsync(
        string tenantId,
        string applicationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = StorageId(tenantId, applicationId);
        var row = await context.Applications.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null || !Matches(row, tenantId, applicationId) ? null : row;
    }

    private async Task<bool> ExistsAsync(ApplicationRecord application, CancellationToken cancellationToken) =>
        await context.Applications.AsNoTracking().AnyAsync(
            candidate => candidate.Id == StorageId(application.TenantId, application.Id),
            cancellationToken);

    private void PrepareTenant(string tenantId, CancellationToken cancellationToken)
    {
        context.EnsureProviderBinding();
        IdentityEntityFrameworkAccessGuard.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void Apply(ApplicationEntity entity, ApplicationRecord application)
    {
        entity.Id = StorageId(application.TenantId, application.Id);
        entity.TenantId = application.TenantId;
        entity.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(application.TenantId);
        entity.ApplicationId = application.Id;
        entity.ApplicationIdOrderKey = EfIdentityStoreSupport.SortableOrderKey(application.Id, nameof(application.Id));
        entity.ClientId = application.ClientId;
        entity.DisplayName = application.DisplayName;
        entity.Type = application.Type;
        entity.Ownership = application.Ownership;
        entity.Status = application.Status;
        entity.AllowedGrantTypesJson = IdentityApplicationSetCodec.Serialize(application.AllowedGrantTypes);
        entity.ScopesJson = IdentityApplicationSetCodec.Serialize(application.Scopes);
        entity.RoleIdsJson = IdentityApplicationSetCodec.Serialize(application.RoleIds);
        entity.SchemaVersion = IdentityIamEfModule.SchemaVersion;
    }

    private static ApplicationRecord Map(ApplicationEntity entity) => Map(entity, IdentityIamEfModule.Chain.Upcast<ApplicationEntity>(
        entity.SchemaVersion,
        (nameof(entity.AllowedGrantTypesJson), entity.AllowedGrantTypesJson),
        (nameof(entity.ScopesJson), entity.ScopesJson),
        (nameof(entity.RoleIdsJson), entity.RoleIdsJson)));

    /// <summary>The application <paramref name="entity"/> holds, its three sets read from its content columns upcast together (#2144).</summary>
    private static ApplicationRecord Map(ApplicationEntity entity, EfSchemaRowContent content) =>
        new(
            entity.ApplicationId,
            entity.TenantId,
            entity.ClientId,
            entity.DisplayName,
            (ApplicationType)entity.Type,
            (ResourceOwnership)entity.Ownership,
            IdentityApplicationSetCodec.Deserialize(content[nameof(entity.AllowedGrantTypesJson)]!),
            IdentityApplicationSetCodec.Deserialize(content[nameof(entity.ScopesJson)]!),
            IdentityApplicationSetCodec.Deserialize(content[nameof(entity.RoleIdsJson)]!),
            entity.Status);

    private static bool Matches(ApplicationEntity entity, string tenantId, string applicationId) =>
        EfSchemaVersion.Readable(IdentityIamEfModule.Chain, entity.SchemaVersion) &&
        string.Equals(entity.Id, IdentityEntityFrameworkKey.TenantRecordId(tenantId, applicationId), StringComparison.Ordinal) &&
        string.Equals(entity.TenantLookupKey, EfIdentityStoreSupport.TenantLookup(tenantId), StringComparison.Ordinal) &&
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
        _ = EfIdentityStoreSupport.SortableOrderKey(application.Id, nameof(application.Id));
        ArgumentNullException.ThrowIfNull(application.ClientId);
        ArgumentNullException.ThrowIfNull(application.DisplayName);
        ArgumentNullException.ThrowIfNull(application.AllowedGrantTypes);
        ArgumentNullException.ThrowIfNull(application.Scopes);
        ArgumentNullException.ThrowIfNull(application.RoleIds);
    }

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) =>
        EfIdentityStoreSupport.Failure(message, exception);
}

using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral tenant-local IAM role store with deterministic bounded paging.</summary>
public sealed class EfRoleStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null)
    : IRoleStore, IRevisionAwareRoleStore, IPagedRoleStore
{
    private readonly EfIdentityAuthorityAggregateCoordinator aggregates = aggregateCoordinator ?? new(context, accessContextAccessor);

    public async ValueTask<RoleRecord?> FindAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Validate(roleId, nameof(roleId)); Prepare(tenantId, cancellationToken);
        try
        {
            var row = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Unable to read the Identity role.",
                () => context.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, roleId), cancellationToken));
            return row is null || !Matches(row, tenantId, roleId) ? null : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to read the Identity role.", exception); }
    }

    public async ValueTask<IReadOnlyList<RoleRecord>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Prepare(tenantId, cancellationToken);
        var rows = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to list Identity roles.",
            () => context.Roles.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId)).OrderBy(x => x.RoleIdOrderKey).ThenBy(x => x.Id).Take(EfIdentityStoreSupport.MaximumMaterializedListEntries + 1).ToListAsync(cancellationToken));
        // This is an explicit public materialization guard, not a provider failure. Keep it
        // outside ReadAsync so callers receive the contract InvalidOperationException.
        if (rows.Count > EfIdentityStoreSupport.MaximumMaterializedListEntries) throw new InvalidOperationException($"The Identity role list exceeds the {EfIdentityStoreSupport.MaximumMaterializedListEntries}-entry materialization limit; use {nameof(IPagedRoleStore)}.");
        try
        {
            return rows.Select(Map).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to list Identity roles.", exception); }
    }

    public async ValueTask<IamPage<RoleRecord>> ListPageAsync(string tenantId, IamPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Validate(tenantId, nameof(tenantId)); Prepare(tenantId, cancellationToken);
        var query = context.Roles.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId));
        var (total, rows) = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to page Identity roles.",
            async () => (await query.LongCountAsync(cancellationToken), await query.OrderBy(x => x.RoleIdOrderKey).ThenBy(x => x.Id).Skip(request.Skip).Take(request.Take).ToListAsync(cancellationToken)));
        return new IamPage<RoleRecord>(rows.Select(Map).ToArray(), total);
    }

    public async ValueTask SaveAsync(RoleRecord role, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        var result = await aggregates.SaveRoleAsync(role, expectedVersion: null, cancellationToken);
        if (!result.WriteResult.Succeeded) throw new IdentityEntityFrameworkPersistenceException("Unable to save the Identity role.", new InvalidOperationException(result.WriteResult.Message));
    }

    public async ValueTask<IamRevisionedRecord<RoleRecord>?> FindWithRevisionAsync(string tenantId, string roleId, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Validate(roleId, nameof(roleId)); Prepare(tenantId, cancellationToken);
        var row = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the Identity role revision.",
            () => context.Roles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, roleId), cancellationToken));
        return row is null || !Matches(row, tenantId, roleId)
            ? null
            : new IamRevisionedRecord<RoleRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(RoleRecord role, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(role);
        long expectedVersion = 0;
        if (expectedRevision is not null && !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion)) return EfIdentityStoreSupport.InvalidRevision();
        var result = await aggregates.SaveRoleAsync(role, expectedVersion, cancellationToken);
        return EfIdentityStoreSupport.ToRevisionResult(result.WriteResult);
    }

    internal Task<RoleEntity?> FindEntityAsync(string tenantId, string roleId, bool track, CancellationToken cancellationToken)
    {
        Validate(tenantId, nameof(tenantId)); Validate(roleId, nameof(roleId)); Prepare(tenantId, cancellationToken);
        var query = track ? context.Roles : context.Roles.AsNoTracking();
        return EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the tracked Identity role.",
            () => query.SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, roleId), cancellationToken));
    }

    private void Prepare(string tenantId, CancellationToken cancellationToken) { context.EnsureProviderBinding(); EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId); cancellationToken.ThrowIfCancellationRequested(); }
    private static RoleRecord Map(RoleEntity entity) => new(entity.RoleId, entity.TenantId, entity.Name, entity.Description, EfIdentityStoreSupport.DeserializeSet(entity.PermissionsJson), entity.System);
    private static bool Matches(RoleEntity entity, string tenantId, string roleId) => string.Equals(entity.Id, EfIdentityStoreSupport.RecordId(tenantId, roleId), StringComparison.Ordinal) && string.Equals(EfIdentityStoreSupport.Normalize(entity.TenantId), EfIdentityStoreSupport.Normalize(tenantId), StringComparison.Ordinal) && string.Equals(EfIdentityStoreSupport.Normalize(entity.RoleId), EfIdentityStoreSupport.Normalize(roleId), StringComparison.Ordinal);
    private static void Validate(string value, string parameter) { ArgumentNullException.ThrowIfNull(value); if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength) throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter); }
    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => new(message, exception);
}

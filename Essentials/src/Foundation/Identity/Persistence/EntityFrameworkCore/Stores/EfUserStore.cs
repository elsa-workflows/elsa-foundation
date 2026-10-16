using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral tenant-local IAM user store backed by the authoritative EF root row, with deterministic bounded paging.</summary>
public sealed class EfUserStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null)
    : IUserStore, IRevisionAwareUserStore, IPagedUserStore
{
    private readonly EfIdentityAuthorityAggregateCoordinator aggregates = aggregateCoordinator ?? new(context, accessContextAccessor);
    private readonly IIdentityEmailUniquenessPolicy emailPolicy = emailUniquenessPolicy ?? IdentityEmailUniquenessPolicy.NonUnique;

    public async ValueTask<UserRecord?> FindAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Validate(userId, nameof(userId));
        Prepare(tenantId, cancellationToken);
        try
        {
            var row = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Unable to read the Identity user.",
                () => context.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, userId), cancellationToken));
            return row is null || !Matches(row, tenantId, userId) ? null : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to read the Identity user.", exception); }
    }

    public async ValueTask<UserRecord?> FindByEmailAsync(string tenantId, string email, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Validate(email, nameof(email));
        Prepare(tenantId, cancellationToken);
        try
        {
            // The key is derived the way a save derives it, so a user written by any path is found by its email.
            var normalized = EfIdentityStoreSupport.NormalizedEmail(aggregates.LookupNormalizer, email);
            var key = EfIdentityStoreSupport.LookupKey(tenantId, normalized);
            if (key is null)
                return null;
            var rows = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Unable to find the Identity user by email.",
                () => context.Users.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId) && x.NormalizedEmailKey == key).OrderBy(x => x.Id).Take(2).ToListAsync(cancellationToken));
            if (rows.Count != 1)
                return null;
            return EfSchemaVersion.Readable(IdentityIamEfModule.Chain, rows[0].SchemaVersion) &&
                   string.Equals(EfIdentityStoreSupport.Normalize(rows[0].NormalizedEmail), EfIdentityStoreSupport.Normalize(normalized), StringComparison.Ordinal)
                ? Map(rows[0])
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to find the Identity user by email.", exception); }
    }

    public async ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var result = await aggregates.SaveUserAsync(user, expectedVersion: null, emailPolicy.RequireUniqueEmail, cancellationToken);
        EfIdentityStoreSupport.EnsureSaved(result.WriteResult, "Unable to save the Identity user.");
    }

    public async ValueTask<bool> SetStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken cancellationToken = default) =>
        Applied(await aggregates.SetUserStatusAsync(tenantId, userId, status, cancellationToken), "Unable to set the Identity user status.");

    public async ValueTask<bool> DeleteAsync(string tenantId, string userId, CancellationToken cancellationToken = default) =>
        Applied(await aggregates.DeleteUserAsync(tenantId, userId, expectedVersion: null, cancellationToken), "Unable to delete the Identity user.");

    public async ValueTask<IamPage<UserRecord>> ListPageAsync(string tenantId, IamPageRequest request, string? search = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Validate(tenantId, nameof(tenantId)); Prepare(tenantId, cancellationToken);
        var query = context.Users.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId));
        if (!string.IsNullOrWhiteSpace(search))
        {
            // Each key is derived exactly the way a save derives it for that field, so the database compares opaque
            // keys and the match is the store's own normalization on every provider, whichever path wrote the user.
            var nameKey = EfIdentityStoreSupport.LookupKey(tenantId, EfIdentityStoreSupport.NormalizedUserName(aggregates.LookupNormalizer, search));
            var emailKey = EfIdentityStoreSupport.LookupKey(tenantId, EfIdentityStoreSupport.NormalizedEmail(aggregates.LookupNormalizer, search));
            query = query.Where(x => (nameKey != null && x.NormalizedUserNameKey == nameKey) || (emailKey != null && x.NormalizedEmailKey == emailKey));
        }

        var (total, rows) = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to page Identity users.",
            async () => (await query.LongCountAsync(cancellationToken), await query.OrderBy(x => x.UserIdOrderKey).ThenBy(x => x.Id).Skip(request.Skip).Take(request.Take).ToListAsync(cancellationToken)));
        return new IamPage<UserRecord>(rows.Select(Map).ToArray(), total);
    }

    public async ValueTask<IamRevisionedRecord<UserRecord>?> FindWithRevisionAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Validate(userId, nameof(userId)); Prepare(tenantId, cancellationToken);
        var row = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the Identity user revision.",
            () => context.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, userId), cancellationToken));
        return row is null || !Matches(row, tenantId, userId)
            ? null
            : new IamRevisionedRecord<UserRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(UserRecord user, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        long expectedVersion = 0;
        if (expectedRevision is not null && !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expectedVersion)) return EfIdentityStoreSupport.InvalidRevision();
        var result = await aggregates.SaveUserAsync(user, expectedVersion, emailPolicy.RequireUniqueEmail, cancellationToken);
        return EfIdentityStoreSupport.ToRevisionResult(result.WriteResult);
    }

    internal async Task<UserEntity?> FindEntityAsync(string tenantId, string userId, bool track, CancellationToken cancellationToken)
    {
        Validate(tenantId, nameof(tenantId)); Validate(userId, nameof(userId)); Prepare(tenantId, cancellationToken);
        var query = track ? context.Users : context.Users.AsNoTracking();
        return await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the tracked Identity user.",
            () => query.SingleOrDefaultAsync(x => x.Id == EfIdentityStoreSupport.RecordId(tenantId, userId), cancellationToken));
    }

    private void Prepare(string tenantId, CancellationToken cancellationToken)
    {
        context.EnsureProviderBinding();
        EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool Applied(EfIdentityWriteResult result, string failureMessage)
    {
        if (result.Status == EfIdentityWriteStatus.NotFound)
            return false;
        EfIdentityStoreSupport.EnsureSaved(result, failureMessage);
        return true;
    }

    private static UserRecord Map(UserEntity entity)
    {
        EfSchemaVersion.EnsureReadable(IdentityIamEfModule.Chain, entity.SchemaVersion);
        return new(
            entity.UserId, entity.TenantId, entity.UserName, entity.Email, entity.DisplayName,
            (UserStatus)entity.Status, (ResourceOwnership)entity.Ownership,
            EfIdentityStoreSupport.ReadSet(entity, nameof(entity.RoleIdsJson)), EfIdentityStoreSupport.ReadSet(entity, nameof(entity.DirectPermissionsJson)));
    }

    private static bool Matches(UserEntity entity, string tenantId, string userId) =>
        EfSchemaVersion.Readable(IdentityIamEfModule.Chain, entity.SchemaVersion) &&
        string.Equals(entity.Id, EfIdentityStoreSupport.RecordId(tenantId, userId), StringComparison.Ordinal) &&
        string.Equals(EfIdentityStoreSupport.Normalize(entity.TenantId), EfIdentityStoreSupport.Normalize(tenantId), StringComparison.Ordinal) &&
        string.Equals(EfIdentityStoreSupport.Normalize(entity.UserId), EfIdentityStoreSupport.Normalize(userId), StringComparison.Ordinal);

    private static void Validate(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength)
            throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter);
    }

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => EfIdentityStoreSupport.Failure(message, exception);
}

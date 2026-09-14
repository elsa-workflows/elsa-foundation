using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral tenant-local IAM user store backed by the authoritative EF root row.</summary>
public sealed class EfUserStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null)
    : IUserStore, IRevisionAwareUserStore
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
            var key = EfIdentityStoreSupport.Lookup(tenantId, email);
            var rows = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Unable to find the Identity user by email.",
                () => context.Users.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId) && x.NormalizedEmailKey == key).Take(2).ToListAsync(cancellationToken));
            return rows.Count == 1 && string.Equals(EfIdentityStoreSupport.Normalize(rows[0].Email), EfIdentityStoreSupport.Normalize(email), StringComparison.Ordinal) ? Map(rows[0]) : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to find the Identity user by email.", exception); }
    }

    public async ValueTask SaveAsync(UserRecord user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        var result = await aggregates.SaveUserAsync(user, expectedVersion: null, emailPolicy.RequireUniqueEmail, cancellationToken);
        if (!result.WriteResult.Succeeded)
            throw new IdentityEntityFrameworkPersistenceException("Unable to save the Identity user.", new InvalidOperationException(result.WriteResult.Message));
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

    private static UserRecord Map(UserEntity entity) => new(
        entity.UserId, entity.TenantId, entity.UserName, entity.Email, entity.DisplayName,
        (UserStatus)entity.Status, (ResourceOwnership)entity.Ownership,
        EfIdentityStoreSupport.DeserializeSet(entity.RoleIdsJson), EfIdentityStoreSupport.DeserializeSet(entity.DirectPermissionsJson));

    private static bool Matches(UserEntity entity, string tenantId, string userId) =>
        string.Equals(entity.Id, EfIdentityStoreSupport.RecordId(tenantId, userId), StringComparison.Ordinal) &&
        string.Equals(EfIdentityStoreSupport.Normalize(entity.TenantId), EfIdentityStoreSupport.Normalize(tenantId), StringComparison.Ordinal) &&
        string.Equals(EfIdentityStoreSupport.Normalize(entity.UserId), EfIdentityStoreSupport.Normalize(userId), StringComparison.Ordinal);

    private static void Validate(string value, string parameter)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength)
            throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter);
    }

    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => new(message, exception);
}

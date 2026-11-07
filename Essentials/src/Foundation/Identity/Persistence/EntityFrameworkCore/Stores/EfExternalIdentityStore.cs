using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Elsa.Persistence.EntityFramework;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral external-login store coordinated with both old and new user owners.</summary>
public sealed class EfExternalIdentityStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAuthorityRelationshipCoordinator? relationshipCoordinator = null,
    EfIdentityAuthorityAggregateCoordinator? aggregateCoordinator = null,
    IIdentityEmailUniquenessPolicy? emailUniquenessPolicy = null)
    : IExternalIdentityStore, IRevisionAwareExternalIdentityStore, IPagedExternalIdentityStore
{
    private readonly EfIdentityAuthorityRelationshipCoordinator relationship = relationshipCoordinator ?? new(
        context,
        new EfIdentityAtomicWrite(context, accessContextAccessor: accessContextAccessor),
        accessContextAccessor);

    public bool SupportsQualifiedKeys => true;
    public bool SupportsAtomicAdmission => true;
    public bool SupportsQualifiedRemoval
    {
        get
        {
            if (!context.Database.IsRelational()) return false;
            var relational = context.GetService<IDbContextOptions>().Extensions.OfType<RelationalOptionsExtension>().Single();
            return relational.Connection is null && !string.IsNullOrEmpty(relational.ConnectionString) &&
                string.Equals(relational.ConnectionString, context.Database.GetConnectionString(), StringComparison.Ordinal);
        }
    }

    public async ValueTask<ExternalIdentityRemovalResult> RemoveWithRevisionAsync(ExternalIdentityKey key, IamRevisionGuard owner,
        string expectedLinkRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(owner);
        Validate(key.Provider, nameof(key)); Validate(key.Subject, nameof(key)); EfIdentityStoreSupport.ValidateIssuer(key.Issuer);
        Prepare(key.TenantId, cancellationToken);
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Qualified removal owns its transaction and cannot borrow caller changes.");
        if (!SupportsQualifiedRemoval) throw new NotSupportedException("Qualified removal requires an owned configured relational target.");
        if (owner.Kind != IamRevisionGuardKind.User || EfIdentityStoreSupport.RecordId(owner.TenantId, owner.Id) !=
            EfIdentityStoreSupport.RecordId(key.TenantId, owner.Id) ||
            !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedLinkRevision, out var linkVersion) ||
            !EfIamRevisionGuards.TryGetVersion(owner.Revision, out _)) return new(ExternalIdentityRemovalStatus.Conflict);
        await using var transaction = await EfSharedTransaction.BeginSerializableAsync([context], cancellationToken);
        var owned = transaction.Context<IdentityIamDbContext>();
        var coordinator = new EfIdentityAuthorityRelationshipCoordinator(owned,
            new EfIdentityAtomicWrite(owned, accessContextAccessor: accessContextAccessor), accessContextAccessor);
        var result = await coordinator.DeleteQualifiedExternalIdentityAsync(key, owner, linkVersion, cancellationToken);
        if (result.Status == EfIdentityWriteStatus.Deleted) await transaction.CommitAsync(cancellationToken);
        return new(result.Status == EfIdentityWriteStatus.Deleted ? ExternalIdentityRemovalStatus.Removed :
                result.Status == EfIdentityWriteStatus.NotFound ? ExternalIdentityRemovalStatus.Missing : ExternalIdentityRemovalStatus.Conflict,
            result.Status == EfIdentityWriteStatus.Deleted && result.Version is { } version ? IdentityEntityFrameworkRevisionCodec.FromVersion(version) : null);
    }

    public ValueTask<ExternalUserAdmissionResult> AdmitAsync(ExternalUserAdmission request, CancellationToken cancellationToken = default) =>
        new EfExternalUserAdmission(context, accessContextAccessor,
            aggregateCoordinator ?? new(context, accessContextAccessor), relationship,
            emailUniquenessPolicy ?? IdentityEmailUniquenessPolicy.NonUnique).ApplyAsync(request, cancellationToken);

    public async ValueTask<ExternalIdentityRecord?> FindBySubjectAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        Validate(tenantId, nameof(tenantId)); Validate(provider, nameof(provider)); Validate(providerSubject, nameof(providerSubject)); _ = EfIdentityStoreSupport.ExternalOrderKey(provider, providerSubject); Prepare(tenantId, cancellationToken);
        try
        {
            var row = await EfIdentityStoreSupport.ReadAsync(
                context,
                "Unable to find the external Identity login.",
                () => context.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(tenantId, provider, providerSubject), cancellationToken));
            return row is null ? null : Map(row);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to find the external Identity login.", exception); }
    }

    public async ValueTask<ExternalIdentityRecord?> FindByKeyAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default)
    {
        var row = await FindByKeyWithRevisionAsync(key, cancellationToken);
        return row?.Record;
    }

    public async ValueTask<IamRevisionedRecord<ExternalIdentityRecord>?> FindByKeyWithRevisionAsync(ExternalIdentityKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        Validate(key.TenantId, nameof(key.TenantId)); Validate(key.Provider, nameof(key.Provider)); Validate(key.Subject, nameof(key.Subject));
        EfIdentityStoreSupport.ValidateIssuer(key.Issuer);
        _ = EfIdentityStoreSupport.ExternalOrderKey(key.Provider, key.Subject);
        Prepare(key.TenantId, cancellationToken);
        var id = EfIdentityStoreSupport.ExternalIdentityId(key.TenantId, key.Provider, key.Subject, key.Issuer);
        var row = await EfIdentityStoreSupport.ReadAsync(context, "Unable to find the issuer-qualified Identity login.",
            () => context.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken));
        if (row is null) return null;
        EfIdentityStoreSupport.EnsureExternalIdentity(row, key.TenantId, key.Provider, key.Subject, issuer: key.Issuer);
        return new(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
    }

    public async ValueTask<IReadOnlyList<ExternalIdentityRecord>> ListForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, cancellationToken); Validate(userId, nameof(userId));
        var rows = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to list external Identity logins.",
            () => QueryForUser(tenantId, userId).OrderBy(x => x.ExternalOrderKey).ThenBy(x => x.Id).Take(EfIdentityStoreSupport.MaximumMaterializedListEntries + 1).ToListAsync(cancellationToken));
        // This is an explicit public materialization guard, not a provider failure. Keep it
        // outside ReadAsync so callers receive the contract InvalidOperationException.
        if (rows.Count > EfIdentityStoreSupport.MaximumMaterializedListEntries) throw new InvalidOperationException($"The external Identity login list exceeds the {EfIdentityStoreSupport.MaximumMaterializedListEntries}-entry materialization limit; use {nameof(IPagedExternalIdentityStore)}.");
        try
        {
            return rows.Select(Map).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to list external Identity logins.", exception); }
    }

    public async ValueTask<IamPage<ExternalIdentityRecord>> ListForUserPageAsync(string tenantId, string userId, IamPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Prepare(tenantId, cancellationToken); Validate(userId, nameof(userId));
        var query = QueryForUser(tenantId, userId);
        var (total, rows) = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to page external Identity logins.",
            async () => (await query.LongCountAsync(cancellationToken), await query.OrderBy(x => x.ExternalOrderKey).ThenBy(x => x.Id).Skip(request.Skip).Take(request.Take).ToListAsync(cancellationToken)));
        var items = rows.Select(Map).ToArray();
        return new IamPage<ExternalIdentityRecord>(items, total);
    }

    public async ValueTask SaveAsync(ExternalIdentityRecord externalIdentity, CancellationToken cancellationToken = default) =>
        EfIdentityStoreSupport.EnsureSaved(await SaveCoreAsync(externalIdentity, expectedVersion: null, createOnly: false, cancellationToken), "Unable to save the external Identity login.");

    public async ValueTask<IamRevisionedRecord<ExternalIdentityRecord>?> FindBySubjectWithRevisionAsync(string tenantId, string provider, string providerSubject, CancellationToken cancellationToken = default)
    {
        var row = await FindEntityAsync(tenantId, provider, providerSubject, cancellationToken);
        return row is null ? null : new IamRevisionedRecord<ExternalIdentityRecord>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
    }

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ExternalIdentityRecord externalIdentity, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(externalIdentity); Validate(externalIdentity.TenantId, nameof(externalIdentity.TenantId));
        long expected = 0;
        if (expectedRevision is not null && !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expected)) return EfIdentityStoreSupport.InvalidRevision();
        var result = await SaveCoreAsync(externalIdentity, expectedRevision is null ? 0 : expected, expectedRevision is null, cancellationToken);
        return EfIdentityStoreSupport.ToRevisionResult(result);
    }

    private async Task<EfIdentityWriteResult> SaveCoreAsync(ExternalIdentityRecord record, long? expectedVersion, bool createOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record); Validate(record.TenantId, nameof(record.TenantId)); Validate(record.Provider, nameof(record.Provider)); Validate(record.ProviderSubject, nameof(record.ProviderSubject)); _ = EfIdentityStoreSupport.ExternalOrderKey(record.Provider, record.ProviderSubject); Validate(record.UserId, nameof(record.UserId));
        EfIdentityStoreSupport.ValidateIssuer(record.Issuer);
        Prepare(record.TenantId, cancellationToken);
        var row = ToEntity(record);
        return await relationship.SaveExternalIdentityPreservingProviderDisplayNameAsync(row, expectedNewOwnerVersion: null, expectedLoginVersion: createOnly ? 0 : expectedVersion, enforceLoginVersion: createOnly || expectedVersion is not null, ownershipPolicy: createOnly || expectedVersion is null ? EfExternalLoginOwnershipPolicy.CreateOrSameOwner : EfExternalLoginOwnershipPolicy.RevisionEnforcedRebind, returnOwnerResult: false, cancellationToken);
    }

    private async Task<ExternalIdentityEntity?> FindEntityAsync(string tenantId, string provider, string subject, CancellationToken cancellationToken)
    {
        Prepare(tenantId, cancellationToken); Validate(provider, nameof(provider)); Validate(subject, nameof(subject));
        return await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the external Identity login revision.",
            () => context.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(tenantId, provider, subject), cancellationToken));
    }

    private IQueryable<ExternalIdentityEntity> QueryForUser(string tenantId, string userId) => context.ExternalIdentities.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId) && x.UserLookupKey == EfIdentityStoreSupport.Lookup(tenantId, userId));
    private static ExternalIdentityEntity ToEntity(ExternalIdentityRecord record) => new() { TenantId = record.TenantId, Provider = record.Provider, ProviderSubject = record.ProviderSubject, Issuer = record.Issuer, UserId = record.UserId, LinkedAt = record.LinkedAt, LastSeenAt = record.LastSeenAt, LinkPolicy = (int)record.LinkPolicy };
    private static ExternalIdentityRecord Map(ExternalIdentityEntity row)
    {
        EfSchemaVersion.EnsureReadable(IdentityIamEfModule.Chain, row.SchemaVersion);
        return new(row.TenantId, row.Provider, row.ProviderSubject, row.UserId, row.LinkedAt, row.LastSeenAt, (ExternalIdentityLinkPolicy)row.LinkPolicy) { Issuer = row.Issuer };
    }

    private static string Id(string tenantId, string provider, string subject) => EfIdentityStoreSupport.CompoundKey(tenantId, provider, subject);
    private void Prepare(string tenantId, CancellationToken cancellationToken) { Validate(tenantId, nameof(tenantId)); EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId); context.EnsureProviderBinding(); cancellationToken.ThrowIfCancellationRequested(); }
    private static void Validate(string value, string parameter) { ArgumentNullException.ThrowIfNull(value); if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength) throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter); }
    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => EfIdentityStoreSupport.Failure(message, exception);
}

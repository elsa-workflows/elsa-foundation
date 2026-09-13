using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Entities;
using Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Stores;

/// <summary>Provider-neutral claim-mapping persistence with tenant/provider bounded paging.</summary>
public sealed class EfClaimMappingStore(
    IdentityIamDbContext context,
    IPersistenceAccessContextAccessor accessContextAccessor,
    EfIdentityAtomicWrite? atomicWrite = null)
    : IClaimMappingStore, IRevisionAwareClaimMappingStore, IPagedClaimMappingStore
{
    private readonly EfIdentityAtomicWrite atomic = atomicWrite ?? new EfIdentityAtomicWrite(context, accessContextAccessor: accessContextAccessor);

    public async ValueTask<IReadOnlyList<ClaimMappingRule>> ListForProviderAsync(string tenantId, string provider, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, cancellationToken); Validate(provider, nameof(provider));
        var rows = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to list Identity claim mappings.",
            () => Query(tenantId, provider).OrderBy(x => x.Order).ThenBy(x => x.RuleIdOrderKey).ThenBy(x => x.Id).Take(EfIdentityStoreSupport.MaximumMaterializedListEntries + 1).ToListAsync(cancellationToken));
        // This is an explicit public materialization guard, not a provider failure. Keep it
        // outside ReadAsync so callers receive the contract InvalidOperationException.
        if (rows.Count > EfIdentityStoreSupport.MaximumMaterializedListEntries)
            throw new InvalidOperationException($"The Identity claim-mapping list exceeds the {EfIdentityStoreSupport.MaximumMaterializedListEntries}-entry materialization limit; use {nameof(IPagedClaimMappingStore)}.");
        try
        {
            return rows.Select(Map).ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { context.ChangeTracker.Clear(); throw; }
        catch (Exception exception) when (exception is not IdentityEntityFrameworkPersistenceException) { context.ChangeTracker.Clear(); throw Failure("Unable to list Identity claim mappings.", exception); }
    }

    public async ValueTask<IamPage<ClaimMappingRule>> ListForProviderPageAsync(string tenantId, string provider, IamPageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); Prepare(tenantId, cancellationToken); Validate(provider, nameof(provider));
        var query = Query(tenantId, provider);
        var (total, rows) = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to page Identity claim mappings.",
            async () => (await query.LongCountAsync(cancellationToken), await query.OrderBy(x => x.Order).ThenBy(x => x.RuleIdOrderKey).ThenBy(x => x.Id).Skip(request.Skip).Take(request.Take).ToListAsync(cancellationToken)));
        var ordered = rows.Select(Map).ToArray();
        return new IamPage<ClaimMappingRule>(ordered, total);
    }

    public async ValueTask<IamRevisionedRecord<ClaimMappingRule>?> FindWithRevisionAsync(string tenantId, string provider, string ruleId, CancellationToken cancellationToken = default)
    {
        Prepare(tenantId, cancellationToken); Validate(provider, nameof(provider)); Validate(ruleId, nameof(ruleId));
        var row = await EfIdentityStoreSupport.ReadAsync(
            context,
            "Unable to read the Identity claim mapping revision.",
            () => context.ClaimMappings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == Id(tenantId, provider, ruleId), cancellationToken));
        return row is null ? null : new IamRevisionedRecord<ClaimMappingRule>(Map(row), IdentityEntityFrameworkRevisionCodec.FromVersion(row.Revision));
    }

    public ValueTask SaveAsync(ClaimMappingRule rule, CancellationToken cancellationToken = default) =>
        new(SaveCoreAsync(rule, expectedVersion: null, createOnly: false, cancellationToken));

    public async ValueTask<IamRevisionSaveResult> SaveWithRevisionAsync(ClaimMappingRule rule, string? expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule); ValidateRule(rule); Prepare(rule.TenantId, cancellationToken);
        long expected = 0;
        if (expectedRevision is not null && !IdentityEntityFrameworkRevisionCodec.TryGetVersion(expectedRevision, out expected)) return EfIdentityStoreSupport.InvalidRevision();
        var result = await SaveCoreAsync(rule, expectedRevision is null ? 0 : expected, createOnly: expectedRevision is null, cancellationToken);
        return EfIdentityStoreSupport.ToRevisionResult(result);
    }

    private async Task<EfIdentityWriteResult> SaveCoreAsync(ClaimMappingRule rule, long? expectedVersion, bool createOnly, CancellationToken cancellationToken)
    {
        ValidateRule(rule); Prepare(rule.TenantId, cancellationToken);
        var operation = createOnly ? "create-claim-mapping" : "save-claim-mapping";
        var fingerprint = EfIdentityStoreSupport.Fingerprint(operation, rule.TenantId, rule.Provider, rule.Id, rule.MatchClaimType, rule.MatchValue, EfIdentityStoreSupport.SerializeSet(rule.GrantRoles), EfIdentityStoreSupport.SerializeSet(rule.GrantPermissions), rule.Order.ToString(CultureInfo.InvariantCulture), rule.StopOnMatch.ToString(), expectedVersion?.ToString(CultureInfo.InvariantCulture));
        return await atomic.ExecuteAsync(EfIdentityAtomicMutation.Create(operation, fingerprint, rule.TenantId), async token =>
        {
            var id = Id(rule.TenantId, rule.Provider, rule.Id);
            var row = await context.ClaimMappings.SingleOrDefaultAsync(x => x.Id == id, token);
            if (createOnly && row is not null) return Conflict(id);
            if (expectedVersion is > 0 && (row is null || row.Revision != expectedVersion)) return row is null ? NotFound(id) : Conflict(id);
            if (expectedVersion == 0 && !createOnly && row is null) return NotFound(id);
            if (row is null) { row = new ClaimMappingEntity { Id = id, Revision = 1 }; context.ClaimMappings.Add(row); }
            else row.Revision = checked(row.Revision + 1);
            Apply(row, rule);
            return row.Revision == 1 ? new EfIdentityWriteResult(EfIdentityWriteStatus.Inserted, 1, "Identity claim mapping inserted.", id) : new EfIdentityWriteResult(EfIdentityWriteStatus.Updated, row.Revision, "Identity claim mapping updated.", id);
        }, cancellationToken);
    }

    private IQueryable<ClaimMappingEntity> Query(string tenantId, string provider) =>
        context.ClaimMappings.AsNoTracking().Where(x => x.TenantLookupKey == EfIdentityStoreSupport.TenantLookup(tenantId) && x.ProviderLookupKey == EfIdentityStoreSupport.Lookup(tenantId, provider));

    private static void Apply(ClaimMappingEntity row, ClaimMappingRule rule)
    {
        row.Id = Id(rule.TenantId, rule.Provider, rule.Id); row.TenantId = rule.TenantId; row.TenantLookupKey = EfIdentityStoreSupport.TenantLookup(rule.TenantId);
        row.Provider = rule.Provider; row.ProviderLookupKey = EfIdentityStoreSupport.Lookup(rule.TenantId, rule.Provider); row.RuleId = rule.Id; row.RuleLookupKey = EfIdentityStoreSupport.CompoundKey(rule.TenantId, rule.Provider, rule.Id); row.RuleIdOrderKey = EfIdentityStoreSupport.SortableOrderKey(rule.Id, nameof(rule.Id));
        row.MatchClaimType = rule.MatchClaimType; row.MatchValue = rule.MatchValue; row.GrantRolesJson = EfIdentityStoreSupport.SerializeSet(rule.GrantRoles); row.GrantPermissionsJson = EfIdentityStoreSupport.SerializeSet(rule.GrantPermissions); row.Order = rule.Order; row.StopOnMatch = rule.StopOnMatch;
    }

    private static ClaimMappingRule Map(ClaimMappingEntity row) => new(row.RuleId, row.TenantId, row.Provider, row.MatchClaimType, row.MatchValue, EfIdentityStoreSupport.DeserializeSet(row.GrantRolesJson), EfIdentityStoreSupport.DeserializeSet(row.GrantPermissionsJson), row.Order, row.StopOnMatch);
    private static string Id(string tenantId, string provider, string ruleId) => EfIdentityStoreSupport.CompoundKey(tenantId, provider, ruleId);
    private static EfIdentityWriteResult Conflict(string id) => new(EfIdentityWriteStatus.Conflict, Message: "Identity claim mapping write conflicted.", Id: id);
    private static EfIdentityWriteResult NotFound(string id) => new(EfIdentityWriteStatus.NotFound, Message: "Identity claim mapping was not found.", Id: id);
    private static void ValidateRule(ClaimMappingRule rule) { Validate(rule.TenantId, nameof(rule.TenantId)); Validate(rule.Id, nameof(rule.Id)); _ = EfIdentityStoreSupport.SortableOrderKey(rule.Id, nameof(rule.Id)); Validate(rule.Provider, nameof(rule.Provider)); ArgumentNullException.ThrowIfNull(rule.MatchClaimType); ArgumentNullException.ThrowIfNull(rule.MatchValue); ArgumentNullException.ThrowIfNull(rule.GrantRoles); ArgumentNullException.ThrowIfNull(rule.GrantPermissions); }
    private static void Validate(string value, string parameter) { ArgumentNullException.ThrowIfNull(value); if (value.Length > IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength) throw new ArgumentException($"Identity key values cannot exceed {IdentityProviderConfigurationCanonicalizer.MaximumIdentityLength} UTF-16 code units.", parameter); }
    private void Prepare(string tenantId, CancellationToken cancellationToken) { Validate(tenantId, nameof(tenantId)); EfIdentityStoreSupport.EnsureTenant(accessContextAccessor, tenantId); context.EnsureProviderBinding(); cancellationToken.ThrowIfCancellationRequested(); }
    private static IdentityEntityFrameworkPersistenceException Failure(string message, Exception exception) => new(message, exception);
}

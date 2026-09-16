using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Data.Common;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// EF implementation of all Activities Design read ports and mutation commands. A single scoped
/// context is deliberately used for a command so EF's transaction is the atomic boundary across
/// the A01-A21 tables. Complex provider payloads remain opaque JSON and never become provider SQL.
/// </summary>
public sealed class EfActivityDesignStores(
    ActivitiesDesignDbContext db,
    IPersistenceAccessContextAccessor? accessContextAccessor = null,
    IDesignAtomicWriter? atomicWriter = null,
    EfActivityManagementProjectionWriter? projectionWriter = null) :
    IActivityDefinitionStore,
    IActivityDefinitionVersionStore,
    IActivityDefinitionAuthoringStore,
    IActivityDefinitionDraftStore,
    IActivityDefinitionVersionPublicationStore,
    IActivityDefinitionLayoutStore,
    IActivityDraftValidationStore,
    IActivityForkStore,
    IActivityDirectDependencyStore,
    IActivityDependencyProjectionStore,
    IActivityUpgradePlanStore,
    IActivityUpgradeApplyReceiptStore,
    IActivityAvailabilitySettingsStore,
    IActivityDefinitionManagementProjectionStore,
    IActivityDefinitionHasher,
    ICreateActivityDefinitionCommand,
    IAddActivityDefinitionCommand,
    IAddActivityDefinitionVersionCommand,
    ICreateActivityDraftCommand,
    IUpdateActivityDefinitionPresentationCommand,
    IUpdateActivityDraftPresentationCommand,
    IStoreActivityDraftValidationCommand,
    IChangeActivityVersionLifecycleCommand,
    ISetActivityDefinitionRecommendationCommand,
    IActivityDependencyProjectionRebuilder,
    ISaveActivityForkCandidateCommand,
    IPruneActivityForkCandidatesCommand,
    IApplyActivityForkCandidateCommand,
    ICreateActivityDraftConflictCopyCommand,
    IReplaceActivityDraftCommand,
    IApplyActivityContractProposalCommand,
    IDiscardActivityDraftCommand,
    IRecommendedActivityDefinitionPickerStore
{
    private readonly IPersistenceAccessContextAccessor? access = accessContextAccessor;
    private readonly IDesignAtomicWriter? atomic = atomicWriter;
    private readonly EfActivityManagementProjectionWriter? projections = projectionWriter;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly DefaultActivityDefinitionHasher SharedActivityDefinitionHasher = new();
    private const int MaximumPageSize = 500;
    private const int IntegrityValidationBatchSize = 256;

    /// <summary>The context these stores read and write through.</summary>
    internal ActivitiesDesignDbContext Context => db;

    public async Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
        await ById(Access(db.ActivityDefinitions.AsNoTracking()), id).SingleOrDefaultAsync(cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinition), id);

    public async Task<ActivityDefinition?> FindAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTenantAgnostic(filter);
        await EnsureSearchCatalogBoundAsync(filter, cancellationToken);
        var query = ApplyDefinitionFilter(Access(db.ActivityDefinitions.AsNoTracking()), filter);
        if (filter.Id is not null)
            return await query.SingleOrDefaultAsync(cancellationToken);
        return await WithPhysicalIdentityTie(query.OrderBy(x => x.ActivityTypeKey).ThenBy(x => x.Id)).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Substring search cannot use an index, so it is refused once the visible catalog
    /// exceeds this many rows rather than scanning an unbounded table.
    /// </summary>
    public const int MaximumSearchCatalogRows = 10_000;

    private async Task EnsureSearchCatalogBoundAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filter.SearchTerm))
            return;
        var rows = await Access(db.ActivityDefinitions.AsNoTracking()).Take(MaximumSearchCatalogRows + 1).CountAsync(cancellationToken);
        if (rows > MaximumSearchCatalogRows)
            throw new InvalidOperationException(
                $"Activity-definition substring search is refused when the current scope contains more than {MaximumSearchCatalogRows} rows.");
    }

    private static IQueryable<ActivityDefinition> ApplyDefinitionFilter(IQueryable<ActivityDefinition> query, ActivityDefinitionFilter filter)
    {
        if (filter.Id is not null) query = ById(query, filter.Id);
        if (filter.Ids is not null) query = ByIds(query, filter.Ids);
        if (filter.ActivityTypeKey is not null) query = query.Where(x => x.ActivityTypeKey == filter.ActivityTypeKey);
        if (filter.ActivityTypeKeys is not null) query = query.Where(x => filter.ActivityTypeKeys.Contains(x.ActivityTypeKey));
        if (filter.Category is not null) query = query.Where(x => x.Category == filter.Category);
        if (filter.DisplayName is not null) query = query.Where(x => x.DisplayName == filter.DisplayName);
        if (filter.Description is not null) query = query.Where(x => x.Description != null && x.Description.Contains(filter.Description));
        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var search = filter.SearchTerm;
            query = query.Where(x => x.Id.Contains(search) || x.ActivityTypeKey.Contains(search) ||
                                     x.Category.Contains(search) || (x.DisplayName != null && x.DisplayName.Contains(search)) ||
                                     (x.Description != null && x.Description.Contains(search)));
        }
        return query;
    }

    public async Task<IReadOnlyList<ActivityDefinition>> ListAsync(ActivityDefinitionFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        EnsureTenantAgnostic(filter);
        await EnsureSearchCatalogBoundAsync(filter, cancellationToken);
        return await ReadAllPagesAsync(
            ApplyDefinitionFilter(Access(db.ActivityDefinitions.AsNoTracking()), filter),
            query => WithPhysicalIdentityTie(query.OrderBy(x => x.ActivityTypeKey).ThenBy(x => x.Id)),
            cancellationToken);
    }

    public async Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(string id, string activityTypeKey, CancellationToken cancellationToken = default) =>
        await ById(Access(db.ActivityDefinitions.AsNoTracking()), id).SingleOrDefaultAsync(cancellationToken)
        ?? await Access(db.ActivityDefinitions.AsNoTracking()).FirstOrDefaultAsync(x => x.ActivityTypeKey == activityTypeKey, cancellationToken);

    public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
        Access(db.ActivityDefinitions).AnyAsync(x => x.ActivityTypeKey == activityTypeKey, cancellationToken);

    async Task<ActivityDefinitionVersion> IActivityDefinitionVersionStore.GetAsync(string versionId, CancellationToken cancellationToken) =>
        HydrateVersion(await ById(Access(db.ActivityDefinitionVersions.AsNoTracking()), versionId).SingleOrDefaultAsync(cancellationToken)
            ?? throw EntityNotFoundException.ForEntity(typeof(ActivityDefinitionVersion), versionId));

    public async Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default)
    {
        var version = await ((IActivityDefinitionVersionStore)this).GetAsync(versionId, cancellationToken);
        version.Definition = await ById(Access(db.ActivityDefinitions.AsNoTracking()), version.DefinitionId)
            .SingleOrDefaultAsync(x => x.TenantId == version.TenantId, cancellationToken);
        return version;
    }

    async Task<ActivityDefinitionVersion?> IActivityDefinitionVersionStore.FindByDefinitionAndSortKeyAsync(string definitionId, string semVerSortKey, CancellationToken cancellationToken)
    {
        var version = await ByReference(Access(db.ActivityDefinitionVersions.AsNoTracking()), nameof(ActivityDefinitionVersion.DefinitionId), definitionId).SingleOrDefaultAsync(x => x.SemVerSortKey == semVerSortKey, cancellationToken);
        return version is null ? null : HydrateVersion(version);
    }

    async Task<IReadOnlyList<ActivityDefinitionVersion>> IActivityDefinitionVersionStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
        (await ReadAllPagesAsync(ByReference(Access(db.ActivityDefinitionVersions.AsNoTracking()), nameof(ActivityDefinitionVersion.DefinitionId), definitionId), query => WithPhysicalIdentityTie(query.OrderBy(x => x.DefinitionId).ThenBy(x => x.SemVerSortKey).ThenBy(x => x.Id)), cancellationToken)).Select(HydrateVersion).Where(x => x is not null).Cast<ActivityDefinitionVersion>().ToArray();

    async Task<IReadOnlyList<ActivityDefinitionVersion>> IActivityDefinitionVersionStore.ListByDefinitionIdsAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken)
    {
        var ids = definitionIds?.Distinct(StringComparer.Ordinal).ToArray() ?? throw new ArgumentNullException(nameof(definitionIds));
        return (await ReadAllPagesAsync(ByReferences(Access(db.ActivityDefinitionVersions.AsNoTracking()), nameof(ActivityDefinitionVersion.DefinitionId), ids), query => WithPhysicalIdentityTie(query.OrderBy(x => x.DefinitionId).ThenBy(x => x.SemVerSortKey).ThenBy(x => x.Id)), cancellationToken)).Select(HydrateVersion).Where(x => x is not null).Cast<ActivityDefinitionVersion>().ToArray();
    }

    async Task<IReadOnlyList<ActivityDefinitionVersion>> IActivityDefinitionVersionStore.ListAsync(CancellationToken cancellationToken) =>
        (await ReadAllPagesAsync(Access(db.ActivityDefinitionVersions.AsNoTracking()), query => WithPhysicalIdentityTie(query.OrderBy(x => x.DefinitionId).ThenBy(x => x.SemVerSortKey).ThenBy(x => x.Id)), cancellationToken)).Select(HydrateVersion).Where(x => x is not null).Cast<ActivityDefinitionVersion>().ToArray();

    async Task<ActivityDefinitionAuthoringState?> IActivityDefinitionAuthoringStore.FindAsync(string definitionId, CancellationToken cancellationToken) =>
        await ByReference(Access(db.ActivityDefinitionAuthoringStates.AsNoTracking()), nameof(ActivityDefinitionAuthoringState.DefinitionId), definitionId).SingleOrDefaultAsync(cancellationToken);

    async Task<IReadOnlyList<ActivityDefinitionAuthoringState>> IActivityDefinitionAuthoringStore.ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken)
    {
        var ids = definitionIds.Distinct(StringComparer.Ordinal).ToArray();
        return await ReadAllPagesAsync(ByReferences(Access(db.ActivityDefinitionAuthoringStates.AsNoTracking()), nameof(ActivityDefinitionAuthoringState.DefinitionId), ids), query => WithPhysicalIdentityTie(query.OrderBy(x => x.DefinitionId)), cancellationToken);
    }

    Task<ActivityDefinitionDraft?> IActivityDefinitionDraftStore.FindAsync(string draftId, CancellationToken cancellationToken) =>
        ById(Access(db.ActivityDefinitionDrafts.AsNoTracking()), draftId).SingleOrDefaultAsync(cancellationToken);

    async Task<IReadOnlyList<ActivityDefinitionDraft>> IActivityDefinitionDraftStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
        await ReadAllPagesAsync(ByReference(Access(db.ActivityDefinitionDrafts.AsNoTracking()), nameof(ActivityDefinitionDraft.DefinitionId), definitionId), query => WithPhysicalIdentityTie(query.OrderBy(x => x.Id)), cancellationToken);

    Task<ActivityDefinitionVersionPublication?> IActivityDefinitionVersionPublicationStore.FindAsync(string definitionVersionId, CancellationToken cancellationToken) =>
        ByReference(Access(db.ActivityDefinitionVersionPublications.AsNoTracking()), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), definitionVersionId).SingleOrDefaultAsync(cancellationToken);

    async Task<IReadOnlyList<ActivityDefinitionVersionPublication>> IActivityDefinitionVersionPublicationStore.ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
        await ReadAllPagesAsync(ByReference(Access(db.ActivityDefinitionVersionPublications.AsNoTracking()), nameof(ActivityDefinitionVersionPublication.DefinitionId), definitionId), query => WithPhysicalIdentityTie(query.OrderBy(x => x.Version).ThenBy(x => x.DefinitionVersionId)), cancellationToken);

    public Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(string draftId, CancellationToken cancellationToken = default) =>
        ByReference(Access(db.ActivityDefinitionDraftLayouts.AsNoTracking()), nameof(ActivityDefinitionDraftLayout.DraftId), draftId).SingleOrDefaultAsync(cancellationToken);

    public Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(string definitionVersionId, CancellationToken cancellationToken = default) =>
        ByReference(Access(db.ActivityDefinitionVersionLayouts.AsNoTracking()), nameof(ActivityDefinitionVersionLayout.DefinitionVersionId), definitionVersionId).SingleOrDefaultAsync(cancellationToken);

    Task<ActivityDraftValidationState?> IActivityDraftValidationStore.FindAsync(string draftId, long revision, CancellationToken cancellationToken) =>
        ByReference(Access(db.ActivityDraftValidations.AsNoTracking()), nameof(ActivityDraftValidationState.DraftId), draftId).SingleOrDefaultAsync(x => x.Revision == revision, cancellationToken);

    public async Task<ActivityForkCandidate?> FindCandidateAsync(string candidateId, CancellationToken cancellationToken = default)
    {
        try
        {
            var byId = await ById(Access(db.ActivityForkCandidates.AsNoTracking()), candidateId).SingleOrDefaultAsync(cancellationToken);
            if (byId is not null)
            {
                ValidateForkIdentity(byId);
                return byId;
            }
            var hash = ActivityForkIdentityMaterial.ExactHash(candidateId);
            var candidates = await Access(db.ActivityForkCandidates.AsNoTracking())
                .Where(x => x.CandidateIdIdentityHash == hash && x.CandidateId == candidateId)
                .Take(2).ToListAsync(cancellationToken);
            foreach (var candidate in candidates)
                ValidateForkIdentity(candidate);
            return candidates.SingleOrDefault();
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<ActivityForkReceipt?> FindReceiptAsync(string receiptId, CancellationToken cancellationToken = default) =>
        FindReceiptCoreAsync(receiptId, cancellationToken);

    private async Task<ActivityForkReceipt?> FindReceiptCoreAsync(string receiptId, CancellationToken cancellationToken)
    {
        var receipt = await ById(Access(db.ActivityForkReceipts.AsNoTracking()), receiptId).SingleOrDefaultAsync(cancellationToken);
        return receipt is null ? null : MapReceipt(receipt);
    }

    public async Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(string ownerVersionId, CancellationToken cancellationToken = default) =>
        await ReadAllPagesAsync(ByReference(Access(db.ActivityDependencyEdges.AsNoTracking()), nameof(ActivityDependencyEdge.OwnerVersionId), ownerVersionId), query => WithPhysicalIdentityTie(query.OrderBy(x => x.OccurrenceId).ThenBy(x => x.DependencyVersionId)), cancellationToken);

    public async Task<ActivityAvailabilitySettings?> LoadAsync(string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var row = await ByScope(db.ActivityAvailabilitySettings.AsNoTracking(), AvailabilityPartitionKey(), scope).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new ActivityAvailabilitySettings { Scope = row.Scope, Mode = row.Mode, Rules = row.Rules };
    }

    public async Task SaveAsync(ActivityAvailabilitySettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Scope);
        var partition = AvailabilityPartitionKey();
        var row = await ByScope(db.ActivityAvailabilitySettings, partition, settings.Scope).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            var entry = db.Entry(new ActivityAvailabilitySettingsRecord { Id = settings.Scope, Scope = settings.Scope, Mode = settings.Mode, Rules = settings.Rules });
            entry.Property("TenantScopeKey").CurrentValue = partition;
            entry.State = EntityState.Added;
        }
        else { row.Mode = settings.Mode; row.Rules = settings.Rules; }
        await SaveAsync(cancellationToken);
    }

    public async Task<ActivityManagementSnapshot> GetCurrentSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var row = await ById(db.ActivityManagementProjectionWatermarks.AsNoTracking(), ActivityManagementProjectionWatermark.CurrentId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? new ActivityManagementSnapshot(0, DateTimeOffset.UnixEpoch) : new(row.Sequence, row.AdvancedAt);
    }

    public async Task<ActivityManagementProjectionPage<ActivityDefinitionManagementProjectionRevision>> ReadDefinitionsAsync(ActivityManagementProjectionPageQuery query, CancellationToken cancellationToken = default)
    {
        try { return await ReadDefinitionsCoreAsync(query, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (DesignPersistenceException) { throw; }
        catch (DbUpdateException exception) { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "read-definitions", null, exception); }
        catch (DbException exception) { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "read-definitions", null, exception); }
    }

    private async Task<ActivityManagementProjectionPage<ActivityDefinitionManagementProjectionRevision>> ReadDefinitionsCoreAsync(ActivityManagementProjectionPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureReadTenant(query.TenantId);
        ValidatePage(query.Offset, query.Limit);
        var snapshot = await ResolveSnapshotAsync(query.SnapshotSequence, cancellationToken);
        var rows = Access(db.ActivityDefinitionManagementProjections.AsNoTracking()).Where(x => x.ValidFromSequence <= snapshot.Sequence && x.ValidToSequenceExclusive > snapshot.Sequence);
        rows = ApplyVisibility(rows, query.TenantId);
        if (!string.IsNullOrWhiteSpace(query.Search)) rows = rows.Where(x => x.SearchText.Contains(query.Search.Trim().ToUpperInvariant()));
        if (query.Authority is { } authority) rows = rows.Where(x => x.ContentAuthorityKind == authority);
        if (!string.IsNullOrWhiteSpace(query.ProviderKey)) rows = rows.Where(x => x.HeadProviderKey == query.ProviderKey.Trim() || x.RecommendationProviderKey == query.ProviderKey.Trim());
        // The marker rejects malformed and structurally inconsistent rows in SQL. The digest
        // itself is intentionally provider-neutral (it is defined over UTF-16 material), so
        // validate full material in bounded, stable-order batches before applying page bounds.
        // This prevents a valid-looking provider marker with a stale digest from shifting totals
        // or page offsets without an unbounded materialization or parameter list.
        rows = rows.Where(x => x.ContentAuthorityIsValid && x.ContentAuthorityJson != null && x.ContentAuthorityJson == x.ContentAuthorityCanonicalJson);
        var items = new List<ActivityDefinitionManagementProjectionRevision>(query.Limit);
        var total = 0L;
        string? lastSortKey = null;
        string? lastResourceId = null;
        string? lastTenantScopeKey = null;
        string? lastResourceHash = null;
        string? lastRevisionHash = null;
        while (true)
        {
            var batchQuery = rows;
            if (lastSortKey is not null)
                batchQuery = batchQuery.Where(x =>
                    string.Compare(x.SortKey, lastSortKey) > 0 ||
                    string.Compare(x.SortKey, lastSortKey) == 0 &&
                    (string.Compare(x.ResourceId, lastResourceId) > 0 ||
                     string.Compare(x.ResourceId, lastResourceId) == 0 &&
                     (string.Compare(EF.Property<string>(x, "TenantScopeKey"), lastTenantScopeKey) > 0 ||
                      string.Compare(EF.Property<string>(x, "TenantScopeKey"), lastTenantScopeKey) == 0 &&
                      (string.Compare(EF.Property<string>(x, "ResourceIdIdentityHash"), lastResourceHash) > 0 ||
                       string.Compare(EF.Property<string>(x, "ResourceIdIdentityHash"), lastResourceHash) == 0 &&
                       string.Compare(EF.Property<string>(x, "IdIdentityHash"), lastRevisionHash) > 0))));
            var batch = await WithResourceIdentityTie(batchQuery.OrderBy(x => x.SortKey).ThenBy(x => x.ResourceId))
                .Take(IntegrityValidationBatchSize)
                .Select(x => new
                {
                    Row = x,
                    ScopeKey = EF.Property<string>(x, "TenantScopeKey"),
                    ResourceHash = EF.Property<string>(x, "ResourceIdIdentityHash"),
                    RevisionHash = EF.Property<string>(x, "IdIdentityHash")
                })
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;
            foreach (var entry in batch)
            {
                var row = entry.Row;
                if (!HasValidContentAuthority(row)) continue;
                if (total >= query.Offset && items.Count < query.Limit) items.Add(row);
                total++;
            }
            var last = batch[^1];
            lastSortKey = last.Row.SortKey;
            lastResourceId = last.Row.ResourceId;
            lastTenantScopeKey = last.ScopeKey;
            lastResourceHash = last.ResourceHash;
            lastRevisionHash = last.RevisionHash;
            if (batch.Count < IntegrityValidationBatchSize) break;
        }
        return new(items, query.Offset + query.Limit < total ? query.Offset + query.Limit : null, total, snapshot);
    }

    public async Task<ActivityDefinitionManagementProjectionRevision?> FindDefinitionAsync(string definitionId, string? tenantId, long? snapshotSequence = null, CancellationToken cancellationToken = default)
    {
        EnsureReadTenant(tenantId);
        var sequence = snapshotSequence ?? (await GetCurrentSnapshotAsync(cancellationToken)).Sequence;
        var rows = Access(db.ActivityDefinitionManagementProjections.AsNoTracking())
            .Where(x => x.ValidFromSequence <= sequence && x.ValidToSequenceExclusive > sequence);
        rows = ByReference(rows, nameof(ActivityDefinitionManagementProjectionRevision.ResourceId), definitionId)
            .Where(x => x.ContentAuthorityIsValid && x.ContentAuthorityJson != null && x.ContentAuthorityJson == x.ContentAuthorityCanonicalJson);
        rows = ApplyVisibility(rows, tenantId);
        var row = await rows.FirstOrDefaultAsync(cancellationToken);
        return row is not null && HasValidContentAuthority(row) ? row : null;
    }

    public async Task<ActivityManagementProjectionPage<ActivityDefinitionDraftManagementProjectionRevision>> ReadDraftsAsync(string definitionId, ActivityManagementProjectionPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(query);
        EnsureReadTenant(query.TenantId);
        ValidatePage(query.Offset, query.Limit);
        var snapshot = await ResolveSnapshotAsync(query.SnapshotSequence, cancellationToken);
        var rows = ByReference(Access(db.ActivityDraftManagementProjections.AsNoTracking()), nameof(ActivityDefinitionDraftManagementProjectionRevision.DefinitionId), definitionId).Where(x => x.ValidFromSequence <= snapshot.Sequence && x.ValidToSequenceExclusive > snapshot.Sequence);
        rows = ApplyVisibility(rows, query.TenantId);
        if (query.DraftStatus is { } status) rows = rows.Where(x => x.Status == status);
        if (!string.IsNullOrWhiteSpace(query.ProviderKey)) rows = rows.Where(x => x.ProviderKey == query.ProviderKey.Trim());
        var total = await rows.LongCountAsync(cancellationToken);
        var items = await WithResourceIdentityTie(rows.OrderBy(x => x.SortKey).ThenBy(x => x.ResourceId)).Skip(query.Offset).Take(query.Limit).ToListAsync(cancellationToken);
        return new(items, query.Offset + items.Count < total ? query.Offset + items.Count : null, total, snapshot);
    }

    public async Task<ActivityManagementProjectionPage<ActivityDefinitionVersionManagementProjectionRevision>> ReadVersionsAsync(string definitionId, ActivityManagementProjectionPageQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentNullException.ThrowIfNull(query);
        EnsureReadTenant(query.TenantId);
        ValidatePage(query.Offset, query.Limit);
        var snapshot = await ResolveSnapshotAsync(query.SnapshotSequence, cancellationToken);
        var rows = ByReference(Access(db.ActivityVersionManagementProjections.AsNoTracking()), nameof(ActivityDefinitionVersionManagementProjectionRevision.DefinitionId), definitionId).Where(x => x.ValidFromSequence <= snapshot.Sequence && x.ValidToSequenceExclusive > snapshot.Sequence);
        rows = ApplyVisibility(rows, query.TenantId);
        if (query.VersionLifecycle is { } lifecycle) rows = rows.Where(x => x.Lifecycle == lifecycle);
        if (!string.IsNullOrWhiteSpace(query.ProviderKey)) rows = rows.Where(x => x.ProviderKey == query.ProviderKey.Trim());
        var total = await rows.LongCountAsync(cancellationToken);
        var items = await WithResourceIdentityTie(rows.OrderBy(x => x.SortKey).ThenBy(x => x.ResourceId)).Skip(query.Offset).Take(query.Limit).ToListAsync(cancellationToken);
        return new(items, query.Offset + items.Count < total ? query.Offset + items.Count : null, total, snapshot);
    }

    public async Task<ActivityUpgradePlan?> FindAsync(string planId, CancellationToken cancellationToken = default)
    {
        var plan = await ReadJsonAsync<ActivityUpgradePlan>(ById(Access(db.ActivityUpgradePlans.AsNoTracking()), planId).SingleOrDefaultAsync(cancellationToken));
        if (plan is not null) EnsureReadTenant(plan.TenantId);
        return plan;
    }

    public async Task SaveAsync(ActivityUpgradePlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureTenant(plan.TenantId);
        var existing = await ById(Access(db.ActivityUpgradePlans), plan.PlanId).SingleOrDefaultAsync(cancellationToken);
        if (existing is not null && existing.PlanJson != SerializeJson(plan, nameof(ActivityUpgradePlan))) throw new InvalidOperationException($"Activity upgrade plan '{plan.PlanId}' is immutable.");
        if (existing is null) db.ActivityUpgradePlans.Add(new ActivityUpgradePlanRecord { Id = plan.PlanId, TenantId = plan.TenantId, PlanId = plan.PlanId, PlanJson = SerializeJson(plan, nameof(ActivityUpgradePlan)) });
        await SaveAsync(cancellationToken);
    }

    public async Task LinkSuccessorAsync(string planId, string successorPlanId, CancellationToken cancellationToken = default)
    {
        var row = await ById(Access(db.ActivityUpgradePlans), planId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException($"Activity upgrade plan '{planId}' was not found.");
        var plan = DeserializeJson<ActivityUpgradePlan>(row.PlanJson, nameof(ActivityUpgradePlan)) ?? throw new InvalidOperationException("Unreadable upgrade plan.");
        EnsureTenant(plan.TenantId);
        if (plan.SuccessorPlanId is not null && plan.SuccessorPlanId != successorPlanId) throw new InvalidOperationException("Activity upgrade plan already has a successor.");
        if (plan.SuccessorPlanId is null) { row.PlanJson = SerializeJson(plan with { SuccessorPlanId = successorPlanId, Status = ActivityUpgradePlanStatus.Superseded }, nameof(ActivityUpgradePlan)); await SaveAsync(cancellationToken); }
    }

    async Task<ActivityUpgradeApplyReceipt?> IActivityUpgradeApplyReceiptStore.FindAsync(string receiptId, CancellationToken cancellationToken)
    {
        var receipt = await ReadJsonAsync<ActivityUpgradeApplyReceipt>(ById(Access(db.ActivityUpgradeApplyReceipts.AsNoTracking()), receiptId).SingleOrDefaultAsync(cancellationToken));
        if (receipt is not null) EnsureReadTenant(receipt.TenantId);
        return receipt;
    }

    public async Task<ActivityUpgradeApplyReceipt?> FindByIdempotencyKeyAsync(string planId, string idempotencyKeyHash, CancellationToken cancellationToken = default)
    {
        var receipt = await ReadJsonAsync<ActivityUpgradeApplyReceipt>(ByReference(Access(db.ActivityUpgradeApplyReceipts.AsNoTracking()), nameof(ActivityUpgradeApplyReceiptRecord.PlanId), planId).SingleOrDefaultAsync(x => x.IdempotencyKeyHash == idempotencyKeyHash, cancellationToken));
        if (receipt is not null) EnsureReadTenant(receipt.TenantId);
        return receipt;
    }

    public async Task<bool> TryCreateAsync(ActivityUpgradeApplyReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        EnsureTenant(receipt.TenantId);
        if (await ById(Access(db.ActivityUpgradeApplyReceipts), receipt.ReceiptId).AnyAsync(cancellationToken)) return false;
        db.ActivityUpgradeApplyReceipts.Add(new ActivityUpgradeApplyReceiptRecord { Id = receipt.ReceiptId, TenantId = receipt.TenantId, ReceiptId = receipt.ReceiptId, PlanId = receipt.PlanId, IdempotencyKeyHash = receipt.IdempotencyKeyHash, ReceiptJson = SerializeJson(receipt, nameof(ActivityUpgradeApplyReceipt)) });
        try { await SaveAsync(cancellationToken); return true; }
        catch (DesignPersistenceException exception) when (exception.InnerException is DbUpdateException providerFailure && EfRelationalExceptionClassifier.IsUniqueConstraintViolation(providerFailure))
        {
            db.ChangeTracker.Clear();
            // The receipt row is the authority for a create race. EF may report unrelated
            // entries from the same failed batch, so do not classify from Entries membership.
            var winner = await ById(Access(db.ActivityUpgradeApplyReceipts.AsNoTracking()), receipt.ReceiptId).SingleOrDefaultAsync(CancellationToken.None);
            if (winner is not null) return false;
            throw;
        }
    }

    public async Task<ActivityUpgradeApplyReceipt?> TryReclaimAsync(ActivityUpgradeApplyReceipt receipt, DateTimeOffset reclaimedAt, DateTimeOffset leaseExpiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (leaseExpiresAt <= reclaimedAt) throw new ArgumentOutOfRangeException(nameof(leaseExpiresAt));
        var row = await ById(Access(db.ActivityUpgradeApplyReceipts), receipt.ReceiptId).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;
        var current = await ReadJsonAsync<ActivityUpgradeApplyReceipt>(Task.FromResult<ActivityUpgradeApplyReceiptRecord?>(row));
        if (current is not null) EnsureTenant(current.TenantId);
        if (current is null || current.Status != ActivityUpgradeApplyReceiptStatus.Preparing || current.Revision != receipt.Revision || current.LeaseExpiresAt > reclaimedAt) return null;
        var reclaimed = current with { UpdatedAt = reclaimedAt, Revision = checked(current.Revision + 1), LeaseExpiresAt = leaseExpiresAt };
        row.ReceiptJson = SerializeJson(reclaimed, nameof(ActivityUpgradeApplyReceipt));
        try { await SaveAsync(cancellationToken); return reclaimed; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return null; }
    }

    public async Task RejectAsync(ActivityUpgradeApplyReceipt receipt, int statusCode, string errorCode, IReadOnlyList<ActivityDiagnostic> diagnostics, DateTimeOffset rejectedAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var row = await ById(Access(db.ActivityUpgradeApplyReceipts), receipt.ReceiptId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Activity upgrade apply receipt '{receipt.ReceiptId}' was not found.");
        var current = await ReadJsonAsync<ActivityUpgradeApplyReceipt>(Task.FromResult<ActivityUpgradeApplyReceiptRecord?>(row));
        if (current is not null) EnsureTenant(current.TenantId);
        if (current is null || current.Status != ActivityUpgradeApplyReceiptStatus.Preparing || current.Revision != receipt.Revision) return;
        var rejected = current with { Status = ActivityUpgradeApplyReceiptStatus.Rejected, UpdatedAt = rejectedAt, Revision = checked(current.Revision + 1), Diagnostics = ActivityDiagnosticOrderer.Order(diagnostics), RejectionStatusCode = statusCode, RejectionCode = errorCode };
        row.ReceiptJson = SerializeJson(rejected, nameof(ActivityUpgradeApplyReceipt));
        await SaveAsync(cancellationToken);
    }

    public async Task RebuildAsync(ActivityDependencyProjectionRebuild rebuild, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rebuild);
        EnsurePrivilegedAcrossScopesForProjectionReplacement();
        ArgumentException.ThrowIfNullOrWhiteSpace(rebuild.RebuildId);
        if (rebuild.Sequence < 0)
            throw new ArgumentException("Projection rebuild sequence must be non-negative.", nameof(rebuild));
        ValidateProjectionItems(rebuild.Items);

        var current = await ById(db.ActivityDependencyProjections, ActivityDependencyProjectionState.CurrentId).SingleOrDefaultAsync(cancellationToken);
        if (current is not null)
        {
            if (rebuild.Sequence < current.Sequence)
                throw new InvalidOperationException("A dependency projection rebuild cannot move its sequence backwards.");
            if (rebuild.Sequence == current.Sequence && !StringComparer.Ordinal.Equals(rebuild.RebuildId, current.RebuildId))
                throw new InvalidOperationException("A projection sequence is already bound to another rebuild identity.");
        }

        if (current is null)
            db.ActivityDependencyProjections.Add(new ActivityDependencyProjectionState
            {
                Id = ActivityDependencyProjectionState.CurrentId,
                RebuildId = rebuild.RebuildId,
                Sequence = rebuild.Sequence,
                AsOf = rebuild.AsOf,
                Items = rebuild.Items.OrderBy(ItemSortKey, StringComparer.Ordinal).ToList()
            });
        else
        {
            current.RebuildId = rebuild.RebuildId;
            current.Sequence = rebuild.Sequence;
            current.AsOf = rebuild.AsOf;
            current.Items = rebuild.Items.OrderBy(ItemSortKey, StringComparer.Ordinal).ToList();
        }
        await SaveAsync(cancellationToken);
    }

    public async Task<ActivityDependencyProjectionRebuild> RebuildCurrentAsync(string rebuildId, DateTimeOffset asOf, IReadOnlyList<ActivityDependencyItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rebuildId);
        ArgumentNullException.ThrowIfNull(items);
        var current = await ById(db.ActivityDependencyProjections.AsNoTracking(), ActivityDependencyProjectionState.CurrentId).SingleOrDefaultAsync(cancellationToken);
        var sequence = checked((current?.Sequence ?? 0) + 1);
        var rebuild = new ActivityDependencyProjectionRebuild(rebuildId, sequence, asOf, items);
        await RebuildAsync(rebuild, cancellationToken);
        return rebuild;
    }

    public async Task<ActivityDependencyProjectionSlice> ReadAsync(ActivityDependencyProjectionReadRequest request, CancellationToken cancellationToken = default)
    {
        EnsureReadTenant(request.TenantId);
        ValidatePage(request.Offset, request.Limit);
        var root = await ByReference(Access(db.ActivityDefinitionVersionPublications.AsNoTracking()), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), request.RootVersionId)
            .SingleOrDefaultAsync(x => x.TenantId == request.TenantId || request.TenantId != null && x.TenantId == null, cancellationToken)
            ?? throw new InvalidOperationException($"Activity version publication '{request.RootVersionId}' was not found.");
        var owner = new ActivityDefinitionReference("ActivityVersion", root.DefinitionId, root.DefinitionVersionId, root.Version, TenantId: root.TenantId, Lifecycle: root.Lifecycle);
        var projection = await ById(db.ActivityDependencyProjections.AsNoTracking(), ActivityDependencyProjectionState.CurrentId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Activity dependency projection has not been initialized.");
        var watermark = Fingerprint(projection);
        if (request.Watermark is not null && !StringComparer.Ordinal.Equals(request.Watermark, watermark))
            throw new ActivityDependencyWatermarkExpiredException(request.Watermark);
        var includeDrafts = request.Query.Include.Contains("Drafts");
        var includeVersions = request.Query.Include.Contains("Versions");
        var scoped = projection.Items
            .Where(x => IsIncluded(x.Owner.Kind, includeDrafts, includeVersions))
            .Where(x => (x.Owner.TenantId == request.TenantId || request.TenantId is not null && x.Owner.TenantId is null) &&
                        (x.Dependency.TenantId == request.TenantId || request.TenantId is not null && x.Dependency.TenantId is null))
            .ToArray();
        var visible = TraverseProjection(request.RootVersionId, request.Query.Direction, request.Query.Transitive, scoped);
        var items = visible.Skip(request.Offset).Take(request.Limit).ToArray();
        return new(owner, new(ActivityDependencyConsistencyKind.DerivedProjection, false, projection.Sequence, projection.AsOf, projection.RebuildId), items, watermark, request.Offset + items.Length < visible.Length ? request.Offset + items.Length : null);
    }

    public string Hash(Elsa.Activities.Design.Core.Contracts.IActivityDefinition definition, Elsa.Activities.Design.Core.Contracts.IActivityDefinitionVersion version) =>
        SharedActivityDefinitionHasher.Hash(definition, version);

    public async Task ExecuteAsync(CreateActivityDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateCreate(request);
        EnsureTenant(request.Definition.TenantId);
        if (atomic is not null)
        {
            var material = new
            {
                Definition = new { request.Definition.ActivityTypeKey, request.Definition.Category, request.Definition.DisplayName, request.Definition.Description },
                Authoring = new { request.AuthoringState.ContentAuthority, request.AuthoringState.ForkedFrom, request.AuthoringState.HeadVersionId, request.AuthoringState.RecommendedVersionId },
                Draft = new { request.InitialDraft.DefinitionId, request.InitialDraft.Revision, request.InitialDraft.SourceVersionId, request.InitialDraft.PresentationLabel, request.InitialDraft.Status, request.InitialDraft.State },
                Layout = request.InitialLayout.Records
            };
            await EfDesignAtomicCommand.ExecuteAsync(atomic, new DesignOperationKey($"create:{request.Definition.Id}"), "activity.authoring.create.v1", material,
                ["activityDefinition", "activityDefinitionAuthoring", "activityDefinitionDraft", "activityDefinitionDraftLayout"], async (context, token) =>
                {
                    context.Db.ActivityDefinitions.Add(request.Definition);
                    context.Db.ActivityDefinitionAuthoringStates.Add(request.AuthoringState);
                    context.Db.ActivityDefinitionDrafts.Add(request.InitialDraft);
                    context.Db.ActivityDefinitionDraftLayouts.Add(request.InitialLayout);
                    if (projections is not null)
                        await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(
                            DateTimeOffset.UtcNow,
                            [new EfActivityManagementDefinitionChange(request.Definition, request.AuthoringState)],
                            [request.InitialDraft], []), token);
                    return "accepted";
                }, tenantId: request.Definition.TenantId, cancellationToken: cancellationToken);
            return;
        }
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (request.Definition.TenantId != request.InitialDraft.TenantId || request.Definition.Id == string.Empty) throw new InvalidOperationException("Activity definition participants must share a tenant and valid identity.");
            if (await Access(db.ActivityDefinitions).AnyAsync(x => x.TenantId == request.Definition.TenantId && x.ActivityTypeKey == request.Definition.ActivityTypeKey, cancellationToken)) throw new InvalidOperationException("Activity definition key already exists.");
            db.ActivityDefinitions.Add(request.Definition); db.ActivityDefinitionAuthoringStates.Add(request.AuthoringState); db.ActivityDefinitionDrafts.Add(request.InitialDraft); db.ActivityDefinitionDraftLayouts.Add(request.InitialLayout);
            if (projections is not null)
                await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(
                    DateTimeOffset.UtcNow,
                    [new EfActivityManagementDefinitionChange(request.Definition, request.AuthoringState)],
                    [request.InitialDraft], []), cancellationToken);
            else await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await EfPersistenceCleanup.RollbackQuietlyAsync(transaction);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public async Task<ActivityDefinitionCreated> Execute(DesignOperationKey operationKey, ActivityDefinition definition, ActivityDefinitionVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(version);
        if (definition.Id != version.DefinitionId || definition.TenantId != version.TenantId)
            throw new ArgumentException("The activity definition and version must share identity and tenant ownership.", nameof(version));
        EnsureTenant(definition.TenantId);
        EnsureTenant(version.TenantId);
        PrepareVersion(version);
        var material = new { definition.ActivityTypeKey, definition.Category, definition.DisplayName, definition.Description, version.Version, version.DefinitionId, version.Hash, version.ProviderKey, version.ProviderSchemaVersion, version.ConsumerKey, version.ConsumerSchemaVersion, version.SourceKind, version.SourceId, version.ExecutionType, DescriptorPayload = version.DescriptorPayload.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : version.DescriptorPayload, version.Inputs, version.Outputs, version.DesignFacets };
        if (atomic is null)
        {
            if (await Access(db.ActivityDefinitions).AnyAsync(x => x.TenantId == definition.TenantId && x.ActivityTypeKey == definition.ActivityTypeKey, cancellationToken)) throw new InvalidOperationException("Activity definition key already exists.");
            db.ActivityDefinitions.Add(definition); db.ActivityDefinitionVersions.Add(version); await SaveAsync(cancellationToken);
            return new(definition.Id, version.Id, version.Version, version.Hash);
        }
        return (await EfDesignAtomicCommand.ExecuteAsync(atomic, operationKey, "activity.definition.create.v1", material, ["activityDefinition", "activityDefinitionVersion"], async (context, token) =>
        {
            if (await Access(db.ActivityDefinitions).AnyAsync(x => x.TenantId == definition.TenantId && x.ActivityTypeKey == definition.ActivityTypeKey, token)) throw new InvalidOperationException("Activity definition key already exists.");
            context.Db.ActivityDefinitions.Add(definition);
            context.Db.ActivityDefinitionVersions.Add(version);
            return new ActivityDefinitionCreated(definition.Id, version.Id, version.Version, version.Hash);
        }, tenantId: definition.TenantId, cancellationToken: cancellationToken)).Value;
    }

    public async Task<ActivityDefinitionVersionAdded> Execute(DesignOperationKey operationKey, ActivityDefinitionVersion version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationKey);
        ArgumentNullException.ThrowIfNull(version);
        EnsureTenant(version.TenantId);
        PrepareVersion(version);
        if (string.IsNullOrWhiteSpace(version.DefinitionId) || string.IsNullOrWhiteSpace(version.Version))
            throw new ArgumentException("An activity version requires a definition and version identity.", nameof(version));
        if (!await ById(Access(db.ActivityDefinitions.AsNoTracking()), version.DefinitionId).AnyAsync(x => x.TenantId == version.TenantId, cancellationToken))
            throw new InvalidOperationException("The activity version definition owner was not found in the same tenant.");
        var material = new { version.DefinitionId, version.Version, version.Hash, version.ProviderKey, version.ProviderSchemaVersion, version.ConsumerKey, version.ConsumerSchemaVersion, version.SourceKind, version.SourceId, version.ExecutionType, DescriptorPayload = version.DescriptorPayload.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : version.DescriptorPayload, version.Inputs, version.Outputs, version.DesignFacets };
        if (atomic is null)
        {
            if (await ByReference(Access(db.ActivityDefinitionVersions), nameof(ActivityDefinitionVersion.DefinitionId), version.DefinitionId).AnyAsync(x => x.SemVerSortKey == version.SemVerSortKey, cancellationToken)) throw new ActivityDefinitionVersionConflictException(version.DefinitionId, version.Version);
            db.ActivityDefinitionVersions.Add(version); await SaveAsync(cancellationToken);
            return new(version.DefinitionId, version.Id, version.Version, version.Hash);
        }
        return (await EfDesignAtomicCommand.ExecuteAsync(atomic, operationKey, "activity.version.add.v1", material, ["activityDefinitionVersion"], async (context, token) =>
        {
            if (await ByReference(Access(db.ActivityDefinitionVersions), nameof(ActivityDefinitionVersion.DefinitionId), version.DefinitionId).AnyAsync(x => x.SemVerSortKey == version.SemVerSortKey, token)) throw new ActivityDefinitionVersionConflictException(version.DefinitionId, version.Version);
            context.Db.ActivityDefinitionVersions.Add(version);
            return new ActivityDefinitionVersionAdded(version.DefinitionId, version.Id, version.Version, version.Hash);
        }, tenantId: version.TenantId, cancellationToken: cancellationToken)).Value;
    }

    private static void ValidateCreate(CreateActivityDefinitionRequest request)
    {
        if (request.Definition.Id != request.AuthoringState.DefinitionId || request.Definition.Id != request.InitialDraft.DefinitionId || request.InitialDraft.Id != request.InitialLayout.DraftId)
            throw new ArgumentException("Definition, authoring, draft, and layout identities must match.", nameof(request));
        if (request.Definition.TenantId != request.AuthoringState.TenantId || request.Definition.TenantId != request.InitialDraft.TenantId || request.Definition.TenantId != request.InitialLayout.TenantId)
            throw new ArgumentException("Definition, authoring, draft, and layout tenants must match.", nameof(request));
        if (request.InitialDraft.Revision != request.InitialLayout.Revision || request.InitialDraft.Status != ActivityDefinitionDraftStatus.Active)
            throw new ArgumentException("The initial activity draft and layout must have the same active revision.", nameof(request));
    }

    public async Task ExecuteAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        try { await CreateDraftCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task CreateDraftCoreAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Draft.Id != request.Layout.DraftId || request.Draft.TenantId != request.Layout.TenantId || request.Draft.Revision != request.Layout.Revision || request.Draft.Status != ActivityDefinitionDraftStatus.Active)
            throw new ArgumentException("Draft and layout identities, tenants, and revisions must match.", nameof(request));
        EnsureTenant(request.Draft.TenantId);
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), request.Draft.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, request.Draft.DefinitionId, request.Draft.TenantId);
        if (authoring.HeadVersionId != request.ExpectedDefinitionHeadVersionId) throw new DbUpdateConcurrencyException("Activity definition head is stale.");
        if (await ById(Access(db.ActivityDefinitionDrafts), request.Draft.Id).AnyAsync(cancellationToken) || await ByReference(Access(db.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), request.Draft.Id).AnyAsync(cancellationToken)) throw new InvalidOperationException("Activity draft already exists.");
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // Advance only the shadow concurrency token inside the same transaction. This conditional
        // EF update fences the authoring read without changing the public authoring timestamp.
        await AdvanceConcurrencyFenceAsync(db.ActivityDefinitionAuthoringStates, authoring, "Activity definition head is stale.", cancellationToken);
        db.ActivityDefinitionDrafts.Add(request.Draft); db.ActivityDefinitionDraftLayouts.Add(request.Layout);
        if (projections is not null)
            await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [], [request.Draft], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<ActivityDefinition> ExecuteAsync(UpdateActivityDefinitionPresentationRequest request, CancellationToken cancellationToken = default)
    {
        try { return await UpdateDefinitionPresentationCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityDefinition> UpdateDefinitionPresentationCoreAsync(UpdateActivityDefinitionPresentationRequest request, CancellationToken cancellationToken = default)
    {
        EnsureTenant(request.TenantId);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await ById(Access(db.ActivityDefinitions), request.DefinitionId).SingleOrDefaultAsync(x => x.TenantId == request.TenantId, cancellationToken) ?? throw new InvalidOperationException("Activity definition was not found.");
        row.Category = request.Category; row.DisplayName = request.DisplayName; row.Description = request.Description; row.LastModifiedAt = request.LastModifiedAt;
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), row.Id).SingleOrDefaultAsync(cancellationToken);
        if (authoring is null) throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, row.Id, row.TenantId);
        if (projections is not null)
            await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(request.LastModifiedAt, [new EfActivityManagementDefinitionChange(row, authoring)], [], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return row;
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(UpdateActivityDraftPresentationRequest request, CancellationToken cancellationToken = default)
    {
        try { return await UpdateDraftPresentationCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityDefinitionDraft> UpdateDraftPresentationCoreAsync(UpdateActivityDraftPresentationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ChangedAt == default) throw new ArgumentException("ChangedAt is required.", nameof(request));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await ById(Access(db.ActivityDefinitionDrafts), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft was not found.");
        EnsureTenant(row.TenantId);
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), row.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, row.DefinitionId, row.TenantId);
        if (row.Status != ActivityDefinitionDraftStatus.Active || row.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Activity draft revision is stale or inactive.");
        var layout = await ByReference(Access(db.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft layout was not found.");
        if (layout.Revision != row.Revision) throw new DbUpdateConcurrencyException("Activity draft and layout revisions are inconsistent.");
        row.Revision = checked(row.Revision + 1); row.PresentationLabel = request.PresentationLabel; row.LastModifiedAt = request.ChangedAt; layout.Revision = row.Revision; layout.LastModifiedAt = request.ChangedAt;
        if (projections is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(request.ChangedAt, [], [row], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return row;
    }

    public async Task ExecuteAsync(ActivityDraftValidationState validation, CancellationToken cancellationToken = default)
    {
        try { await ValidateDraftCoreAsync(validation, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task ValidateDraftCoreAsync(ActivityDraftValidationState validation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validation);
        EnsureTenant(validation.TenantId);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var draft = await ById(Access(db.ActivityDefinitionDrafts), validation.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft was not found.");
        if (draft.TenantId != validation.TenantId) throw new InvalidOperationException("Activity draft validation ownership does not match its draft.");
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates).AsNoTracking(), nameof(ActivityDefinitionAuthoringState.DefinitionId), draft.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, draft.DefinitionId, draft.TenantId);
        if (draft.Status != ActivityDefinitionDraftStatus.Active || draft.Revision != validation.Revision) throw new DbUpdateConcurrencyException("Activity draft validation is stale or inactive.");
        await AdvanceConcurrencyFenceAsync(db.ActivityDefinitionDrafts, draft, "Activity draft validation is stale or inactive.", cancellationToken);
        db.ActivityDraftValidations.Add(validation);
        await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<ActivityDefinitionVersionPublication> ExecuteAsync(ChangeActivityVersionLifecycleRequest request, CancellationToken cancellationToken = default)
    {
        try { return await ChangeLifecycleCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityDefinitionVersionPublication> ChangeLifecycleCoreAsync(ChangeActivityVersionLifecycleRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("A lifecycle change reason is required.", nameof(request));
        EnsureTenant(request.TenantId);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await ByReference(Access(db.ActivityDefinitionVersionPublications), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), request.DefinitionVersionId).SingleOrDefaultAsync(x => x.TenantId == request.TenantId, cancellationToken) ?? throw new InvalidOperationException("Activity publication was not found.");
        if (row.Lifecycle != request.ExpectedLifecycle) throw new DbUpdateConcurrencyException("Activity publication lifecycle is stale.");
        if (!IsAllowedTransition(row.Lifecycle, request.Lifecycle)) throw new InvalidOperationException($"Activity version lifecycle cannot transition from {row.Lifecycle} to {request.Lifecycle}.");
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), row.DefinitionId).SingleOrDefaultAsync(cancellationToken);
        if (authoring is null || authoring.TenantId != row.TenantId) throw new InvalidOperationException("Activity publication authoring ownership is missing or inconsistent.");
        var recommendationChanged = StringComparer.Ordinal.Equals(authoring.RecommendedVersionId, row.DefinitionVersionId) && request.Lifecycle is ActivityDefinitionVersionLifecycle.Retired or ActivityDefinitionVersionLifecycle.Revoked;
        if (recommendationChanged)
        {
            var decision = request.RecommendationDecision ?? throw new InvalidOperationException("Retiring or revoking the recommended activity version requires an explicit recommendation decision.");
            if (decision.ExpectedDefinitionHeadVersionId != authoring.HeadVersionId || decision.ExpectedRecommendedVersionId != authoring.RecommendedVersionId) throw new DbUpdateConcurrencyException("Activity recommendation or definition head is stale.");
            if (decision.Disposition == ActivityRecommendationDisposition.Clear)
            {
                if (decision.ReplacementVersionId is not null || decision.ExpectedReplacementLifecycle is not null) throw new ArgumentException("A clear recommendation decision cannot include a replacement.", nameof(request));
                authoring.RecommendedVersionId = null;
            }
            else
            {
                if (decision.ReplacementVersionId is null || decision.ExpectedReplacementLifecycle is null) throw new ArgumentException("A replacement recommendation decision requires a version and lifecycle.", nameof(request));
                var replacement = await ByReference(Access(db.ActivityDefinitionVersionPublications), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), decision.ReplacementVersionId).SingleOrDefaultAsync(cancellationToken);
                if (replacement is null || replacement.DefinitionId != row.DefinitionId || replacement.TenantId != row.TenantId) throw new InvalidOperationException("The replacement recommendation is outside the definition tenant.");
                if (replacement.Lifecycle != decision.ExpectedReplacementLifecycle || replacement.Lifecycle != ActivityDefinitionVersionLifecycle.Active) throw new DbUpdateConcurrencyException("The replacement recommendation lifecycle is stale or inactive.");
                authoring.RecommendedVersionId = replacement.DefinitionVersionId;
            }
            authoring.LastModifiedAt = DateTimeOffset.UtcNow;
        }
        else if (request.RecommendationDecision is not null) throw new ArgumentException("A recommendation decision is valid only for the recommended version.", nameof(request));
        row.Lifecycle = request.Lifecycle;
        row.LastModifiedAt = DateTimeOffset.UtcNow;
        if (projections is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(row.LastModifiedAt, [new EfActivityManagementDefinitionChange(await ById(Access(db.ActivityDefinitions), row.DefinitionId).SingleAsync(x => x.TenantId == row.TenantId, cancellationToken), authoring)], [], [row]), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return row;
    }

    public async Task<ActivityDefinitionAuthoringState> ExecuteAsync(SetActivityDefinitionRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        try { return await SetRecommendationCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityDefinitionAuthoringState> SetRecommendationCoreAsync(SetActivityDefinitionRecommendationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ChangedAt == default) throw new ArgumentException("ChangedAt is required.", nameof(request));
        EnsureTenant(request.TenantId);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), request.DefinitionId).SingleOrDefaultAsync(x => x.TenantId == request.TenantId, cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        if (row.HeadVersionId != request.ExpectedDefinitionHeadVersionId) throw new DbUpdateConcurrencyException("Activity definition head is stale.");
        if (row.RecommendedVersionId != request.ExpectedRecommendedVersionId) throw new DbUpdateConcurrencyException("Activity recommendation is stale.");
        if (request.ExpectedRecommendedVersionLifecycle is { } expected && row.RecommendedVersionId is { } recommended)
        {
            var publication = await ByReference(Access(db.ActivityDefinitionVersionPublications), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), recommended).SingleOrDefaultAsync(cancellationToken);
            if (publication is null || publication.Lifecycle != expected) throw new DbUpdateConcurrencyException("Activity recommendation lifecycle is stale.");
        }
        if (request.RecommendedVersionId is null)
        {
            if (request.ExpectedRecommendedVersionLifecycle is not null) throw new ArgumentException("A cleared recommendation cannot declare a target lifecycle.", nameof(request));
        }
        else
        {
            if (request.ExpectedRecommendedVersionLifecycle is null) throw new ArgumentException("A recommendation target requires an expected lifecycle.", nameof(request));
            var target = await ByReference(Access(db.ActivityDefinitionVersionPublications), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), request.RecommendedVersionId).SingleOrDefaultAsync(cancellationToken);
            if (target is null || target.DefinitionId != request.DefinitionId || target.TenantId != row.TenantId) throw new InvalidOperationException("The recommendation target is outside the definition tenant.");
            if (target.Lifecycle != request.ExpectedRecommendedVersionLifecycle || target.Lifecycle != ActivityDefinitionVersionLifecycle.Active) throw new DbUpdateConcurrencyException("The recommendation target lifecycle is stale or inactive.");
        }
        row.RecommendedVersionId = request.RecommendedVersionId;
        row.LastModifiedAt = request.ChangedAt;
        var definition = await ById(Access(db.ActivityDefinitions), row.DefinitionId).SingleOrDefaultAsync(x => x.TenantId == row.TenantId, cancellationToken);
        if (projections is not null && definition is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [new EfActivityManagementDefinitionChange(definition, row)], [], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return row;
    }

    public async Task<ActivityForkCandidate> ExecuteAsync(SaveActivityForkCandidateRequest request, CancellationToken cancellationToken = default)
    {
        try { return await SaveForkCandidateCoreAsync(request, cancellationToken); }
        catch (DesignPersistenceException exception) when (exception.InnerException is DbUpdateException providerFailure && EfRelationalExceptionClassifier.IsUniqueConstraintViolation(providerFailure))
        {
            db.ChangeTracker.Clear();
            return await ReconcileForkCandidateSaveAsync(request, exception, cancellationToken);
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityForkCandidate> ReconcileForkCandidateSaveAsync(SaveActivityForkCandidateRequest request, DesignPersistenceException original, CancellationToken cancellationToken)
    {
        var winner = await ById(Access(db.ActivityForkCandidates.AsNoTracking()), request.Candidate.Id).SingleOrDefaultAsync(cancellationToken);
        if (winner is null)
            throw original;

        ValidateForkIdentity(winner);
        ValidateForkMaterial(winner);
        if (!ForkCandidateMaterialMatches(winner, request.Candidate))
            throw new ActivityForkPreviewIdempotencyConflictException("The preview operation identity is already bound to different material.");
        return winner;
    }

    private async Task<ActivityForkCandidate> SaveForkCandidateCoreAsync(SaveActivityForkCandidateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureTenant(request.Candidate.TenantId);
        var existing = await ById(Access(db.ActivityForkCandidates), request.Candidate.Id).SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            ValidateForkIdentity(existing);
            ValidateForkMaterial(existing);
            if (existing.ExpiresAt <= request.Candidate.CreatedAt) throw new ActivityForkPreviewExpiredException("The reviewed fork reservation expired.");
            if (existing.PreviewIdempotencyKey != request.Candidate.PreviewIdempotencyKey || existing.RequestFingerprint != request.Candidate.RequestFingerprint || existing.AccessBindingFingerprint != request.Candidate.AccessBindingFingerprint || existing.ActorId != request.Candidate.ActorId || existing.AuthorizationProfile != request.Candidate.AuthorizationProfile)
                throw new ActivityForkPreviewIdempotencyConflictException("The preview operation identity is already bound to different material.");
            return existing;
        }
        if (request.Candidate.ExpiresAt <= request.Candidate.CreatedAt || request.Candidate.RetainUntil <= request.Candidate.ExpiresAt || request.Candidate.RetentionKey != ActivityForkCandidateIdentity.RetentionKey(request.Candidate.RetainUntil))
            throw new ArgumentException("Fork candidate expiry and retention must be strictly ordered.", nameof(request));
        ValidateForkMaterial(request.Candidate);
        db.ActivityForkCandidates.Add(request.Candidate);
        await SaveAsync(cancellationToken);
        ValidateForkIdentity(request.Candidate);
        return request.Candidate;
    }

    public async Task<int> ExecuteAsync(DateTimeOffset retainBefore, int maximumCount = 100, CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var rows = await WithPhysicalIdentityTie(Access(db.ActivityForkCandidates).Where(x => x.RetainUntil <= retainBefore).OrderBy(x => x.RetainUntil).ThenBy(x => x.Id)).Take(maximumCount).ToListAsync(cancellationToken);
        db.ActivityForkCandidates.RemoveRange(rows);
        await SaveAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<ActivityForkApplyResult> ExecuteAsync(ApplyActivityForkCandidateRequest request, CancellationToken cancellationToken = default)
    {
        try { return await ApplyForkCandidateCoreAsync(request, cancellationToken); }
        catch (DesignPersistenceException exception) when (exception.InnerException is DbUpdateException providerFailure && EfRelationalExceptionClassifier.IsUniqueConstraintViolation(providerFailure))
        {
            // A concurrent apply may have committed after this transaction read the candidate.
            // Re-read the durable receipt only after the failed transaction is gone; if no exact
            // winner exists, this was a real definition/other uniqueness collision.
            db.ChangeTracker.Clear();
            return await ReconcileForkApplyAsync(request, cancellationToken);
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityForkApplyResult> ReconcileForkApplyAsync(ApplyActivityForkCandidateRequest request, CancellationToken cancellationToken)
    {
        var winner = await ById(Access(db.ActivityForkReceipts.AsNoTracking()), request.ReceiptId).SingleOrDefaultAsync(cancellationToken);
        if (winner is null)
            throw new ActivityForkCollisionException("The activity fork candidate collided with a concurrent write before a matching receipt was committed.");

        ValidateReceiptIdentity(winner, request);
        return new(MapReceipt(winner), true);
    }

    private async Task<ActivityForkApplyResult> ApplyForkCandidateCoreAsync(ApplyActivityForkCandidateRequest request, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var existing = await ById(Access(db.ActivityForkReceipts.AsNoTracking()), request.ReceiptId).SingleOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            var candidateReceipt = await ById(Access(db.ActivityForkReceipts.AsNoTracking()), request.ReceiptId).SingleAsync(cancellationToken);
            ValidateReceiptIdentity(candidateReceipt, request);
            await transaction.RollbackAsync(cancellationToken);
            return new(MapReceipt(candidateReceipt), true);
        }
        var candidate = await ById(Access(db.ActivityForkCandidates), request.CandidateId).SingleOrDefaultAsync(cancellationToken);
        if (candidate is null)
        {
            var candidateHash = ActivityForkIdentityMaterial.ExactHash(request.CandidateId);
            var actorHash = ActivityForkIdentityMaterial.ExactHash(request.ActorId);
            var candidates = await Access(db.ActivityForkCandidates)
                .Where(x => x.ActorIdentityHash == actorHash && x.ActorId == request.ActorId &&
                            x.CandidateIdIdentityHash == candidateHash && x.CandidateId == request.CandidateId)
                .Take(2).ToListAsync(cancellationToken);
            foreach (var candidateMatch in candidates)
                ValidateForkIdentity(candidateMatch);
            candidate = candidates.SingleOrDefault();
        }
        if (candidate is null)
            throw new ActivityForkCandidateStaleException("The activity fork candidate no longer exists.");
        ValidateForkIdentity(candidate);
        EnsureTenant(candidate.TenantId);
        if (candidate.Status != ActivityForkCandidateStatus.Reserved) throw new ActivityForkCandidateStaleException("The activity fork candidate has already been consumed.");
        if (candidate.ExpiresAt <= request.AppliedAt) throw new ActivityForkPreviewExpiredException("The reviewed fork reservation expired.");
        if (candidate.RequestFingerprint != request.RequestFingerprint || candidate.AccessBindingFingerprint != request.AccessBindingFingerprint || candidate.ActorId != request.ActorId || candidate.AuthorizationProfile != request.AuthorizationProfile) throw new ActivityForkCandidateStaleException("The activity fork candidate binding changed before commit.");
        ValidateForkMaterial(candidate);
        if (candidate.ReservedDraft.SourceVersionId != candidate.SourceVersionId)
            throw new ActivityForkCandidateStaleException("The fork candidate draft is not bound to the reviewed source version.");
        var sourceAuthoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates.AsNoTracking()), nameof(ActivityDefinitionAuthoringState.DefinitionId), candidate.SourceDefinitionId)
            .SingleOrDefaultAsync(x => x.TenantId == candidate.TenantId, cancellationToken)
            ?? throw new ActivityForkCandidateStaleException("The fork source authoring state no longer exists.");
        if (sourceAuthoring.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource)
            throw new ActivityForkCandidateStaleException("The fork source is no longer owned by its provider source.");
        var source = await ByReference(Access(db.ActivityDefinitionVersionPublications.AsNoTracking()), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), candidate.SourceVersionId).SingleOrDefaultAsync(x => x.TenantId == candidate.TenantId, cancellationToken)
            ?? throw new ActivityForkCandidateStaleException("The fork source version no longer exists.");
        if (source.DefinitionId != candidate.SourceDefinitionId || source.Version != candidate.SourceVersion || source.Lifecycle != candidate.SourceLifecycle ||
            !StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(source.Provider), candidate.SourceProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(ActivityForkMaterialFingerprint.Compute(source.Contract), candidate.SourceContractFingerprint))
            throw new ActivityForkCandidateStaleException("The exact fork source changed before the atomic commit.");
        if (!StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(candidate.ReservedDraft.State.Provider), candidate.TargetProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(ActivityForkMaterialFingerprint.Compute(candidate.ReservedDraft.State.Contract), candidate.TargetContractFingerprint))
            throw new ActivityForkCandidateStaleException("The reviewed fork target material was tampered with.");
        var targetScopeKey = NormalizeTenantKey(candidate.TenantId);
        var typeKeyMatches = await db.ActivityDefinitions.AsNoTracking()
            .Where(x => EF.Property<string>(x, "TenantScopeKey") == targetScopeKey &&
                        x.ActivityTypeKey == candidate.ReservedDefinition.ActivityTypeKey)
            .Select(x => new
            {
                x.TenantId,
                PhysicalTenantKey = EF.Property<string>(x, "TenantKey")
            })
            .Take(2)
            .ToArrayAsync(cancellationToken);
        if (typeKeyMatches.Any(x => !StringComparer.Ordinal.Equals(x.TenantId, candidate.TenantId) ||
                                    !StringComparer.Ordinal.Equals(x.PhysicalTenantKey, targetScopeKey)))
            throw new InvalidDataException("The fork target activity type key has corrupt tenant ownership.");
        if (typeKeyMatches.Length != 0)
            throw new ActivityForkCollisionException("The reserved activity type key is no longer available.");
        db.ActivityDefinitions.Add(candidate.ReservedDefinition);
        db.ActivityDefinitionAuthoringStates.Add(candidate.ReservedAuthoringState);
        db.ActivityDefinitionDrafts.Add(candidate.ReservedDraft);
        db.ActivityDefinitionDraftLayouts.Add(candidate.ReservedLayout);
        candidate.Status = ActivityForkCandidateStatus.Applied;
        candidate.AppliedIdempotencyKey = request.IdempotencyKey;
        candidate.LastModifiedAt = request.AppliedAt;
        var receipt = new ActivityForkReceipt { Id = request.ReceiptId, TenantId = candidate.TenantId, TenantScopeKey = ActivitiesDesignDbContext.NormalizeTenantKey(candidate.TenantId), ActorIdentityHash = ActivityForkIdentityMaterial.ExactHash(request.ActorId), IdempotencyIdentityHash = ActivityForkIdentityMaterial.ExactHash(request.IdempotencyKey), IdempotencyKey = request.IdempotencyKey, CandidateId = candidate.Id, PublicCandidateId = candidate.CandidateId, RequestFingerprint = candidate.RequestFingerprint, AccessBindingFingerprint = candidate.AccessBindingFingerprint, ActorId = candidate.ActorId, AuthorizationProfile = candidate.AuthorizationProfile, DefinitionId = candidate.ReservedDefinition.Id, ActivityTypeKey = candidate.ReservedDefinition.ActivityTypeKey, DraftId = candidate.ReservedDraft.Id, Definition = candidate.ReservedDefinition, DefinitionMaterialJson = SerializeJson(candidate.ReservedDefinition, nameof(ActivityForkReceipt)), AuthoringState = candidate.ReservedAuthoringState, Draft = candidate.ReservedDraft, Layout = candidate.ReservedLayout, MigrationDiagnostics = candidate.MigrationDiagnostics, AppliedAt = request.AppliedAt, CreatedAt = request.AppliedAt, LastModifiedAt = request.AppliedAt };
        db.ActivityForkReceipts.Add(receipt);
        if (projections is not null)
            await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(
                request.AppliedAt,
                [new EfActivityManagementDefinitionChange(candidate.ReservedDefinition, candidate.ReservedAuthoringState)],
                [candidate.ReservedDraft], []), cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(receipt, false);
    }

    private static ActivityForkReceipt MapReceipt(ActivityForkReceipt receipt)
    {
        ValidateReceiptScope(receipt);
        if (receipt.Definition is null && !string.IsNullOrWhiteSpace(receipt.DefinitionMaterialJson))
            try { receipt.Definition = JsonSerializer.Deserialize<ActivityDefinition>(receipt.DefinitionMaterialJson, Json) ?? throw new InvalidDataException("Fork receipt definition material is unreadable."); }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or InvalidDataException)
            { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "load", nameof(ActivityForkReceipt), exception); }
        return receipt;
    }

    private static void ValidateReceiptIdentity(ActivityForkReceipt receipt, ApplyActivityForkCandidateRequest request)
    {
        ValidateReceiptScope(receipt);
        if (!StringComparer.Ordinal.Equals(receipt.IdempotencyKey, request.IdempotencyKey) || !StringComparer.Ordinal.Equals(receipt.CandidateId, request.CandidateId) || !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, request.RequestFingerprint) || !StringComparer.Ordinal.Equals(receipt.AccessBindingFingerprint, request.AccessBindingFingerprint) || !StringComparer.Ordinal.Equals(receipt.ActorId, request.ActorId) || !StringComparer.Ordinal.Equals(receipt.AuthorizationProfile, request.AuthorizationProfile))
            throw new ActivityForkIdempotencyConflictException("The activity fork operation identity is already bound to different material.");
    }

    private static void ValidateReceiptScope(ActivityForkReceipt receipt)
    {
        if (!StringComparer.Ordinal.Equals(receipt.TenantScopeKey, ActivitiesDesignDbContext.NormalizeTenantKey(receipt.TenantId)))
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "load", nameof(ActivityForkReceipt), new InvalidDataException("The fork receipt scope key does not match its raw tenant scope."));
        if (!StringComparer.Ordinal.Equals(receipt.ActorIdentityHash, ActivityForkIdentityMaterial.ExactHash(receipt.ActorId)) || !StringComparer.Ordinal.Equals(receipt.IdempotencyIdentityHash, ActivityForkIdentityMaterial.ExactHash(receipt.IdempotencyKey)))
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "load", nameof(ActivityForkReceipt), new InvalidDataException("The fork receipt identity hashes do not match their raw values."));
    }

    private static void ValidateForkMaterial(ActivityForkCandidate candidate)
    {
        var definition = candidate.ReservedDefinition;
        var authoring = candidate.ReservedAuthoringState;
        var draft = candidate.ReservedDraft;
        var layout = candidate.ReservedLayout;
        if (definition is null || authoring is null || draft is null || layout is null ||
            definition.TenantId != candidate.TenantId || authoring.TenantId != candidate.TenantId || draft.TenantId != candidate.TenantId || layout.TenantId != candidate.TenantId ||
            authoring.DefinitionId != definition.Id || draft.DefinitionId != definition.Id || layout.DraftId != draft.Id ||
            draft.Status != ActivityDefinitionDraftStatus.Active || layout.Revision != draft.Revision ||
            string.IsNullOrWhiteSpace(candidate.SourceDefinitionId) || string.IsNullOrWhiteSpace(candidate.SourceVersionId) ||
            string.IsNullOrWhiteSpace(candidate.SourceProviderFingerprint) || string.IsNullOrWhiteSpace(candidate.TargetProviderFingerprint) ||
            string.IsNullOrWhiteSpace(candidate.SourceContractFingerprint) || string.IsNullOrWhiteSpace(candidate.TargetContractFingerprint))
            throw new ActivityForkCandidateStaleException("The fork candidate contains inconsistent or incomplete reviewed material.");
        if (!StringComparer.Ordinal.Equals(draft.SourceVersionId, candidate.SourceVersionId))
            throw new ActivityForkCandidateStaleException("The fork candidate draft is not bound to the reviewed source version.");
    }

    private static void ValidateForkIdentity(ActivityForkCandidate candidate)
    {
        if (candidate.ActorId is null || candidate.CandidateId is null ||
            !StringComparer.Ordinal.Equals(candidate.TenantScopeKey, ActivitiesDesignDbContext.NormalizeTenantKey(candidate.TenantId)) ||
            !StringComparer.Ordinal.Equals(candidate.ActorIdentityHash, ActivityForkIdentityMaterial.ExactHash(candidate.ActorId)) ||
            !StringComparer.Ordinal.Equals(candidate.CandidateIdIdentityHash, ActivityForkIdentityMaterial.ExactHash(candidate.CandidateId)))
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "load", nameof(ActivityForkCandidate), new InvalidDataException("The fork candidate identity columns do not match their raw values."));
    }

    private static bool ForkCandidateMaterialMatches(ActivityForkCandidate left, ActivityForkCandidate right)
    {
        if (!StringComparer.Ordinal.Equals(left.TenantId, right.TenantId) ||
            !StringComparer.Ordinal.Equals(left.Id, right.Id) ||
            !StringComparer.Ordinal.Equals(left.PreviewIdempotencyKey, right.PreviewIdempotencyKey) ||
            !StringComparer.Ordinal.Equals(left.RequestFingerprint, right.RequestFingerprint) ||
            !StringComparer.Ordinal.Equals(left.AccessBindingFingerprint, right.AccessBindingFingerprint) ||
            !StringComparer.Ordinal.Equals(left.ActorId, right.ActorId) ||
            !StringComparer.Ordinal.Equals(left.AuthorizationProfile, right.AuthorizationProfile) ||
            !StringComparer.Ordinal.Equals(left.SourceDefinitionId, right.SourceDefinitionId) ||
            !StringComparer.Ordinal.Equals(left.SourceVersionId, right.SourceVersionId) ||
            !StringComparer.Ordinal.Equals(left.SourceVersion, right.SourceVersion) ||
            left.SourceLifecycle != right.SourceLifecycle ||
            !StringComparer.Ordinal.Equals(left.SourceProviderFingerprint, right.SourceProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(left.TargetProviderFingerprint, right.TargetProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(left.SourceContractFingerprint, right.SourceContractFingerprint) ||
            !StringComparer.Ordinal.Equals(left.TargetContractFingerprint, right.TargetContractFingerprint))
            return false;
        return true;
    }

    public async Task ExecuteAsync(CreateActivityDraftConflictCopyRequest request, CancellationToken cancellationToken = default)
    {
        try { await CreateConflictCopyCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task CreateConflictCopyCoreAsync(CreateActivityDraftConflictCopyRequest request, CancellationToken cancellationToken = default)
    {
        EnsureTenant(request.ConflictCopy.TenantId);
        if (request.ConflictCopy.Id != request.Layout.DraftId || request.ConflictCopy.TenantId != request.Layout.TenantId || request.ConflictCopy.Revision != request.Layout.Revision || request.ConflictCopy.Status != ActivityDefinitionDraftStatus.Active)
            throw new ArgumentException("Conflict copy draft and layout identities, tenants, revisions, and status must match.", nameof(request));
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        // Keep the source draft tracked so its shadow token can be advanced by a conditional EF
        // update. This makes the revision check linearizable against a concurrent source-draft
        // update, rather than merely a preflight read.
        var source = await ById(Access(db.ActivityDefinitionDrafts), request.SourceDraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("The source draft was not found.");
        if (source.Status != ActivityDefinitionDraftStatus.Active || source.Revision != request.ExpectedSourceRevision) throw new DbUpdateConcurrencyException("The source draft revision is stale or inactive.");
        await AdvanceConcurrencyFenceAsync(db.ActivityDefinitionDrafts, source, "Activity draft revision is stale or inactive.", cancellationToken);
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), source.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, source.DefinitionId, source.TenantId);
        if (authoring.DefinitionId != request.ConflictCopy.DefinitionId || authoring.TenantId != request.ConflictCopy.TenantId || source.SourceVersionId != request.ConflictCopy.SourceVersionId)
            throw new ArgumentException("The conflict copy must preserve source draft definition, tenant, and lineage.", nameof(request));
        if (await ById(Access(db.ActivityDefinitionDrafts), request.ConflictCopy.Id).AnyAsync(cancellationToken) || await ByReference(Access(db.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), request.ConflictCopy.Id).AnyAsync(cancellationToken))
            throw new InvalidOperationException("The conflict copy identity is already in use.");
        db.ActivityDefinitionDrafts.Add(request.ConflictCopy);
        db.ActivityDefinitionDraftLayouts.Add(request.Layout);
        if (projections is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [], [request.ConflictCopy], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(ReplaceActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        try { return await ReplaceDraftCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityDefinitionDraft> ReplaceDraftCoreAsync(ReplaceActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var draft = await ById(Access(db.ActivityDefinitionDrafts), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft was not found.");
        EnsureTenant(draft.TenantId);
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), draft.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, draft.DefinitionId, draft.TenantId);
        if (draft.Status != ActivityDefinitionDraftStatus.Active || draft.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Activity draft revision is stale.");
        var layout = await ByReference(Access(db.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft layout was not found.");
        if (layout.Revision != draft.Revision) throw new DbUpdateConcurrencyException("Activity draft and layout revisions are inconsistent.");
        draft.Revision = checked(draft.Revision + 1);
        draft.State = request.State;
        draft.PresentationLabel = request.PresentationLabel;
        draft.LastModifiedAt = DateTimeOffset.UtcNow;
        layout.Revision = draft.Revision;
        layout.Records = request.Layout.ToList();
        layout.LastModifiedAt = draft.LastModifiedAt;
        if (projections is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(draft.LastModifiedAt, [], [draft], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return draft;
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(ApplyActivityContractProposalRequest request, CancellationToken cancellationToken = default)
    {
        try { return await ApplyContractCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task<ActivityDefinitionDraft> ApplyContractCoreAsync(ApplyActivityContractProposalRequest request, CancellationToken cancellationToken = default)
    {
        EnsureTenant(request.TenantId);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var draft = await ById(Access(db.ActivityDefinitionDrafts), request.DraftId).SingleOrDefaultAsync(x => x.TenantId == request.TenantId, cancellationToken) ?? throw new InvalidOperationException("Activity draft was not found.");
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), draft.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, draft.DefinitionId, draft.TenantId);
        if (draft.Status != ActivityDefinitionDraftStatus.Active || draft.Revision != request.ExpectedRevision || draft.State.Provider.ProviderKey != request.ExpectedProviderKey || draft.State.Provider.SchemaVersion != request.ExpectedProviderSchemaVersion || !StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(draft.State.Provider), request.ExpectedManifestFingerprint))
            throw new DbUpdateConcurrencyException("Activity contract proposal is stale.");
        var layout = await ByReference(Access(db.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft layout was not found.");
        if (layout.Revision != draft.Revision) throw new DbUpdateConcurrencyException("Activity draft and layout revisions are inconsistent.");
        draft.Revision = checked(draft.Revision + 1); draft.State = draft.State with { Contract = request.Contract }; draft.LastModifiedAt = DateTimeOffset.UtcNow; layout.Revision = draft.Revision; layout.LastModifiedAt = draft.LastModifiedAt;
        if (projections is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(draft.LastModifiedAt, [], [draft], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken); return draft;
    }

    public async Task ExecuteAsync(DiscardActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        try { await DiscardDraftCoreAsync(request, cancellationToken); }
        catch { db.ChangeTracker.Clear(); throw; }
    }

    private async Task DiscardDraftCoreAsync(DiscardActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var draft = await ById(Access(db.ActivityDefinitionDrafts), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft was not found.");
        EnsureTenant(draft.TenantId);
        var authoring = await ByReference(Access(db.ActivityDefinitionAuthoringStates), nameof(ActivityDefinitionAuthoringState.DefinitionId), draft.DefinitionId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity authoring state was not found.");
        EnsureDesignAuthoring(authoring, draft.DefinitionId, draft.TenantId);
        if (draft.Status != ActivityDefinitionDraftStatus.Active || draft.Revision != request.ExpectedRevision) throw new DbUpdateConcurrencyException("Activity draft is stale or inactive.");
        var layout = await ByReference(Access(db.ActivityDefinitionDraftLayouts), nameof(ActivityDefinitionDraftLayout.DraftId), request.DraftId).SingleOrDefaultAsync(cancellationToken) ?? throw new InvalidOperationException("Activity draft layout was not found.");
        if (layout.Revision != draft.Revision) throw new DbUpdateConcurrencyException("Activity draft and layout revisions are inconsistent.");
        draft.Revision = checked(draft.Revision + 1);
        draft.Status = ActivityDefinitionDraftStatus.Discarded;
        draft.LastModifiedAt = DateTimeOffset.UtcNow;
        layout.Revision = draft.Revision;
        layout.LastModifiedAt = draft.LastModifiedAt;
        if (projections is not null) await projections.WriteInCurrentTransactionAsync(new EfActivityManagementProjectionMutation(draft.LastModifiedAt, [], [draft], []), cancellationToken);
        else await SaveAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<RecommendedActivityDefinitionPickerPage> ReadAsync(string? tenantId, int offset, int limit, CancellationToken cancellationToken = default)
    {
        EnsureReadTenant(tenantId);
        ValidatePage(offset, limit);
        var states = Access(db.ActivityDefinitionAuthoringStates.AsNoTracking()).Where(x => x.RecommendedVersionId != null && (x.TenantId == null || tenantId != null && x.TenantId == tenantId));
        var total = await states.CountAsync(cancellationToken);
        var rows = await WithPhysicalIdentityTie(states.OrderBy(x => x.DefinitionId)).Skip(offset).Take(limit).ToListAsync(cancellationToken);
        var definitionIds = rows.Select(x => x.DefinitionId).Distinct(StringComparer.Ordinal).ToArray();
        var recommendedVersionIds = rows.Where(x => x.RecommendedVersionId is not null).Select(x => x.RecommendedVersionId!).Distinct(StringComparer.Ordinal).ToArray();
        var definitions = await ReadAllPagesAsync(
            ByIds(Access(db.ActivityDefinitions.AsNoTracking()), definitionIds),
            query => WithPhysicalIdentityTie(query.OrderBy(x => x.TenantId).ThenBy(x => x.Id)), cancellationToken);
        var publications = await ReadAllPagesAsync(
            ByReferences(Access(db.ActivityDefinitionVersionPublications.AsNoTracking()), nameof(ActivityDefinitionVersionPublication.DefinitionVersionId), recommendedVersionIds),
            query => WithPhysicalIdentityTie(query.OrderBy(x => x.TenantId).ThenBy(x => x.DefinitionVersionId)), cancellationToken);
        var definitionsById = definitions.ToDictionary(x => (NormalizeTenantKey(x.TenantId), x.Id));
        var publicationsByVersionId = publications.ToDictionary(x => (NormalizeTenantKey(x.TenantId), x.DefinitionVersionId));
        var items = new List<RecommendedActivityDefinitionPickerItem>();
        foreach (var state in rows.Where(x => x.RecommendedVersionId is not null))
            if (definitionsById.TryGetValue((NormalizeTenantKey(state.TenantId), state.DefinitionId), out var definition) &&
                publicationsByVersionId.TryGetValue((NormalizeTenantKey(state.TenantId), state.RecommendedVersionId!), out var version))
                items.Add(new(definition, version));
        return new(items, offset + rows.Count < total ? offset + rows.Count : null);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (OperationCanceledException) { db.ChangeTracker.Clear(); throw; }
        catch (DbUpdateConcurrencyException exception) { db.ChangeTracker.Clear(); throw new DbUpdateConcurrencyException(exception.Message, exception); }
        catch (DbUpdateException exception) when (EfRelationalExceptionClassifier.IsUniqueConstraintViolation(exception)) { db.ChangeTracker.Clear(); throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "save-unique", null, exception); }
        catch (DbUpdateException exception) { db.ChangeTracker.Clear(); throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Provider, "save", null, exception); }
    }

    private static async Task AdvanceConcurrencyFenceAsync<TEntity>(DbSet<TEntity> set, TEntity entity, string message, CancellationToken cancellationToken)
        where TEntity : Elsa.Primitives.Entities.TenantEntity
    {
        var concurrencyProperty = set.Entry(entity).Property<byte[]>("ConcurrencyToken");
        var token = concurrencyProperty.OriginalValue;
        var replacement = Guid.NewGuid().ToByteArray();
        var scopeKey = ActivitiesDesignDbContext.NormalizeTenantKey(entity.TenantId);
        var idHash = ActivitiesDesignDbContext.ComputeIdentityHash(entity.Id);
        var candidates = set.Where(x => EF.Property<string>(x, "TenantScopeKey") == scopeKey &&
                                       EF.Property<string>(x, "IdIdentityHash") == idHash &&
                                       EF.Property<string>(x, "Id") == entity.Id);
        candidates = token is null
            ? candidates.Where(x => EF.Property<byte[]>(x, "ConcurrencyToken") == null)
            : candidates.Where(x => EF.Property<byte[]>(x, "ConcurrencyToken") == token);
        var affected = await candidates
            .ExecuteUpdateAsync(updates => updates.SetProperty(x => EF.Property<byte[]>(x, "ConcurrencyToken"), replacement), cancellationToken);
        if (affected != 1)
            throw new DbUpdateConcurrencyException(message);
        concurrencyProperty.CurrentValue = replacement;
        concurrencyProperty.OriginalValue = replacement;
    }

    internal static IQueryable<T> ById<T>(IQueryable<T> query, string id)
        where T : Elsa.Primitives.Entities.Entity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var hash = ActivitiesDesignDbContext.ComputeIdentityHash(id);
        return query.Where(x => EF.Property<string>(x, "IdIdentityHash") == hash && EF.Property<string>(x, "Id") == id);
    }

    private static string NormalizeTenantKey(string? tenantId) => ActivitiesDesignDbContext.NormalizeTenantKey(tenantId);

    // A provider collation may consider distinct raw IDs equal. The physical hash-backed key
    // finishes every paged order so offset and keyset boundaries remain total on all providers.
    private static IOrderedQueryable<T> WithPhysicalIdentityTie<T>(IOrderedQueryable<T> ordered) where T : class =>
        ordered.ThenBy(x => EF.Property<string>(x, "TenantScopeKey"))
            .ThenBy(x => EF.Property<string>(x, "IdIdentityHash"));

    private static IOrderedQueryable<T> WithResourceIdentityTie<T>(IOrderedQueryable<T> ordered)
        where T : ActivityManagementProjectionRevision =>
        ordered.ThenBy(x => EF.Property<string>(x, "TenantScopeKey"))
            .ThenBy(x => EF.Property<string>(x, "ResourceIdIdentityHash"))
            .ThenBy(x => EF.Property<string>(x, "IdIdentityHash"));

    internal static IQueryable<T> ByReference<T>(IQueryable<T> query, string propertyName, string value)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var hash = ActivitiesDesignDbContext.ComputeIdentityHash(value);
        return query.Where(x => EF.Property<string>(x, propertyName + "IdentityHash") == hash && EF.Property<string>(x, propertyName) == value);
    }

    private static IQueryable<T> ByIds<T>(IQueryable<T> query, IEnumerable<string> values)
        where T : Elsa.Primitives.Entities.Entity
    {
        var ids = values.Distinct(StringComparer.Ordinal).ToArray();
        var hashes = ids.Select(ActivitiesDesignDbContext.ComputeIdentityHash).ToArray();
        return query.Where(x => hashes.Contains(EF.Property<string>(x, "IdIdentityHash")) && ids.Contains(EF.Property<string>(x, "Id")));
    }

    private static IQueryable<T> ByReferences<T>(IQueryable<T> query, string propertyName, IReadOnlyCollection<string> values)
        where T : class
    {
        var hashes = values.Select(ActivitiesDesignDbContext.ComputeIdentityHash).Distinct(StringComparer.Ordinal).ToArray();
        return query.Where(x => hashes.Contains(EF.Property<string>(x, propertyName + "IdentityHash")) &&
                                values.Contains(EF.Property<string>(x, propertyName)));
    }

    private static IQueryable<ActivityAvailabilitySettingsRecord> ByScope(IQueryable<ActivityAvailabilitySettingsRecord> query, string partition, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        var hash = ActivitiesDesignDbContext.ComputeIdentityHash(scope);
        return query.Where(x => EF.Property<string>(x, "TenantScopeKey") == partition && EF.Property<string>(x, "ScopeIdentityHash") == hash && x.Scope == scope);
    }

    /// <summary>
    /// The persistence scope whose availability settings this request reads and writes. An across-scopes
    /// context has no single partition, so it is refused rather than silently mapped to one.
    /// </summary>
    private string AvailabilityPartitionKey()
    {
        var context = access?.Current;
        if (context?.AcrossScopes == true)
            throw new InvalidOperationException("Activity availability settings are partitioned by persistence scope; an across-scopes context has no single partition.");
        return ActivitiesDesignDbContext.NormalizeTenantKey(context?.Scope?.Value);
    }

    private static ActivityDefinitionVersion HydrateVersion(ActivityDefinitionVersion version)
    {
        try
        {
            version.DescriptorPayload = string.IsNullOrWhiteSpace(version.DescriptorPayloadSource) ? default : JsonDocument.Parse(version.DescriptorPayloadSource).RootElement.Clone();
            version.Inputs = DeserializeMany<InputDefinition>(version.InputsSource);
            version.Outputs = DeserializeMany<OutputDefinition>(version.OutputsSource);
            version.DesignFacets = DeserializeMany<ActivityDesignFacet>(version.DesignFacetsSource);
            return version;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "load", nameof(ActivityDefinitionVersion), exception);
        }
    }

    internal static void PrepareVersion(ActivityDefinitionVersion version)
    {
        try
        {
            version.DescriptorPayloadSource = version.DescriptorPayload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null ? null : version.DescriptorPayload.GetRawText();
            version.InputsSource = JsonSerializer.Serialize(version.Inputs ?? [], Json);
            version.OutputsSource = JsonSerializer.Serialize(version.Outputs ?? [], Json);
            version.DesignFacetsSource = JsonSerializer.Serialize(version.DesignFacets ?? [], Json);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "save", nameof(ActivityDefinitionVersion), exception);
        }
    }

    private static IReadOnlyList<T> DeserializeMany<T>(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return [];
        return JsonSerializer.Deserialize<IReadOnlyList<T>>(source, Json) ?? [];
    }
    private static string SerializeJson<T>(T value, string entity)
    {
        try { return JsonSerializer.Serialize(value, Json); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "save", entity, exception); }
    }
    private static T DeserializeJson<T>(string source, string entity) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(source, Json) ?? throw new InvalidDataException($"Stored {entity} material is null."); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or InvalidDataException)
        { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "load", entity, exception); }
    }
    private static async Task<T?> ReadJsonAsync<T>(Task<ActivityUpgradePlanRecord?> task) where T : class
    {
        var row = await task;
        if (row is null) return null;
        try { return DeserializeStoredJson<T>(row.PlanJson, nameof(ActivityUpgradePlanRecord)); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or InvalidDataException) { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "load", nameof(ActivityUpgradePlanRecord), exception); }
    }
    private static async Task<T?> ReadJsonAsync<T>(Task<ActivityUpgradeApplyReceiptRecord?> task) where T : class
    {
        var row = await task;
        if (row is null) return null;
        try { return DeserializeStoredJson<T>(row.ReceiptJson, nameof(ActivityUpgradeApplyReceiptRecord)); }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or InvalidDataException) { throw new DesignPersistenceException(DesignPersistenceDomain.Activity, DesignPersistenceFailureKind.Serialization, "load", nameof(ActivityUpgradeApplyReceiptRecord), exception); }
    }
    private static T DeserializeStoredJson<T>(string? source, string entity) where T : class
    {
        if (string.IsNullOrWhiteSpace(source)) throw new InvalidDataException($"Stored {entity} material is empty.");
        return JsonSerializer.Deserialize<T>(source, Json) ?? throw new InvalidDataException($"Stored {entity} material is null.");
    }
    private static void ValidatePage(int offset, int limit) { if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset)); if (limit is < 1 or > MaximumPageSize) throw new ArgumentOutOfRangeException(nameof(limit)); }
    private static async Task<IReadOnlyList<T>> ReadAllPagesAsync<T>(IQueryable<T> query, Func<IQueryable<T>, IOrderedQueryable<T>> order, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        var offset = 0;
        while (true)
        {
            var page = await order(query).Skip(offset).Take(MaximumPageSize).ToListAsync(cancellationToken);
            result.AddRange(page);
            if (page.Count < MaximumPageSize) return result;
            offset = checked(offset + page.Count);
        }
    }
    private static bool IsAllowedTransition(ActivityDefinitionVersionLifecycle current, ActivityDefinitionVersionLifecycle next) =>
        (current, next) switch
        {
            (ActivityDefinitionVersionLifecycle.Active, ActivityDefinitionVersionLifecycle.Retired) => true,
            (ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Active) => true,
            (ActivityDefinitionVersionLifecycle.Active or ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Revoked) => true,
            _ => false
        };
    private async Task<ActivityManagementSnapshot> ResolveSnapshotAsync(long? requested, CancellationToken cancellationToken)
    {
        var watermark = await ById(db.ActivityManagementProjectionWatermarks.AsNoTracking(), ActivityManagementProjectionWatermark.CurrentId).SingleOrDefaultAsync(cancellationToken);
        if (watermark is null)
            return requested is null or 0 ? new ActivityManagementSnapshot(0, DateTimeOffset.UnixEpoch) : throw new ActivityManagementSnapshotExpiredException(requested.Value);
        var sequence = requested ?? watermark.Sequence;
        if (sequence == 0) return new ActivityManagementSnapshot(0, DateTimeOffset.UnixEpoch);
        if (sequence < watermark.RetainedFromSequence || sequence > watermark.Sequence)
            throw new ActivityManagementSnapshotExpiredException(sequence);
        if (sequence == watermark.Sequence) return new ActivityManagementSnapshot(sequence, watermark.AdvancedAt);
        var snapshot = await db.ActivityManagementProjectionSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.Sequence == sequence, cancellationToken);
        return snapshot is null ? throw new ActivityManagementSnapshotExpiredException(sequence) : new(snapshot.Sequence, snapshot.AsOf);
    }

    private static IQueryable<T> ApplyVisibility<T>(IQueryable<T> rows, string? tenantId)
        where T : ActivityManagementProjectionRevision => tenantId is null ? rows.Where(x => x.TenantId == null) : rows.Where(x => x.TenantId == null || x.TenantId == tenantId);
    private static string Fingerprint(ActivityDependencyProjectionState projection) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { projection.RebuildId, projection.Sequence, projection.AsOf, projection.Items }, Json))));

    private static bool IsIncluded(string kind, bool drafts, bool versions) =>
        kind.EndsWith("Draft", StringComparison.Ordinal) ? drafts : versions;

    private static bool HasOwnerIdentity(ActivityDefinitionReference owner) =>
        owner.Kind.EndsWith("Draft", StringComparison.Ordinal)
            ? !string.IsNullOrWhiteSpace(owner.DraftId)
            : !string.IsNullOrWhiteSpace(owner.VersionId);

    private static bool HasValidContentAuthority(ActivityDefinitionManagementProjectionRevision row)
    {
        var authority = row.ContentAuthority;
        if (authority is null || authority.Kind != row.ContentAuthorityKind || !Enum.IsDefined(authority.Kind) || string.IsNullOrWhiteSpace(authority.AuthorityKey))
            return false;
        if (authority.SourceId is not null && string.IsNullOrWhiteSpace(authority.SourceId))
            return false;

        // A source id is meaningful only for provider-owned content. Design-owned rows must not
        // carry provider lineage, while provider keys remain extensible and are not hard-coded.
        if (authority.Kind != ActivityContentAuthorityKind.ProviderSource && authority.SourceId is not null)
            return false;

        if (string.IsNullOrWhiteSpace(row.ContentAuthorityIntegrityHash) ||
            !TryGetAuthorityHashMaterial(row.ContentAuthorityJson, out var raw, out var hash) ||
            !StringComparer.Ordinal.Equals(hash, row.ContentAuthorityIntegrityHash))
            return false;

        var keyToken = JsonSerializer.Serialize(authority.AuthorityKey, Json);
        var sourceToken = authority.SourceId is null ? "null" : JsonSerializer.Serialize(authority.SourceId, Json);
        return StringComparer.Ordinal.Equals(
            row.ContentAuthorityIntegrityHash,
            ActivityAuthorityIntegrity.Compute(raw, raw, keyToken, sourceToken, authority.AuthorityKey, authority.SourceId, authority.Kind));
    }

    internal static string ItemSortKey(ActivityDependencyItem item) =>
        string.Join('\u001f', item.Depth, item.Owner.Kind, item.Owner.DraftId ?? item.Owner.VersionId,
            item.Occurrence.OccurrenceId, item.Dependency.VersionId);

    internal static void ValidateProjectionItems(IReadOnlyList<ActivityDependencyItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var supportedOwners = new HashSet<string>(["ActivityVersion", "ActivityDraft", "WorkflowVersion", "WorkflowDraft"], StringComparer.Ordinal);
        if (items.Any(x => string.IsNullOrWhiteSpace(x.RelationshipId) ||
                           string.IsNullOrWhiteSpace(x.Occurrence.OccurrenceId) ||
                           !supportedOwners.Contains(x.Owner.Kind) ||
                           !HasOwnerIdentity(x.Owner) ||
                           !StringComparer.Ordinal.Equals(x.Dependency.Kind, "ActivityVersion") ||
                           string.IsNullOrWhiteSpace(x.Dependency.VersionId)))
            throw new ArgumentException("Projection items require a supported mixed owner, occurrence, and exact activity-version dependency.", nameof(items));
    }

    private static ActivityDependencyItem[] TraverseProjection(string rootVersionId, ActivityDependencyDirection direction, bool transitive, IReadOnlyList<ActivityDependencyItem> items)
    {
        var direct = items
            .Where(x => x.IsDirect)
            .OrderBy(x => x.Owner.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Owner.DraftId ?? x.Owner.VersionId, StringComparer.Ordinal)
            .ThenBy(x => x.Occurrence.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.Dependency.VersionId, StringComparer.Ordinal)
            .ToArray();
        var result = new List<ActivityDependencyItem>();
        var queue = new Queue<ProjectionTraversalState>();
        queue.Enqueue(new(rootVersionId, 0, [], new HashSet<string>([rootVersionId], StringComparer.Ordinal)));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var edges = direction == ActivityDependencyDirection.Inbound
                ? direct.Where(x => StringComparer.Ordinal.Equals(x.Dependency.VersionId, current.VersionId))
                : direct.Where(x => StringComparer.Ordinal.Equals(x.Owner.VersionId, current.VersionId));
            foreach (var edge in edges)
            {
                var path = current.Path.Count == 0
                    ? edge.Path
                    : direction == ActivityDependencyDirection.Inbound
                        ? new[] { edge.Owner }.Concat(current.Path).ToArray()
                        : current.Path.Concat([edge.Dependency]).ToArray();
                var projected = edge with { Depth = current.Depth + 1, Path = path };
                result.Add(projected);
                if (!transitive)
                    continue;
                var next = direction == ActivityDependencyDirection.Inbound ? edge.Owner.VersionId : edge.Dependency.VersionId;
                if (next is null || current.VisitedVersions.Contains(next))
                    continue;
                queue.Enqueue(new(next, current.Depth + 1, path, new HashSet<string>(current.VisitedVersions, StringComparer.Ordinal) { next }));
            }
        }
        return result.ToArray();
    }

    private sealed record ProjectionTraversalState(
        string VersionId,
        int Depth,
        IReadOnlyList<ActivityDefinitionReference> Path,
        IReadOnlySet<string> VisitedVersions);

    private void EnsureTenant(string? tenantId)
    {
        access?.Current.EnsureTenantScope(tenantId);
    }

    private void EnsureReadTenant(string? tenantId)
    {
        if (access?.Current.AcrossScopes == true)
            return;
        access?.Current.EnsureTenantScope(tenantId);
    }

    private void EnsurePrivilegedAcrossScopesForProjectionReplacement()
    {
        var context = access?.Current;
        if (context is null || context.AccessPolicy != PersistenceAccessPolicy.Privileged || !context.AcrossScopes)
            throw new InvalidOperationException("Activity dependency projection replacement requires an explicit privileged-across-scopes context.");
    }

    private static bool TryGetAuthorityHashMaterial(string? rawJson, out string raw, out string hash)
    {
        raw = string.Empty;
        hash = string.Empty;
        if (string.IsNullOrWhiteSpace(rawJson))
            return false;
        try
        {
            var node = JsonNode.Parse(rawJson);
            if (node is not JsonObject obj || obj["integrityHash"]?.GetValue<string>() is not { } embeddedHash)
                return false;
            obj.Remove("integrityHash");
            raw = obj.ToJsonString(Json);
            hash = embeddedHash;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private void EnsureTenantAgnostic(ActivityDefinitionFilter filter)
    {
        if (filter.TenantAgnostic != true)
            return;

        if (access is null || access.Current.AccessPolicy != PersistenceAccessPolicy.Privileged || !access.Current.AcrossScopes)
            throw new InvalidOperationException("Activity-definition tenant-agnostic queries require an explicit privileged-across-scopes context.");
    }

    private static void EnsureDesignAuthoring(ActivityDefinitionAuthoringState authoring, string definitionId, string? tenantId)
    {
        if (authoring.DefinitionId != definitionId || authoring.TenantId != tenantId)
            throw new InvalidOperationException("Activity authoring state ownership does not match the requested definition.");
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw new InvalidOperationException($"Activity definition '{definitionId}' is owned by provider source '{authoring.ContentAuthority.AuthorityKey}'.");
    }

    private IQueryable<T> Access<T>(IQueryable<T> query) where T : Elsa.Primitives.Entities.TenantEntity =>
        AccessFilter(query, access);

    /// <summary>The rows <paramref name="access"/> may see: its own scope and the global scope.</summary>
    internal static IQueryable<T> AccessFilter<T>(IQueryable<T> query, IPersistenceAccessContextAccessor? access)
        where T : Elsa.Primitives.Entities.TenantEntity
    {
        if (access is null || access.Current.AcrossScopes)
            return query;
        if (access.Current.Scope is { } scope)
        {
            var scopeKey = NormalizeTenantKey(scope.Value);
            return query.Where(x =>
                (x.TenantId == scope.Value && EF.Property<string>(x, "TenantScopeKey") == scopeKey) ||
                (x.TenantId == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey));
        }
        return query.Where(x => x.TenantId == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey);
    }

    private IQueryable<ActivityUpgradePlanRecord> Access(IQueryable<ActivityUpgradePlanRecord> query)
    {
        if (access is null || access.Current.AcrossScopes) return query;
        if (access.Current.Scope is { } scope)
        {
            var scopeKey = NormalizeTenantKey(scope.Value);
            return query.Where(x =>
                (x.TenantId == scope.Value && EF.Property<string>(x, "TenantScopeKey") == scopeKey) ||
                (x.TenantId == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey));
        }
        return query.Where(x => x.TenantId == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey);
    }

    private IQueryable<ActivityUpgradeApplyReceiptRecord> Access(IQueryable<ActivityUpgradeApplyReceiptRecord> query)
    {
        if (access is null || access.Current.AcrossScopes) return query;
        if (access.Current.Scope is { } scope)
        {
            var scopeKey = NormalizeTenantKey(scope.Value);
            return query.Where(x =>
                (x.TenantId == scope.Value && EF.Property<string>(x, "TenantScopeKey") == scopeKey) ||
                (x.TenantId == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey));
        }
        return query.Where(x => x.TenantId == null && EF.Property<string>(x, "TenantScopeKey") == ActivitiesDesignDbContext.GlobalTenantKey);
    }
}

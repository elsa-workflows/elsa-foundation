using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Activities.Design.Persistence.Groundwork.Services;

/// <summary>
/// Groundwork implementation of the reusable-activity Design read and mutation ports. Multi-document
/// mutations use Groundwork's document unit of work with envelope-version CAS on every previously read
/// participant. The cross-domain publication commit is intentionally absent: Publishing owns the atomic
/// Design + Runtime template + Source Reference boundary.
/// Hosts must therefore select a Groundwork provider with <c>CrossUnitAtomic</c> transaction support; a
/// single-unit provider cannot safely execute the multi-kind authoring commands exposed by this adapter.
/// </summary>
public sealed class GroundworkReusableActivityStores(
    IDocumentStore store,
    ISystemClock clock,
    IDistributedLockProvider lockProvider,
    IBoundedDocumentStore? boundedStore = null) :
    IActivityDefinitionAuthoringStore,
    IActivityDefinitionDraftStore,
    IActivityDefinitionVersionPublicationStore,
    IActivityDefinitionManagementStore,
    IRecommendedActivityDefinitionPickerStore,
    IActivityDefinitionLayoutStore,
    IActivityDraftValidationStore,
    IActivityDirectDependencyStore,
    IActivityDependencyProjectionStore,
    ICreateActivityDefinitionCommand,
    IUpdateActivityDefinitionPresentationCommand,
    ICreateActivityDraftCommand,
    IUpdateActivityDraftPresentationCommand,
    ICreateActivityDraftConflictCopyCommand,
    IReplaceActivityDraftCommand,
    IApplyActivityContractProposalCommand,
    IDiscardActivityDraftCommand,
    IStoreActivityDraftValidationCommand,
    IChangeActivityVersionLifecycleCommand,
    ISetActivityDefinitionRecommendationCommand
{
    // This initial concrete projection derives immutable activity-version edges. The aggregate port
    // already carries all four owner kinds; T060 adds current activity/workflow draft and workflow-
    // version projection sources without changing the public dependency read model.
    private static readonly JsonSerializerOptions JsonOptions = GroundworkActivitiesDesignJson.Options;

    public async Task<ActivityDefinitionAuthoringState?> FindAsync(
        string definitionId,
        CancellationToken cancellationToken = default) =>
        (await FindSingleAsync<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            definitionId,
            cancellationToken))?.Entity;

    public async Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(
        IEnumerable<string> definitionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = definitionIds.ToHashSet(StringComparer.Ordinal);
        var documents = await ListAllAsync<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
            cancellationToken);
        return documents.Select(x => x.Entity).Where(x => ids.Contains(x.DefinitionId)).ToArray();
    }

    async Task<ActivityDefinitionDraft?> IActivityDefinitionDraftStore.FindAsync(
        string draftId,
        CancellationToken cancellationToken) =>
        (await LoadAsync<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            draftId,
            cancellationToken))?.Entity;

    public async Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            definitionId,
            cancellationToken))
        .Select(x => x.Entity)
        .OrderBy(x => x.Id, StringComparer.Ordinal)
        .ToArray();

    async Task<ActivityDefinitionVersionPublication?> IActivityDefinitionVersionPublicationStore.FindAsync(
        string definitionVersionId,
        CancellationToken cancellationToken) =>
        (await FindSingleAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
            definitionVersionId,
            cancellationToken))?.Entity;

    async Task<IReadOnlyList<ActivityDefinitionVersionPublication>> IActivityDefinitionVersionPublicationStore.ListByDefinitionAsync(
        string definitionId,
        CancellationToken cancellationToken) =>
        (await QueryAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            definitionId,
            cancellationToken))
        .Select(x => x.Entity)
        .OrderBy(x => x.Version, StringComparer.Ordinal)
        .ToArray();

    public async Task<RecommendedActivityDefinitionPickerPage> ReadAsync(
        string? tenantId,
        int offset,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));

        var reader = boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
            "Recommended activity picker queries require an admitted bounded document-store runtime.");
        var items = new List<RecommendedActivityDefinitionPickerItem>(limit);
        var sourceOffset = offset;
        long totalCount = offset;
        while (items.Count < limit)
        {
            var result = await reader.QueryAsync(
                new DocumentQuery(
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                    ActivitiesDesignStorageManifest.ListAllQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                        ActivitiesDesignStorageManifest.CollectionField,
                        ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection))],
                    null,
                    sourceOffset,
                    Math.Min(100, Math.Max(limit * 2, 20))),
                cancellationToken);
            totalCount = result.TotalCount;
            if (result.Documents.Count == 0)
                break;

            foreach (var envelope in result.Documents)
            {
                sourceOffset++;
                var authoring = Deserialize<ActivityDefinitionAuthoringState>(
                    envelope,
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind).Entity;
                if (!IsVisible(authoring.TenantId, tenantId) || authoring.RecommendedVersionId is null)
                    continue;
                var publication = await ((IActivityDefinitionVersionPublicationStore)this).FindAsync(authoring.RecommendedVersionId, cancellationToken);
                if (publication is null ||
                    publication.Lifecycle != ActivityDefinitionVersionLifecycle.Active ||
                    !StringComparer.Ordinal.Equals(publication.DefinitionId, authoring.DefinitionId) ||
                    !StringComparer.Ordinal.Equals(publication.TenantId, authoring.TenantId))
                    continue;
                var definition = await LoadAsync<ActivityDefinition>(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                    authoring.DefinitionId,
                    cancellationToken);
                if (definition is null ||
                    !StringComparer.Ordinal.Equals(definition.Entity.TenantId, authoring.TenantId) ||
                    !IsVisible(definition.Entity.TenantId, tenantId))
                    continue;
                items.Add(new(definition.Entity, publication));
                if (items.Count == limit)
                    break;
            }
        }

        return new(items, sourceOffset < totalCount ? sourceOffset : null);
    }

    public async Task<ActivityManagementPage<ActivityDefinitionManagementRecord>> ReadDefinitionsAsync(
        ActivityManagementPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateManagementQuery(query);
        var authoringRows = await ReadAllPagesAsync<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ListAllQuery,
            ActivitiesDesignStorageManifest.CollectionField,
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
            cancellationToken);
        var definitionRows = await ReadAllPagesAsync<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListAllQuery,
            ActivitiesDesignStorageManifest.CollectionField,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            cancellationToken);
        var draftRows = await ReadAllPagesAsync<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            ActivitiesDesignStorageManifest.ListAllQuery,
            ActivitiesDesignStorageManifest.CollectionField,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection,
            cancellationToken);
        var versionRows = await ReadAllPagesAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ListAllQuery,
            ActivitiesDesignStorageManifest.CollectionField,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
            cancellationToken);
        var definitionsById = definitionRows.Select(x => x.Entity)
            .Where(x => x.LastModifiedAt <= query.AsOf)
            .ToDictionary(x => x.Id, StringComparer.Ordinal);
        var drafts = draftRows.Select(x => x.Entity).Where(x => x.LastModifiedAt <= query.AsOf).ToArray();
        var versions = versionRows.Select(x => x.Entity).Where(x => x.LastModifiedAt <= query.AsOf).ToArray();
        var versionsById = versions.ToDictionary(x => x.DefinitionVersionId, StringComparer.Ordinal);
        var matches = authoringRows.Select(x => x.Entity)
            .Where(x => IsVisible(x.TenantId, query.TenantId) && x.LastModifiedAt <= query.AsOf)
            .Select(authoring => ManagementRecord(authoring, definitionsById, drafts, versions, versionsById))
            .Where(x => x is not null && Matches(x, query))
            .Select(x => x!)
            .OrderBy(x => x.Definition.ActivityTypeKey, StringComparer.Ordinal)
            .ToArray();
        var items = matches.Skip(query.Offset).Take(query.Limit).ToArray();
        int? nextOffset = query.Offset + items.Length < matches.Length ? query.Offset + items.Length : null;
        return new(items, nextOffset, matches.LongLength, query.AsOf);
    }

    public async Task<ActivityDefinitionManagementRecord?> FindDefinitionAsync(
        string definitionId,
        string? tenantId,
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        var authoring = await FindAsync(definitionId, cancellationToken);
        if (authoring is null || !IsVisible(authoring.TenantId, tenantId) || authoring.LastModifiedAt > asOf)
            return null;
        return await ManagementRecordAsync(authoring, asOf, cancellationToken);
    }

    public async Task<ActivityManagementPage<ActivityDefinitionDraft>> ReadDraftsAsync(
        string definitionId,
        ActivityManagementPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateManagementQuery(query);
        var authoring = await FindAsync(definitionId, cancellationToken);
        if (authoring is null || !IsVisible(authoring.TenantId, query.TenantId))
            return new([], null, 0, query.AsOf);
        var rows = await ReadAllPagesAsync<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            "list-by-definition",
            ActivitiesDesignStorageManifest.DefinitionIdField,
            definitionId,
            cancellationToken);
        var matches = rows.Select(x => x.Entity)
            .Where(x => StringComparer.Ordinal.Equals(x.TenantId, authoring.TenantId))
            .Where(x => x.LastModifiedAt <= query.AsOf)
            .Where(x => query.DraftStatus is null || x.Status == query.DraftStatus)
            .Where(x => query.ProviderKey is null || StringComparer.Ordinal.Equals(x.State.Provider.ProviderKey, query.ProviderKey))
            .Where(x => query.Search is null || Contains(x.PresentationLabel, query.Search) || Contains(x.Id, query.Search))
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();
        var items = matches.Skip(query.Offset).Take(query.Limit).ToArray();
        int? nextOffset = query.Offset + items.Length < matches.Length ? query.Offset + items.Length : null;
        return new(items, nextOffset, matches.LongLength, query.AsOf);
    }

    public async Task<ActivityManagementPage<ActivityDefinitionVersionPublication>> ReadVersionsAsync(
        string definitionId,
        ActivityManagementPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ValidateManagementQuery(query);
        var authoring = await FindAsync(definitionId, cancellationToken);
        if (authoring is null || !IsVisible(authoring.TenantId, query.TenantId))
            return new([], null, 0, query.AsOf);
        var rows = await ReadAllPagesAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            "list-by-definition",
            ActivitiesDesignStorageManifest.DefinitionIdField,
            definitionId,
            cancellationToken);
        var matches = rows.Select(x => x.Entity)
            .Where(x => StringComparer.Ordinal.Equals(x.TenantId, authoring.TenantId))
            .Where(x => x.LastModifiedAt <= query.AsOf)
            .Where(x => query.VersionLifecycle is null || x.Lifecycle == query.VersionLifecycle)
            .Where(x => query.ProviderKey is null || StringComparer.Ordinal.Equals(x.Provider.ProviderKey, query.ProviderKey))
            .Where(x => query.Search is null || Contains(x.Version, query.Search) || Contains(x.DefinitionVersionId, query.Search))
            .OrderBy(x => x.DefinitionVersionId, StringComparer.Ordinal)
            .ToArray();
        var items = matches.Skip(query.Offset).Take(query.Limit).ToArray();
        int? nextOffset = query.Offset + items.Length < matches.Length ? query.Offset + items.Length : null;
        return new(items, nextOffset, matches.LongLength, query.AsOf);
    }

    public async Task<ActivityDefinitionDraftLayout?> FindDraftLayoutAsync(
        string draftId,
        CancellationToken cancellationToken = default) =>
        (await FindSingleAsync<ActivityDefinitionDraftLayout>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ByDraftIndex,
            draftId,
            cancellationToken))?.Entity;

    public async Task<ActivityDefinitionVersionLayout?> FindVersionLayoutAsync(
        string definitionVersionId,
        CancellationToken cancellationToken = default) =>
        (await FindSingleAsync<ActivityDefinitionVersionLayout>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
            definitionVersionId,
            cancellationToken))?.Entity;

    async Task<ActivityDraftValidationState?> IActivityDraftValidationStore.FindAsync(
        string draftId,
        long revision,
        CancellationToken cancellationToken) =>
        (await QueryAsync<ActivityDraftValidationState>(
            ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind,
            ActivitiesDesignStorageManifest.ByDraftIndex,
            draftId,
            cancellationToken))
        .Select(x => x.Entity)
        .SingleOrDefault(x => x.Revision == revision);

    public async Task<IReadOnlyList<ActivityDependencyEdge>> ListOutboundAsync(
        string ownerVersionId,
        CancellationToken cancellationToken = default) =>
        (await QueryAsync<ActivityDependencyEdge>(
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            ActivitiesDesignStorageManifest.ByOwnerVersionIndex,
            ownerVersionId,
            cancellationToken))
        .Select(x => x.Entity)
        .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
        .ThenBy(x => x.DependencyVersionId, StringComparer.Ordinal)
        .ToArray();

    public async Task<ActivityDependencyProjectionSlice> ReadAsync(
        ActivityDependencyProjectionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.Limit));
        if (request.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(request.Offset));

        var publicationDocuments = await ListAllAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
            cancellationToken);
        var publications = publicationDocuments
            .Select(x => x.Entity)
            .ToDictionary(x => x.DefinitionVersionId, StringComparer.Ordinal);
        if (!publications.TryGetValue(request.RootVersionId, out var root))
            throw Missing($"Activity version publication '{request.RootVersionId}' was not found.");

        var edgeDocuments = await ListAllAsync<ActivityDependencyEdge>(
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeCollection,
            cancellationToken);
        var fingerprint = Fingerprint(edgeDocuments, publicationDocuments);
        if (request.Watermark is not null && !StringComparer.Ordinal.Equals(request.Watermark, fingerprint))
            throw new ActivityDependencyWatermarkExpiredException(request.Watermark);
        var traversed = Traverse(request.RootVersionId, request.Query, edgeDocuments.Select(x => x.Entity).ToArray());
        var visibleTraversed = traversed
            .Where(x => IsVisible(RequiredPublication(x.Edge.OwnerVersionId, publications).TenantId, request.TenantId) &&
                        IsVisible(RequiredPublication(x.Edge.DependencyVersionId, publications).TenantId, request.TenantId))
            .Where(_ => request.Query.Include.Contains("Versions"))
            .ToArray();
        var items = visibleTraversed.Skip(request.Offset).Take(request.Limit).Select(x => ToItem(x, publications)).ToArray();
        var nextOffset = request.Offset + items.Length;

        return new ActivityDependencyProjectionSlice(
            ToReference(root),
            new ActivityDependencyConsistency(
                ActivityDependencyConsistencyKind.DerivedProjection,
                false,
                null,
                edgeDocuments.Select(x => x.Envelope.UpdatedAt)
                    .Concat(publicationDocuments.Select(x => x.Envelope.UpdatedAt))
                    .DefaultIfEmpty()
                    .Max()),
            items,
            fingerprint,
            nextOffset < visibleTraversed.Length ? nextOffset : null);
    }

    public async Task ExecuteAsync(CreateActivityDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        ValidateCreate(request);
        await using var lockHandle = await lockProvider.AcquireLockAsync(
            DefinitionKeyLock(request.Definition.TenantId, request.Definition.ActivityTypeKey),
            null,
            cancellationToken);

        var existingDefinitions = await ListAllAsync<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
            cancellationToken);
        if (existingDefinitions.Any(x =>
                StringComparer.Ordinal.Equals(x.Entity.TenantId, request.Definition.TenantId) &&
                StringComparer.Ordinal.Equals(x.Entity.ActivityTypeKey, request.Definition.ActivityTypeKey)))
            throw Conflict($"Activity definition key '{request.Definition.ActivityTypeKey}' already exists.");

        await EnsureAbsentAsync<ActivityDefinition>(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, request.Definition.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionAuthoringState>(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, request.AuthoringState.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionDraft>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, request.InitialDraft.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionDraftLayout>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, request.InitialLayout.Id, cancellationToken);
        if (await FindAsync(request.Definition.Id, cancellationToken) is not null)
            throw Conflict($"Activity definition authoring state '{request.Definition.Id}' already exists.");
        if (await FindDraftLayoutAsync(request.InitialDraft.Id, cancellationToken) is not null)
            throw Conflict($"Activity draft layout '{request.InitialDraft.Id}' already exists.");

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionCollection, request.Definition, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection, request.AuthoringState, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, request.InitialDraft, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, request.InitialLayout, 0)
            ],
            cancellationToken);
    }

    public async Task<ActivityDefinition> ExecuteAsync(
        UpdateActivityDefinitionPresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        var definition = await LoadAsync<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            request.DefinitionId,
            cancellationToken)
            ?? throw Missing($"Activity definition '{request.DefinitionId}' was not found.");
        var authoring = await RequiredAuthoringAsync(request.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        if (!StringComparer.Ordinal.Equals(definition.Entity.TenantId, authoring.Entity.TenantId))
            throw Conflict($"Activity definition '{request.DefinitionId}' tenant does not match its authoring state.");
        if (!IsVisible(definition.Entity.TenantId, request.TenantId) || !IsVisible(authoring.Entity.TenantId, request.TenantId))
            throw Conflict($"Activity definition '{request.DefinitionId}' is outside the caller tenant scope.");

        definition.Entity.Category = request.Category;
        definition.Entity.DisplayName = request.DisplayName;
        definition.Entity.Description = request.Description;
        definition.Entity.LastModifiedAt = request.LastModifiedAt;

        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind),
            [Save(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                definition.Entity,
                definition.Envelope.Version)],
            cancellationToken);
        return definition.Entity;
    }

    public async Task ExecuteAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        ValidateDraftAndLayout(request.Draft, request.Layout);
        var authoring = await RequiredAuthoringAsync(request.Draft.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureExpectedHead(authoring.Entity, request.ExpectedDefinitionHeadVersionId);
        EnsureTenant(authoring.Entity, request.Draft);

        await EnsureAbsentAsync<ActivityDefinitionDraft>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, request.Draft.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionDraftLayout>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, request.Layout.Id, cancellationToken);
        if (await FindDraftLayoutAsync(request.Draft.Id, cancellationToken) is not null)
            throw Conflict($"Activity draft layout '{request.Draft.Id}' already exists.");

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection, authoring.Entity, authoring.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, request.Draft, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, request.Layout, 0)
            ],
            cancellationToken);
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(
        UpdateActivityDraftPresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await RequiredDraftAsync(request.DraftId, cancellationToken);
        EnsureActiveRevision(draft.Entity, request.ExpectedRevision);
        var authoring = await RequiredAuthoringAsync(draft.Entity.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureTenant(authoring.Entity, draft.Entity);
        var layout = await RequiredDraftLayoutAsync(request.DraftId, cancellationToken);
        if (layout.Entity.Revision != draft.Entity.Revision)
            throw Conflict($"Draft '{request.DraftId}' and its layout do not have the same revision.");
        draft.Entity.Revision = checked(draft.Entity.Revision + 1);
        draft.Entity.PresentationLabel = request.PresentationLabel;
        draft.Entity.LastModifiedAt = request.ChangedAt;
        layout.Entity.Revision = draft.Entity.Revision;
        layout.Entity.LastModifiedAt = request.ChangedAt;
        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout.Entity, layout.Envelope.Version)
            ],
            cancellationToken);
        return draft.Entity;
    }

    public async Task ExecuteAsync(
        CreateActivityDraftConflictCopyRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateDraftAndLayout(request.ConflictCopy, request.Layout);
        var source = await RequiredDraftAsync(request.SourceDraftId, cancellationToken);
        EnsureActiveRevision(source.Entity, request.ExpectedSourceRevision);
        var authoring = await RequiredAuthoringAsync(source.Entity.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureTenant(authoring.Entity, source.Entity);
        if (!StringComparer.Ordinal.Equals(source.Entity.DefinitionId, request.ConflictCopy.DefinitionId) ||
            !StringComparer.Ordinal.Equals(source.Entity.TenantId, request.ConflictCopy.TenantId) ||
            !StringComparer.Ordinal.Equals(source.Entity.SourceVersionId, request.ConflictCopy.SourceVersionId))
            throw new ArgumentException("The conflict copy must inherit its source draft lineage.", nameof(request));
        await EnsureAbsentAsync<ActivityDefinitionDraft>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, request.ConflictCopy.Id, cancellationToken);
        if (await FindDraftLayoutAsync(request.ConflictCopy.Id, cancellationToken) is not null)
            throw Conflict($"Activity draft layout '{request.ConflictCopy.Id}' already exists.");
        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, source.Entity, source.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, request.ConflictCopy, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, request.Layout, 0)
            ],
            cancellationToken);
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(
        ReplaceActivityDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await RequiredDraftAsync(request.DraftId, cancellationToken);
        EnsureActiveRevision(draft.Entity, request.ExpectedRevision);
        var authoring = await RequiredAuthoringAsync(draft.Entity.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureTenant(authoring.Entity, draft.Entity);
        var layout = await RequiredDraftLayoutAsync(request.DraftId, cancellationToken);
        if (layout.Entity.Revision != draft.Entity.Revision)
            throw Conflict($"Draft '{request.DraftId}' and its layout do not have the same revision.");

        var now = clock.UtcNow;
        draft.Entity.Revision = checked(draft.Entity.Revision + 1);
        draft.Entity.State = request.State;
        draft.Entity.PresentationLabel = request.PresentationLabel;
        draft.Entity.LastModifiedAt = now;
        layout.Entity.Revision = draft.Entity.Revision;
        layout.Entity.Records = request.Layout.ToList();
        layout.Entity.LastModifiedAt = now;

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout.Entity, layout.Envelope.Version)
            ],
            cancellationToken);

        return draft.Entity;
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(
        ApplyActivityContractProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        var draft = await RequiredDraftAsync(request.DraftId, cancellationToken);
        EnsureActiveRevision(draft.Entity, request.ExpectedRevision);
        var authoring = await RequiredAuthoringAsync(draft.Entity.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureTenant(authoring.Entity, draft.Entity);
        if (!IsVisible(draft.Entity.TenantId, request.TenantId) ||
            !StringComparer.Ordinal.Equals(draft.Entity.State.Provider.ProviderKey, request.ExpectedProviderKey) ||
            !StringComparer.Ordinal.Equals(draft.Entity.State.Provider.SchemaVersion, request.ExpectedProviderSchemaVersion) ||
            !StringComparer.Ordinal.Equals(ActivityProviderManifestFingerprint.Compute(draft.Entity.State.Provider), request.ExpectedManifestFingerprint))
            throw Conflict($"Draft '{request.DraftId}' provider binding is stale.");
        var layout = await RequiredDraftLayoutAsync(request.DraftId, cancellationToken);
        if (layout.Entity.Revision != draft.Entity.Revision)
            throw Conflict($"Draft '{request.DraftId}' and its layout do not have the same revision.");

        var now = clock.UtcNow;
        draft.Entity.Revision = checked(draft.Entity.Revision + 1);
        draft.Entity.State = draft.Entity.State with { Contract = request.Contract };
        draft.Entity.LastModifiedAt = now;
        layout.Entity.Revision = draft.Entity.Revision;
        layout.Entity.LastModifiedAt = now;
        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout.Entity, layout.Envelope.Version)
            ],
            cancellationToken);
        return draft.Entity;
    }

    public async Task ExecuteAsync(DiscardActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        var draft = await RequiredDraftAsync(request.DraftId, cancellationToken);
        EnsureActiveRevision(draft.Entity, request.ExpectedRevision);
        var authoring = await RequiredAuthoringAsync(draft.Entity.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureTenant(authoring.Entity, draft.Entity);
        draft.Entity.Revision = checked(draft.Entity.Revision + 1);
        draft.Entity.Status = ActivityDefinitionDraftStatus.Discarded;
        draft.Entity.LastModifiedAt = clock.UtcNow;

        await store.SaveAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind),
            [Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version)],
            cancellationToken);
    }

    public async Task ExecuteAsync(ActivityDraftValidationState validation, CancellationToken cancellationToken = default)
    {
        var draft = await RequiredDraftAsync(validation.DraftId, cancellationToken);
        if (draft.Entity.Revision != validation.Revision)
            throw Conflict($"Draft '{validation.DraftId}' is at revision {draft.Entity.Revision}, not {validation.Revision}.");

        var matches = (await QueryAsync<ActivityDraftValidationState>(
                ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind,
                ActivitiesDesignStorageManifest.ByDraftIndex,
                validation.DraftId,
                cancellationToken))
            .Where(x => x.Entity.Revision == validation.Revision)
            .ToArray();
        if (matches.Length > 1)
            throw Conflict($"Multiple validations exist for draft '{validation.DraftId}' revision {validation.Revision}.");
        if (matches.SingleOrDefault() is { } existing && !StringComparer.Ordinal.Equals(existing.Entity.Id, validation.Id))
            throw Conflict($"Validation identity for draft '{validation.DraftId}' revision {validation.Revision} does not match.");

        await store.SaveAllAsync(
            DocumentCommitScope.Of(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind),
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind, ActivitiesDesignStorageManifest.ActivityDraftValidationCollection, validation, matches.SingleOrDefault()?.Envelope.Version ?? 0)
            ],
            cancellationToken);
    }

    public async Task<ActivityDefinitionVersionPublication> ExecuteAsync(
        ChangeActivityVersionLifecycleRequest request,
        CancellationToken cancellationToken = default)
    {
        var publication = await RequiredPublicationAsync(request.DefinitionVersionId, cancellationToken);
        var authoring = await RequiredAuthoringAsync(publication.Entity.DefinitionId, cancellationToken);
        if (!IsVisible(authoring.Entity.TenantId, request.TenantId) ||
            !StringComparer.Ordinal.Equals(authoring.Entity.TenantId, publication.Entity.TenantId))
            throw Conflict($"Activity version '{request.DefinitionVersionId}' is outside the caller tenant scope.");
        if (publication.Entity.Lifecycle != request.ExpectedLifecycle)
            throw Conflict($"Activity version '{request.DefinitionVersionId}' is {publication.Entity.Lifecycle}, not {request.ExpectedLifecycle}.");
        if (!IsAllowedTransition(publication.Entity.Lifecycle, request.Lifecycle))
            throw Conflict($"Activity version lifecycle cannot transition from {publication.Entity.Lifecycle} to {request.Lifecycle}.");

        Stored<ActivityDefinitionVersionPublication>? replacement = null;
        var invalidatesRecommendation = request.Lifecycle is ActivityDefinitionVersionLifecycle.Retired or ActivityDefinitionVersionLifecycle.Revoked &&
                                        StringComparer.Ordinal.Equals(authoring.Entity.RecommendedVersionId, publication.Entity.DefinitionVersionId);
        if (invalidatesRecommendation)
        {
            var decision = request.RecommendationDecision ?? throw Conflict("An explicit recommendation decision is required.");
            EnsureExpectedHead(authoring.Entity, decision.ExpectedDefinitionHeadVersionId);
            if (!StringComparer.Ordinal.Equals(authoring.Entity.RecommendedVersionId, decision.ExpectedRecommendedVersionId))
                throw Conflict($"Activity definition '{authoring.Entity.DefinitionId}' recommendation is stale.");
            if (decision.Disposition == ActivityRecommendationDisposition.Clear)
            {
                if (decision.ReplacementVersionId is not null || decision.ExpectedReplacementLifecycle is not null)
                    throw new ArgumentException("A clear recommendation decision cannot include a replacement.", nameof(request));
                authoring.Entity.RecommendedVersionId = null;
            }
            else
            {
                if (decision.ReplacementVersionId is null || decision.ExpectedReplacementLifecycle is null)
                    throw new ArgumentException("A replacement recommendation decision requires a version and lifecycle.", nameof(request));
                replacement = await RequiredPublicationAsync(decision.ReplacementVersionId, cancellationToken);
                if (!StringComparer.Ordinal.Equals(replacement.Entity.DefinitionId, publication.Entity.DefinitionId) ||
                    !StringComparer.Ordinal.Equals(replacement.Entity.TenantId, publication.Entity.TenantId))
                    throw Missing($"Activity version publication '{decision.ReplacementVersionId}' was not found.");
                if (replacement.Entity.Lifecycle != decision.ExpectedReplacementLifecycle ||
                    replacement.Entity.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
                    throw Conflict($"Activity version '{decision.ReplacementVersionId}' lifecycle is stale.");
                authoring.Entity.RecommendedVersionId = replacement.Entity.DefinitionVersionId;
            }
            authoring.Entity.LastModifiedAt = clock.UtcNow;
        }
        else if (request.RecommendationDecision is not null)
            throw new ArgumentException("A recommendation decision is valid only for the recommended version.", nameof(request));

        publication.Entity.Lifecycle = request.Lifecycle;
        publication.Entity.LastModifiedAt = clock.UtcNow;
        var requests = new List<SaveDocumentRequest>
        {
            Save(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection, publication.Entity, publication.Envelope.Version)
        };
        if (invalidatesRecommendation)
            requests.Add(Save(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection, authoring.Entity, authoring.Envelope.Version));
        if (replacement is not null)
            requests.Add(Save(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection, replacement.Entity, replacement.Envelope.Version));
        await store.SaveAllAsync(
            invalidatesRecommendation
                ? DocumentCommitScope.Of(
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind)
                : DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind),
            requests,
            cancellationToken);
        return publication.Entity;
    }

    public async Task<ActivityDefinitionAuthoringState> ExecuteAsync(
        SetActivityDefinitionRecommendationRequest request,
        CancellationToken cancellationToken = default)
    {
        var authoring = await RequiredAuthoringAsync(request.DefinitionId, cancellationToken);
        if (!IsVisible(authoring.Entity.TenantId, request.TenantId))
            throw Conflict($"Activity definition '{request.DefinitionId}' is outside the caller tenant scope.");
        EnsureExpectedHead(authoring.Entity, request.ExpectedDefinitionHeadVersionId);
        if (!StringComparer.Ordinal.Equals(authoring.Entity.RecommendedVersionId, request.ExpectedRecommendedVersionId))
            throw Conflict($"Activity definition '{request.DefinitionId}' recommendation is stale.");

        Stored<ActivityDefinitionVersionPublication>? target = null;
        if (request.RecommendedVersionId is null)
        {
            if (request.ExpectedRecommendedVersionLifecycle is not null)
                throw new ArgumentException("A cleared recommendation cannot declare a target lifecycle.", nameof(request));
        }
        else
        {
            if (request.ExpectedRecommendedVersionLifecycle is null)
                throw new ArgumentException("A recommendation target requires an expected lifecycle.", nameof(request));
            target = await RequiredPublicationAsync(request.RecommendedVersionId, cancellationToken);
            if (!StringComparer.Ordinal.Equals(target.Entity.DefinitionId, request.DefinitionId) ||
                !StringComparer.Ordinal.Equals(target.Entity.TenantId, authoring.Entity.TenantId))
                throw Missing($"Activity version publication '{request.RecommendedVersionId}' was not found.");
            if (target.Entity.Lifecycle != request.ExpectedRecommendedVersionLifecycle)
                throw Conflict($"Activity version '{request.RecommendedVersionId}' lifecycle is stale.");
            if (target.Entity.Lifecycle != ActivityDefinitionVersionLifecycle.Active)
                throw Conflict($"Activity version '{request.RecommendedVersionId}' is not active.");
        }

        authoring.Entity.RecommendedVersionId = request.RecommendedVersionId;
        authoring.Entity.LastModifiedAt = request.ChangedAt;
        var requests = new List<SaveDocumentRequest>
        {
            Save(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
                authoring.Entity,
                authoring.Envelope.Version)
        };
        if (target is not null)
            requests.Add(Save(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
                target.Entity,
                target.Envelope.Version));
        await store.SaveAllAsync(
            target is null
                ? DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind)
                : DocumentCommitScope.Of(
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind),
            requests,
            cancellationToken);
        return authoring.Entity;
    }

    private async Task<Stored<ActivityDefinitionAuthoringState>> RequiredAuthoringAsync(string definitionId, CancellationToken cancellationToken) =>
        await FindSingleAsync<ActivityDefinitionAuthoringState>(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            definitionId,
            cancellationToken)
        ?? throw Missing($"Activity definition authoring state '{definitionId}' was not found.");

    private async Task<Stored<ActivityDefinitionDraft>> RequiredDraftAsync(string draftId, CancellationToken cancellationToken) =>
        await LoadAsync<ActivityDefinitionDraft>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, draftId, cancellationToken)
        ?? throw Missing($"Activity draft '{draftId}' was not found.");

    private async Task<Stored<ActivityDefinitionDraftLayout>> RequiredDraftLayoutAsync(string draftId, CancellationToken cancellationToken) =>
        await FindSingleAsync<ActivityDefinitionDraftLayout>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ByDraftIndex,
            draftId,
            cancellationToken)
        ?? throw Missing($"Activity draft layout '{draftId}' was not found.");

    private async Task<Stored<ActivityDefinitionVersionPublication>> RequiredPublicationAsync(string versionId, CancellationToken cancellationToken) =>
        await FindSingleAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
            versionId,
            cancellationToken)
        ?? throw Missing($"Activity version publication '{versionId}' was not found.");

    private async Task EnsureAbsentAsync<TEntity>(string kind, string id, CancellationToken cancellationToken)
        where TEntity : Entity
    {
        if (await LoadAsync<TEntity>(kind, id, cancellationToken) is not null)
            throw Conflict($"Document '{id}' of kind '{kind}' already exists.");
    }

    private async Task<Stored<TEntity>?> LoadAsync<TEntity>(string kind, string id, CancellationToken cancellationToken)
        where TEntity : Entity
    {
        var envelope = await store.LoadAsync(kind, id, cancellationToken);
        return envelope is null ? null : Deserialize<TEntity>(envelope, kind);
    }

    private async Task<Stored<TEntity>?> FindSingleAsync<TEntity>(
        string kind,
        string index,
        string value,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        var matches = await QueryAsync<TEntity>(kind, index, value, cancellationToken);
        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw Conflict($"Multiple '{kind}' documents match '{value}'.")
        };
    }

    private async Task<IReadOnlyList<Stored<TEntity>>> ListAllAsync<TEntity>(
        string kind,
        string collection,
        CancellationToken cancellationToken)
        where TEntity : Entity =>
        await QueryAsync<TEntity>(kind, ActivitiesDesignStorageManifest.ByCollectionIndex, collection, cancellationToken);

    private async Task<IReadOnlyList<Stored<TEntity>>> QueryAsync<TEntity>(
        string kind,
        string index,
        string value,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        var (queryIdentity, fieldPath) = index switch
        {
            ActivitiesDesignStorageManifest.ByCollectionIndex => (ActivitiesDesignStorageManifest.ListAllQuery, ActivitiesDesignStorageManifest.CollectionField),
            ActivitiesDesignStorageManifest.ByDefinitionIndex => ("list-by-definition", ActivitiesDesignStorageManifest.DefinitionIdField),
            ActivitiesDesignStorageManifest.ByHeadVersionIndex => ("list-by-head-version", ActivitiesDesignStorageManifest.HeadVersionIdField),
            ActivitiesDesignStorageManifest.ByDraftIndex => ("list-by-draft", ActivitiesDesignStorageManifest.DraftIdField),
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex => ("list-by-definition-version", ActivitiesDesignStorageManifest.DefinitionVersionIdField),
            ActivitiesDesignStorageManifest.ByOwnerVersionIndex => ("list-by-owner-version", ActivitiesDesignStorageManifest.OwnerVersionIdField),
            ActivitiesDesignStorageManifest.ByDependencyVersionIndex => ("list-by-dependency-version", ActivitiesDesignStorageManifest.DependencyVersionIdField),
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "The activity-design query index is not declared.")
        };
        var result = await (boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
                "Reusable-activity design queries require an admitted bounded document-store runtime."))
            .QueryAsync(
                new DocumentQuery(
                    kind,
                    queryIdentity,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))]),
                cancellationToken);
        return result.Documents.Select(x => Deserialize<TEntity>(x, kind)).ToArray();
    }

    private async Task<StoredPage<TEntity>> QueryPageAsync<TEntity>(
        string kind,
        string queryIdentity,
        string fieldPath,
        string value,
        int offset,
        int limit,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        var result = await (boundedStore ?? store as IBoundedDocumentStore ?? throw new InvalidOperationException(
                "Reusable-activity management queries require an admitted bounded document-store runtime."))
            .QueryAsync(
                new DocumentQuery(
                    kind,
                    queryIdentity,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))],
                    null,
                    offset,
                    limit),
                cancellationToken);
        return new(result.Documents.Select(x => Deserialize<TEntity>(x, kind)).ToArray(), result.TotalCount);
    }

    private async Task<IReadOnlyList<Stored<TEntity>>> ReadAllPagesAsync<TEntity>(
        string kind,
        string queryIdentity,
        string fieldPath,
        string value,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        const int pageSize = 100;
        var items = new List<Stored<TEntity>>();
        var offset = 0;
        while (true)
        {
            var page = await QueryPageAsync<TEntity>(kind, queryIdentity, fieldPath, value, offset, pageSize, cancellationToken);
            items.AddRange(page.Documents);
            offset += page.Documents.Count;
            if (page.Documents.Count == 0 || offset >= page.TotalCount)
                return items;
        }
    }

    private async Task<ActivityDefinitionManagementRecord?> ManagementRecordAsync(
        ActivityDefinitionAuthoringState authoring,
        DateTimeOffset asOf,
        CancellationToken cancellationToken)
    {
        var definition = await LoadAsync<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            authoring.DefinitionId,
            cancellationToken);
        if (definition is null || definition.Entity.LastModifiedAt > asOf ||
            !StringComparer.Ordinal.Equals(definition.Entity.TenantId, authoring.TenantId))
            return null;
        var draftRows = await ReadAllPagesAsync<ActivityDefinitionDraft>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            "list-by-definition",
            ActivitiesDesignStorageManifest.DefinitionIdField,
            authoring.DefinitionId,
            cancellationToken);
        var versionRows = await ReadAllPagesAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            "list-by-definition",
            ActivitiesDesignStorageManifest.DefinitionIdField,
            authoring.DefinitionId,
            cancellationToken);
        var drafts = draftRows.Select(x => x.Entity).Where(x => x.LastModifiedAt <= asOf).ToArray();
        var versions = versionRows.Select(x => x.Entity).Where(x => x.LastModifiedAt <= asOf).ToArray();
        var versionsById = versions.ToDictionary(x => x.DefinitionVersionId, StringComparer.Ordinal);
        return ManagementRecord(
            authoring,
            new Dictionary<string, ActivityDefinition>(StringComparer.Ordinal) { [definition.Entity.Id] = definition.Entity },
            drafts,
            versions,
            versionsById);
    }

    private static ActivityDefinitionManagementRecord? ManagementRecord(
        ActivityDefinitionAuthoringState authoring,
        IReadOnlyDictionary<string, ActivityDefinition> definitions,
        IReadOnlyList<ActivityDefinitionDraft> drafts,
        IReadOnlyList<ActivityDefinitionVersionPublication> versions,
        IReadOnlyDictionary<string, ActivityDefinitionVersionPublication> versionsById)
    {
        if (!definitions.TryGetValue(authoring.DefinitionId, out var definition) ||
            !StringComparer.Ordinal.Equals(definition.TenantId, authoring.TenantId))
            return null;
        versionsById.TryGetValue(authoring.HeadVersionId ?? string.Empty, out var head);
        versionsById.TryGetValue(authoring.RecommendedVersionId ?? string.Empty, out var recommendation);
        if (head is not null && (!StringComparer.Ordinal.Equals(head.DefinitionId, authoring.DefinitionId) ||
                                 !StringComparer.Ordinal.Equals(head.TenantId, authoring.TenantId)))
            head = null;
        if (recommendation is not null && (!StringComparer.Ordinal.Equals(recommendation.DefinitionId, authoring.DefinitionId) ||
                                           !StringComparer.Ordinal.Equals(recommendation.TenantId, authoring.TenantId)))
            recommendation = null;
        return new(
            definition,
            authoring,
            head,
            recommendation,
            drafts.LongCount(x => StringComparer.Ordinal.Equals(x.DefinitionId, authoring.DefinitionId) &&
                                  StringComparer.Ordinal.Equals(x.TenantId, authoring.TenantId)),
            versions.LongCount(x => StringComparer.Ordinal.Equals(x.DefinitionId, authoring.DefinitionId) &&
                                    StringComparer.Ordinal.Equals(x.TenantId, authoring.TenantId)));
    }

    private static bool Matches(ActivityDefinitionManagementRecord record, ActivityManagementPageQuery query)
    {
        if (query.Authority is { } authority && record.Authoring.ContentAuthority.Kind != authority)
            return false;
        if (query.ProviderKey is { } provider &&
            !StringComparer.Ordinal.Equals(record.Head?.Provider.ProviderKey, provider) &&
            !StringComparer.Ordinal.Equals(record.Recommendation?.Provider.ProviderKey, provider))
            return false;
        return query.Search is null ||
               Contains(record.Definition.DisplayName, query.Search) ||
               Contains(record.Definition.ActivityTypeKey, query.Search) ||
               Contains(record.Definition.Category, query.Search) ||
               Contains(record.Definition.Description, query.Search);
    }

    private static bool Contains(string? value, string search) =>
        value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

    private static void ValidateManagementQuery(ActivityManagementPageQuery query)
    {
        if (query.Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(query.Offset));
        if (query.Limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query.Limit));
    }

    private static Stored<TEntity> Deserialize<TEntity>(DocumentEnvelope envelope, string kind)
        where TEntity : Entity
    {
        var document = JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, JsonOptions);
        return document?.Entity is { } entity
            ? new Stored<TEntity>(entity, envelope)
            : throw new InvalidOperationException($"Document '{envelope.Id}' of kind '{kind}' could not be deserialized as {typeof(TEntity).Name}.");
    }

    private static SaveDocumentRequest Save<TEntity>(string kind, string collection, TEntity entity, long expectedVersion)
        where TEntity : Entity
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, JsonOptions);
        return new SaveDocumentRequest(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private static void ValidateCreate(CreateActivityDefinitionRequest request)
    {
        if (!StringComparer.Ordinal.Equals(request.Definition.Id, request.AuthoringState.DefinitionId) ||
            !StringComparer.Ordinal.Equals(request.Definition.Id, request.InitialDraft.DefinitionId))
            throw new ArgumentException("Definition, authoring-state, and draft definition identities must match.", nameof(request));
        if (!StringComparer.Ordinal.Equals(request.Definition.TenantId, request.AuthoringState.TenantId) ||
            !StringComparer.Ordinal.Equals(request.Definition.TenantId, request.InitialDraft.TenantId) ||
            !StringComparer.Ordinal.Equals(request.Definition.TenantId, request.InitialLayout.TenantId))
            throw new ArgumentException("Definition, authoring-state, draft, and layout tenants must match.", nameof(request));
        ValidateDraftAndLayout(request.InitialDraft, request.InitialLayout);
    }

    private static void ValidateDraftAndLayout(ActivityDefinitionDraft draft, ActivityDefinitionDraftLayout layout)
    {
        if (!StringComparer.Ordinal.Equals(draft.Id, layout.DraftId))
            throw new ArgumentException("Draft and layout identities must match.");
        if (!StringComparer.Ordinal.Equals(draft.TenantId, layout.TenantId))
            throw new ArgumentException("Draft and layout tenants must match.");
        if (draft.Revision != layout.Revision)
            throw new ArgumentException("Draft and layout revisions must match.");
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw new ArgumentException("A newly created draft must be active.");
    }

    private static void EnsureActiveRevision(ActivityDefinitionDraft draft, long expectedRevision)
    {
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict($"Activity draft '{draft.Id}' is {draft.Status}, not Active.");
        if (draft.Revision != expectedRevision)
            throw Conflict($"Activity draft '{draft.Id}' is at revision {draft.Revision}, not {expectedRevision}.");
    }

    private static void EnsureDesignAuthority(ActivityDefinitionAuthoringState authoring)
    {
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Conflict($"Activity definition '{authoring.DefinitionId}' is owned by provider source '{authoring.ContentAuthority.AuthorityKey}'.");
    }

    private static void EnsureExpectedHead(ActivityDefinitionAuthoringState authoring, string? expectedHead)
    {
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, expectedHead))
            throw Conflict($"Activity definition '{authoring.DefinitionId}' head is '{authoring.HeadVersionId}', not '{expectedHead}'.");
    }

    private static void EnsureTenant(ActivityDefinitionAuthoringState authoring, ActivityDefinitionDraft draft)
    {
        if (!StringComparer.Ordinal.Equals(authoring.TenantId, draft.TenantId))
            throw Conflict($"Activity draft '{draft.Id}' tenant does not match definition '{authoring.DefinitionId}'.");
    }

    private static bool IsAllowedTransition(ActivityDefinitionVersionLifecycle current, ActivityDefinitionVersionLifecycle next) =>
        (current, next) switch
        {
            (ActivityDefinitionVersionLifecycle.Active, ActivityDefinitionVersionLifecycle.Retired) => true,
            (ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Active) => true,
            (ActivityDefinitionVersionLifecycle.Active or ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Revoked) => true,
            _ => false
        };

    private static IReadOnlyList<TraversedEdge> Traverse(
        string rootVersionId,
        ActivityDependencyQuery query,
        IReadOnlyList<ActivityDependencyEdge> edges)
    {
        var result = new List<TraversedEdge>();
        var queue = new Queue<(string VersionId, int Depth, DependencyPathNode Path)>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { rootVersionId };
        var adjacency = edges
            .GroupBy(x => query.Direction == ActivityDependencyDirection.Outbound ? x.OwnerVersionId : x.DependencyVersionId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(edge => edge.OccurrenceId, StringComparer.Ordinal).ThenBy(edge => edge.Id, StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        queue.Enqueue((rootVersionId, 1, new(rootVersionId, null)));

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!adjacency.TryGetValue(current.VersionId, out var candidates)) continue;
            foreach (var edge in candidates)
            {
                var next = query.Direction == ActivityDependencyDirection.Outbound ? edge.DependencyVersionId : edge.OwnerVersionId;
                var path = new DependencyPathNode(next, current.Path);
                result.Add(new TraversedEdge(edge, current.Depth, path));
                if (query.Transitive && visited.Add(next))
                    queue.Enqueue((next, current.Depth + 1, path));
            }

            if (!query.Transitive)
                break;
        }

        return result
            .OrderBy(x => x.Depth)
            .ThenBy(x => x.Edge.OwnerVersionId, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.DependencyVersionId, StringComparer.Ordinal)
            .ThenBy(x => x.Edge.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static ActivityDependencyItem ToItem(
        TraversedEdge traversed,
        IReadOnlyDictionary<string, ActivityDefinitionVersionPublication> publications) => new(
        traversed.Edge.Id,
        ToReference(RequiredPublication(traversed.Edge.OwnerVersionId, publications)),
        ToReference(RequiredPublication(traversed.Edge.DependencyVersionId, publications)),
        new ActivityDependencyOccurrence(traversed.Edge.OccurrenceId, traversed.Edge.NodeOrigin.ToArray()),
        traversed.Depth == 1,
        traversed.Depth,
        MaterializePath(traversed.Path).Select(x => ToReference(RequiredPublication(x, publications))).ToArray());

    private static IReadOnlyList<string> MaterializePath(DependencyPathNode path)
    {
        var values = new string[path.Depth];
        for (var current = path; current is not null; current = current.Parent)
            values[current.Depth - 1] = current.VersionId;
        return values;
    }

    private static ActivityDefinitionVersionPublication RequiredPublication(
        string versionId,
        IReadOnlyDictionary<string, ActivityDefinitionVersionPublication> publications) =>
        publications.GetValueOrDefault(versionId)
        ?? throw new InvalidOperationException($"Dependency edge references missing activity version publication '{versionId}'.");

    private static ActivityDefinitionReference ToReference(ActivityDefinitionVersionPublication publication) => new(
        "ActivityVersion",
        publication.DefinitionId,
        publication.DefinitionVersionId,
        publication.Version,
        TemplateHash: publication.TemplateHash,
        TenantId: publication.TenantId,
        Lifecycle: publication.Lifecycle);

    private static string Fingerprint(
        IReadOnlyList<Stored<ActivityDependencyEdge>> edges,
        IReadOnlyList<Stored<ActivityDefinitionVersionPublication>> publications)
    {
        var canonicalEdges = edges
            .OrderBy(x => x.Entity.Id, StringComparer.Ordinal)
            .Select(x => $"edge\u001f{x.Entity.Id}\u001f{x.Envelope.Version}\u001f{x.Entity.OwnerVersionId}\u001f{x.Entity.DependencyVersionId}");
        var canonicalPublications = publications
            .OrderBy(x => x.Entity.DefinitionVersionId, StringComparer.Ordinal)
            .Select(x => $"publication\u001f{x.Entity.DefinitionVersionId}\u001f{x.Envelope.Version}");
        var canonical = string.Join('\n', canonicalEdges.Concat(canonicalPublications));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string DefinitionKeyLock(string? tenantId, string activityTypeKey)
    {
        var key = $"{tenantId ?? "<global>"}\u001f{activityTypeKey}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return $"elsa:activities:design:definition-key:{hash}";
    }

    private static bool IsVisible(string? itemTenantId, string? tenantId) =>
        itemTenantId is null || StringComparer.Ordinal.Equals(itemTenantId, tenantId);

    private static InvalidOperationException Conflict(string message, Exception? innerException = null) => new(message, innerException);

    private static KeyNotFoundException Missing(string message) => new(message);

    private sealed record Stored<TEntity>(TEntity Entity, DocumentEnvelope Envelope) where TEntity : Entity;
    private sealed record StoredPage<TEntity>(IReadOnlyList<Stored<TEntity>> Documents, long TotalCount) where TEntity : Entity;

    private sealed record TraversedEdge(ActivityDependencyEdge Edge, int Depth, DependencyPathNode Path);

    private sealed record DependencyPathNode(string VersionId, DependencyPathNode? Parent)
    {
        public int Depth { get; } = (Parent?.Depth ?? 0) + 1;
    }

}

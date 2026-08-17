using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Constants;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Locking.Core;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Entities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    IBoundedDocumentStore boundedStore,
    GroundworkActivityManagementProjectionWriter managementProjectionWriter) :
    IActivityDefinitionAuthoringStore,
    IActivityDefinitionDraftStore,
    IActivityDefinitionVersionPublicationStore,
    IActivityDefinitionLayoutStore,
    IActivityDraftValidationStore,
    IActivityForkStore,
    IActivityDirectDependencyStore,
    IActivityDependencyProjectionStore,
    ICreateActivityDefinitionCommand,
    ISaveActivityForkCandidateCommand,
    IPruneActivityForkCandidatesCommand,
    IApplyActivityForkCandidateCommand,
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

    private const string ListByDefinitionQuery = "list-by-definition";
    private const string ListByOwnerVersionQuery = "list-by-owner-version";

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
        // Selective shaped reads on the by-definition route: the requested ids are canonicalized into
        // deterministic bounded batches so an oversized caller set never exceeds the declared IN
        // cardinality a single membership comparison may carry.
        var states = new List<ActivityDefinitionAuthoringState>();
        foreach (var batch in GroundworkMembershipBatches.Create(definitionIds))
        {
            var documents = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
                boundedStore,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ListByDefinitionQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.In(ActivitiesDesignStorageManifest.DefinitionIdField, batch))],
                ActivitiesDesignStorageManifest.ByDefinitionDocumentOrder,
                cancellationToken);
            states.AddRange(documents.Select(x => Deserialize<ActivityDefinitionAuthoringState>(
                x,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind).Entity));
        }

        return states;
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

    public async Task<ActivityForkCandidate?> FindCandidateAsync(
        string candidateId,
        CancellationToken cancellationToken = default) =>
        (await LoadAsync<ActivityForkCandidate>(
            ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
            candidateId,
            cancellationToken))?.Entity;

    public async Task<ActivityForkReceipt?> FindReceiptAsync(
        string receiptId,
        CancellationToken cancellationToken = default) =>
        (await LoadAsync<ActivityForkReceipt>(
            ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind,
            receiptId,
            cancellationToken))?.Entity;

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

        var publicationDocuments = await TraverseAsync<ActivityDefinitionVersionPublication>(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ListByDefinitionQuery,
            ActivitiesDesignStorageManifest.ByDefinitionDocumentOrder,
            cancellationToken);
        var publications = publicationDocuments
            .Select(x => x.Entity)
            .ToDictionary(x => x.DefinitionVersionId, StringComparer.Ordinal);
        if (!publications.TryGetValue(request.RootVersionId, out var root))
            throw Missing($"Activity version publication '{request.RootVersionId}' was not found.");

        var edgeDocuments = await TraverseAsync<ActivityDependencyEdge>(
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            ListByOwnerVersionQuery,
            ActivitiesDesignStorageManifest.ByOwnerVersionDocumentOrder,
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
            ActivityDesignPersistenceLockKeys.DefinitionTypeKey(
                request.Definition.TenantId,
                request.Definition.ActivityTypeKey),
            null,
            cancellationToken);

        var existingDefinitions = await ListDefinitionsByTypeKeyAsync(request.Definition.ActivityTypeKey, cancellationToken);
        if (existingDefinitions.Any(x =>
                StringComparer.Ordinal.Equals(x.Entity.TenantId, request.Definition.TenantId)))
            throw Conflict($"Activity definition key '{request.Definition.ActivityTypeKey}' already exists.");

        await EnsureAbsentAsync<ActivityDefinition>(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, request.Definition.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionAuthoringState>(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, request.AuthoringState.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionDraft>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, request.InitialDraft.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionDraftLayout>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, request.InitialLayout.Id, cancellationToken);
        if (await FindAsync(request.Definition.Id, cancellationToken) is not null)
            throw Conflict($"Activity definition authoring state '{request.Definition.Id}' already exists.");
        if (await FindDraftLayoutAsync(request.InitialDraft.Id, cancellationToken) is not null)
            throw Conflict($"Activity draft layout '{request.InitialDraft.Id}' already exists.");

        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionCollection, request.Definition, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection, request.AuthoringState, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, request.InitialDraft, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, request.InitialLayout, 0)
            ],
            new(
                request.Definition.LastModifiedAt,
                [new(request.Definition, request.AuthoringState)],
                [request.InitialDraft],
                []),
            cancellationToken);
    }

    public async Task<ActivityForkCandidate> ExecuteAsync(
        SaveActivityForkCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateForkCandidate(request.Candidate);
        await using var lockHandle = await lockProvider.AcquireLockAsync(
            ForkCandidateLock(request.Candidate.Id),
            null,
            cancellationToken);
        var existing = await LoadAsync<ActivityForkCandidate>(
            ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
            request.Candidate.Id,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.Entity.ExpiresAt <= request.Candidate.CreatedAt)
                throw new ActivityForkPreviewExpiredException("The reviewed fork reservation expired.");
            EnsureExactPreviewReplay(existing.Entity, request.Candidate);
            return existing.Entity;
        }
        await store.SaveAsync(
            Save(
                ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityForkCandidateCollection,
            request.Candidate,
            0),
            cancellationToken);
        return request.Candidate;
    }

    public async Task<int> ExecuteAsync(
        DateTimeOffset retainBefore,
        int maximumCount = 100,
        CancellationToken cancellationToken = default)
    {
        if (maximumCount is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(maximumCount));
        var result = await boundedStore.QueryAsync(
            new DocumentQuery(
                ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityForkCandidateExpiredQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
                    ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionField,
                    ActivityForkCandidateIdentity.RetentionKey(retainBefore)))],
                null,
                0,
                maximumCount),
            cancellationToken);
        if (result.Documents.Count == 0)
            return 0;
        await store.WriteAllAsync(
            DocumentCommitScope.Of(ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind),
            result.Documents.Select(x => DocumentWriteOperation.Delete(new DeleteDocumentRequest(
                x.DocumentKind,
                x.Id,
                x.Version))).ToArray(),
            cancellationToken);
        return result.Documents.Count;
    }

    public async Task<ActivityForkApplyResult> ExecuteAsync(
        ApplyActivityForkCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var operationLock = await lockProvider.AcquireLockAsync(
            ForkApplyLock(request.ReceiptId),
            null,
            cancellationToken);

        var existingReceipt = await LoadAsync<ActivityForkReceipt>(
            ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind,
            request.ReceiptId,
            cancellationToken);
        if (existingReceipt is not null)
        {
            EnsureExactReplay(existingReceipt.Entity, request);
            return new(existingReceipt.Entity, true);
        }

        var candidate = await LoadAsync<ActivityForkCandidate>(
            ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
            request.CandidateId,
            cancellationToken)
            ?? throw new ActivityForkCandidateStaleException("The activity fork candidate no longer exists.");
        EnsureApplicable(candidate.Entity, request);
        ValidateForkCandidate(candidate.Entity);

        await using var sourceLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(
                candidate.Entity.SourceDefinitionId),
            null,
            cancellationToken);
        await using var keyLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DefinitionTypeKey(
                candidate.Entity.ReservedDefinition.TenantId,
                candidate.Entity.ReservedDefinition.ActivityTypeKey),
            null,
            cancellationToken);

        await RecheckForkSourceAsync(candidate.Entity, cancellationToken);
        var existingDefinitions = await ListDefinitionsByTypeKeyAsync(
            candidate.Entity.ReservedDefinition.ActivityTypeKey,
            cancellationToken);
        if (existingDefinitions.Any(x =>
                StringComparer.Ordinal.Equals(x.Entity.TenantId, candidate.Entity.ReservedDefinition.TenantId)))
            throw new ActivityForkCollisionException("The reserved activity type key is no longer available.");

        try
        {
            await EnsureAbsentAsync<ActivityDefinition>(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                candidate.Entity.ReservedDefinition.Id,
                cancellationToken);
            await EnsureAbsentAsync<ActivityDefinitionAuthoringState>(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                candidate.Entity.ReservedAuthoringState.Id,
                cancellationToken);
            await EnsureAbsentAsync<ActivityDefinitionDraft>(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                candidate.Entity.ReservedDraft.Id,
                cancellationToken);
            await EnsureAbsentAsync<ActivityDefinitionDraftLayout>(
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
                candidate.Entity.ReservedLayout.Id,
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new ActivityForkCollisionException(exception.Message);
        }

        candidate.Entity.Status = ActivityForkCandidateStatus.Applied;
        candidate.Entity.AppliedIdempotencyKey = request.IdempotencyKey;
        candidate.Entity.LastModifiedAt = request.AppliedAt;
        var receipt = new ActivityForkReceipt
        {
            Id = request.ReceiptId,
            TenantId = candidate.Entity.TenantId,
            IdempotencyKey = request.IdempotencyKey,
            CandidateId = candidate.Entity.Id,
            PublicCandidateId = candidate.Entity.CandidateId,
            RequestFingerprint = candidate.Entity.RequestFingerprint,
            AccessBindingFingerprint = candidate.Entity.AccessBindingFingerprint,
            ActorId = candidate.Entity.ActorId,
            AuthorizationProfile = candidate.Entity.AuthorizationProfile,
            Status = ActivityForkReceiptStatus.Applied,
            DefinitionId = candidate.Entity.ReservedDefinition.Id,
            ActivityTypeKey = candidate.Entity.ReservedDefinition.ActivityTypeKey,
            DraftId = candidate.Entity.ReservedDraft.Id,
            Definition = candidate.Entity.ReservedDefinition,
            AuthoringState = candidate.Entity.ReservedAuthoringState,
            Draft = candidate.Entity.ReservedDraft,
            AppliedAt = request.AppliedAt,
            CreatedAt = request.AppliedAt,
            LastModifiedAt = request.AppliedAt
        };

        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(
                    ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityForkCandidateCollection,
                    candidate.Entity,
                    candidate.Envelope.Version),
                Save(
                    ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityForkReceiptCollection,
                    receipt,
                    0),
                Save(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                    candidate.Entity.ReservedDefinition,
                    0),
                Save(
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
                    candidate.Entity.ReservedAuthoringState,
                    0),
                Save(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection,
                    candidate.Entity.ReservedDraft,
                    0),
                Save(
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection,
                    candidate.Entity.ReservedLayout,
                    0)
            ],
            new(
                request.AppliedAt,
                [new(candidate.Entity.ReservedDefinition, candidate.Entity.ReservedAuthoringState)],
                [candidate.Entity.ReservedDraft],
                []),
            cancellationToken);
        return new(receipt, false);
    }

    private async Task RecheckForkSourceAsync(
        ActivityForkCandidate candidate,
        CancellationToken cancellationToken)
    {
        var authoring = await RequiredAuthoringAsync(candidate.SourceDefinitionId, cancellationToken);
        var publication = await RequiredPublicationAsync(candidate.SourceVersionId, cancellationToken);
        if (authoring.Entity.ContentAuthority.Kind != ActivityContentAuthorityKind.ProviderSource ||
            !StringComparer.Ordinal.Equals(authoring.Entity.TenantId, candidate.TenantId) ||
            !StringComparer.Ordinal.Equals(publication.Entity.TenantId, candidate.TenantId) ||
            !StringComparer.Ordinal.Equals(publication.Entity.DefinitionId, candidate.SourceDefinitionId) ||
            !StringComparer.Ordinal.Equals(publication.Entity.Version, candidate.SourceVersion) ||
            publication.Entity.Lifecycle != candidate.SourceLifecycle ||
            !StringComparer.Ordinal.Equals(
                ActivityProviderManifestFingerprint.Compute(publication.Entity.Provider),
                candidate.SourceProviderFingerprint) ||
            !StringComparer.Ordinal.Equals(
                ActivityForkMaterialFingerprint.Compute(publication.Entity.Contract),
                candidate.SourceContractFingerprint))
            throw new ActivityForkCandidateStaleException(
                "The exact source version or authority changed before the atomic fork commit.");
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

        await CommitWithManagementProjectionAsync(
            [ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind],
            [Save(
                ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionCollection,
                definition.Entity,
                definition.Envelope.Version)],
            new(
                request.LastModifiedAt,
                [new(definition.Entity, authoring.Entity)],
                [],
                []),
            cancellationToken);
        return definition.Entity;
    }

    public async Task ExecuteAsync(CreateActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(request.Draft.Id),
            null,
            cancellationToken);
        ValidateDraftAndLayout(request.Draft, request.Layout);
        var authoring = await RequiredAuthoringAsync(request.Draft.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureExpectedHead(authoring.Entity, request.ExpectedDefinitionHeadVersionId);
        EnsureTenant(authoring.Entity, request.Draft);

        await EnsureAbsentAsync<ActivityDefinitionDraft>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, request.Draft.Id, cancellationToken);
        await EnsureAbsentAsync<ActivityDefinitionDraftLayout>(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, request.Layout.Id, cancellationToken);
        if (await FindDraftLayoutAsync(request.Draft.Id, cancellationToken) is not null)
            throw Conflict($"Activity draft layout '{request.Draft.Id}' already exists.");

        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection, authoring.Entity, authoring.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, request.Draft, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, request.Layout, 0)
            ],
            new(request.Draft.LastModifiedAt, [], [request.Draft], []),
            cancellationToken);
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(
        UpdateActivityDraftPresentationRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(request.DraftId),
            null,
            cancellationToken);
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
        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout.Entity, layout.Envelope.Version)
            ],
            new(request.ChangedAt, [], [draft.Entity], []),
            cancellationToken);
        return draft.Entity;
    }

    public async Task ExecuteAsync(
        CreateActivityDraftConflictCopyRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(request.SourceDraftId),
            null,
            cancellationToken);
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
        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, source.Entity, source.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, request.ConflictCopy, 0),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, request.Layout, 0)
            ],
            new(request.ConflictCopy.LastModifiedAt, [], [request.ConflictCopy], []),
            cancellationToken);
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(
        ReplaceActivityDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(request.DraftId),
            null,
            cancellationToken);
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

        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout.Entity, layout.Envelope.Version)
            ],
            new(now, [], [draft.Entity], []),
            cancellationToken);

        return draft.Entity;
    }

    public async Task<ActivityDefinitionDraft> ExecuteAsync(
        ApplyActivityContractProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(request.DraftId),
            null,
            cancellationToken);
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
        await CommitWithManagementProjectionAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind
            ],
            [
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version),
                Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutCollection, layout.Entity, layout.Envelope.Version)
            ],
            new(now, [], [draft.Entity], []),
            cancellationToken);
        return draft.Entity;
    }

    public async Task ExecuteAsync(DiscardActivityDraftRequest request, CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(request.DraftId),
            null,
            cancellationToken);
        var draft = await RequiredDraftAsync(request.DraftId, cancellationToken);
        EnsureActiveRevision(draft.Entity, request.ExpectedRevision);
        var authoring = await RequiredAuthoringAsync(draft.Entity.DefinitionId, cancellationToken);
        EnsureDesignAuthority(authoring.Entity);
        EnsureTenant(authoring.Entity, draft.Entity);
        draft.Entity.Revision = checked(draft.Entity.Revision + 1);
        draft.Entity.Status = ActivityDefinitionDraftStatus.Discarded;
        draft.Entity.LastModifiedAt = clock.UtcNow;

        await CommitWithManagementProjectionAsync(
            [ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind],
            [Save(ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind, ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection, draft.Entity, draft.Envelope.Version)],
            new(draft.Entity.LastModifiedAt, [], [draft.Entity], []),
            cancellationToken);
    }

    public async Task ExecuteAsync(ActivityDraftValidationState validation, CancellationToken cancellationToken = default)
    {
        await using var draftLock = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.DraftKey(validation.DraftId),
            null,
            cancellationToken);
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
        await using var lockHandle = await lockProvider.AcquireLockAsync(
            ActivityDesignPersistenceLockKeys.PublicationDefinitionKey(publication.Entity.DefinitionId),
            null,
            cancellationToken);
        publication = await RequiredPublicationAsync(request.DefinitionVersionId, cancellationToken);
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
        var definitionChanges = new List<ActivityManagementDefinitionChange>();
        var changesDefinitionReference =
            StringComparer.Ordinal.Equals(authoring.Entity.HeadVersionId, publication.Entity.DefinitionVersionId) ||
            StringComparer.Ordinal.Equals(authoring.Entity.RecommendedVersionId, publication.Entity.DefinitionVersionId) ||
            invalidatesRecommendation;
        if (changesDefinitionReference)
        {
            var definition = await RequiredDefinitionAsync(publication.Entity.DefinitionId, cancellationToken);
            definitionChanges.Add(new(definition.Entity, authoring.Entity));
        }
        await CommitWithManagementProjectionAsync(
            invalidatesRecommendation
                ? [
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind
                ]
                : [ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind],
            requests,
            new(
                publication.Entity.LastModifiedAt,
                definitionChanges,
                [],
                [publication.Entity]),
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
        var definition = await RequiredDefinitionAsync(request.DefinitionId, cancellationToken);
        await CommitWithManagementProjectionAsync(
            target is null
                ? [ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind]
                : [
                    ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                    ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind
                ],
            requests,
            new(request.ChangedAt, [new(definition.Entity, authoring.Entity)], [], []),
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

    private async Task<Stored<ActivityDefinition>> RequiredDefinitionAsync(string definitionId, CancellationToken cancellationToken) =>
        await LoadAsync<ActivityDefinition>(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            definitionId,
            cancellationToken)
        ?? throw Missing($"Activity definition '{definitionId}' was not found.");

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

    // Deterministic full-kind traversal on a declared shaped route: a zero-clause bounded request the
    // route admits, ordered by the route's declared document-id ascending sort.
    private async Task<IReadOnlyList<Stored<TEntity>>> TraverseAsync<TEntity>(
        string kind,
        string queryIdentity,
        IReadOnlyList<DocumentQueryOrder> order,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        var documents = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            boundedStore,
            kind,
            queryIdentity,
            [],
            order,
            cancellationToken);
        return documents.Select(x => Deserialize<TEntity>(x, kind)).ToArray();
    }

    // Selective bounded read of the activity-definition main kind constrained to a single type key on the
    // by-type-key route; callers keep their in-memory tenant comparison over the small result set.
    private async Task<IReadOnlyList<Stored<ActivityDefinition>>> ListDefinitionsByTypeKeyAsync(
        string activityTypeKey,
        CancellationToken cancellationToken)
    {
        var documents = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            boundedStore,
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionsByTypeKeyQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                activityTypeKey))],
            ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder,
            cancellationToken);
        return documents
            .Select(x => Deserialize<ActivityDefinition>(x, ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind))
            .ToArray();
    }

    private async Task<IReadOnlyList<Stored<TEntity>>> QueryAsync<TEntity>(
        string kind,
        string index,
        string value,
        CancellationToken cancellationToken)
        where TEntity : Entity
    {
        var (queryIdentity, fieldPath) = index switch
        {
            ActivitiesDesignStorageManifest.ByDefinitionIndex => (ListByDefinitionQuery, ActivitiesDesignStorageManifest.DefinitionIdField),
            ActivitiesDesignStorageManifest.ByHeadVersionIndex => ("list-by-head-version", ActivitiesDesignStorageManifest.HeadVersionIdField),
            ActivitiesDesignStorageManifest.ByDraftIndex => ("list-by-draft", ActivitiesDesignStorageManifest.DraftIdField),
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex => ("list-by-definition-version", ActivitiesDesignStorageManifest.DefinitionVersionIdField),
            ActivitiesDesignStorageManifest.ByOwnerVersionIndex => (ListByOwnerVersionQuery, ActivitiesDesignStorageManifest.OwnerVersionIdField),
            ActivitiesDesignStorageManifest.ByDependencyVersionIndex => ("list-by-dependency-version", ActivitiesDesignStorageManifest.DependencyVersionIdField),
            _ => throw new ArgumentOutOfRangeException(nameof(index), index, "The activity-design query index is not declared.")
        };
        var documents = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            boundedStore,
            kind,
            queryIdentity,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal(fieldPath, value))],
            IndexHeadOrder(fieldPath),
            cancellationToken);
        return documents.Select(x => Deserialize<TEntity>(x, kind)).ToArray();
    }

    // A by-key route leads its declared order with the index key field, then the document id; every scoped
    // read and every zero-clause traversal on that route must present that same deterministic order.
    private static IReadOnlyList<DocumentQueryOrder> IndexHeadOrder(string keyField) =>
    [
        new(keyField, PhysicalSortDirection.Ascending),
        new(ActivitiesDesignStorageManifest.EntityIdField, PhysicalSortDirection.Ascending)
    ];

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

    private async Task CommitWithManagementProjectionAsync(
        IReadOnlyCollection<string> documentKinds,
        IReadOnlyList<SaveDocumentRequest> requests,
        ActivityManagementProjectionMutation mutation,
        CancellationToken cancellationToken)
    {
        await using var update = await managementProjectionWriter.PrepareAsync(mutation, cancellationToken);
        await update.CommitAsync(documentKinds, requests, cancellationToken);
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

    private static void ValidateForkCandidate(ActivityForkCandidate candidate)
    {
        ValidateCreate(new(
            candidate.ReservedDefinition,
            candidate.ReservedAuthoringState,
            candidate.ReservedDraft,
            candidate.ReservedLayout));
        if (candidate.Status == ActivityForkCandidateStatus.Applied &&
            string.IsNullOrWhiteSpace(candidate.AppliedIdempotencyKey))
            throw new ArgumentException("An applied fork candidate requires its operation identity.", nameof(candidate));
        if (!StringComparer.Ordinal.Equals(candidate.TenantId, candidate.ReservedDefinition.TenantId) ||
            !StringComparer.Ordinal.Equals(candidate.SourceVersionId, candidate.ReservedDraft.SourceVersionId))
            throw new ArgumentException("Fork candidate tenant and exact source bindings must match its reserved authoring material.", nameof(candidate));
        if (candidate.ExpiresAt <= candidate.CreatedAt || candidate.RetainUntil <= candidate.ExpiresAt)
            throw new ArgumentException("Fork candidate expiry and retention must be strictly ordered.", nameof(candidate));
        if (!StringComparer.Ordinal.Equals(
                candidate.RetentionKey,
                ActivityForkCandidateIdentity.RetentionKey(candidate.RetainUntil)))
            throw new ArgumentException("Fork candidate retention key must exactly represent its retention deadline.", nameof(candidate));
    }

    private static void EnsureExactPreviewReplay(ActivityForkCandidate existing, ActivityForkCandidate requested)
    {
        if (!StringComparer.Ordinal.Equals(existing.PreviewIdempotencyKey, requested.PreviewIdempotencyKey) ||
            !StringComparer.Ordinal.Equals(existing.RequestFingerprint, requested.RequestFingerprint) ||
            !StringComparer.Ordinal.Equals(existing.AccessBindingFingerprint, requested.AccessBindingFingerprint) ||
            !StringComparer.Ordinal.Equals(existing.ActorId, requested.ActorId) ||
            !StringComparer.Ordinal.Equals(existing.AuthorizationProfile, requested.AuthorizationProfile))
            throw new ActivityForkPreviewIdempotencyConflictException(
                "The preview operation identity is already bound to different material.");
    }

    private static void EnsureApplicable(
        ActivityForkCandidate candidate,
        ApplyActivityForkCandidateRequest request)
    {
        if (candidate.Status != ActivityForkCandidateStatus.Reserved)
            throw new ActivityForkCandidateStaleException("The activity fork candidate has already been consumed.");
        if (!StringComparer.Ordinal.Equals(candidate.RequestFingerprint, request.RequestFingerprint) ||
            !StringComparer.Ordinal.Equals(candidate.AccessBindingFingerprint, request.AccessBindingFingerprint) ||
            !StringComparer.Ordinal.Equals(candidate.ActorId, request.ActorId) ||
            !StringComparer.Ordinal.Equals(candidate.AuthorizationProfile, request.AuthorizationProfile))
            throw new ActivityForkCandidateStaleException("The activity fork candidate binding changed before commit.");
    }

    private static void EnsureExactReplay(
        ActivityForkReceipt receipt,
        ApplyActivityForkCandidateRequest request)
    {
        if (!StringComparer.Ordinal.Equals(receipt.CandidateId, request.CandidateId) ||
            !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, request.RequestFingerprint) ||
            !StringComparer.Ordinal.Equals(receipt.AccessBindingFingerprint, request.AccessBindingFingerprint) ||
            !StringComparer.Ordinal.Equals(receipt.ActorId, request.ActorId) ||
            !StringComparer.Ordinal.Equals(receipt.AuthorizationProfile, request.AuthorizationProfile) ||
            !StringComparer.Ordinal.Equals(receipt.IdempotencyKey, request.IdempotencyKey))
            throw new ActivityForkIdempotencyConflictException("The activity fork operation identity is already bound to different material.");
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
            if (!adjacency.TryGetValue(current.VersionId, out var candidates))
                continue;
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

    private static string ForkCandidateLock(string candidateId) =>
        $"elsa:activities:design:fork-candidate:{HashLock(candidateId)}";

    private static string ForkApplyLock(string receiptId) =>
        $"elsa:activities:design:fork-apply:{HashLock(receiptId)}";

    private static string HashLock(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static bool IsVisible(string? itemTenantId, string? tenantId) =>
        itemTenantId is null || StringComparer.Ordinal.Equals(itemTenantId, tenantId);

    private static InvalidOperationException Conflict(string message, Exception? innerException = null) => new(message, innerException);

    private static KeyNotFoundException Missing(string message) => new(message);

    private sealed record Stored<TEntity>(TEntity Entity, DocumentEnvelope Envelope) where TEntity : Entity;

    private sealed record TraversedEdge(ActivityDependencyEdge Edge, int Depth, DependencyPathNode Path);

    private sealed record DependencyPathNode(string VersionId, DependencyPathNode? Parent)
    {
        public int Depth { get; } = (Parent?.Depth ?? 0) + 1;
    }

}

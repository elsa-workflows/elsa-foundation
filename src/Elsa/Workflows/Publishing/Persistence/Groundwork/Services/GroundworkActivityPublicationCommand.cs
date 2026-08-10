using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Entities;
using Elsa.Primitives.Versioning;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Core.Services;
using Elsa.Workflows.Publishing.Persistence.Groundwork.Stores;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Services;

/// <summary>
/// Commits the Design publication and Runtime execution material under one cross-unit Groundwork
/// transaction. A provider unable to honor this scope must reject it rather than expose partial state.
/// </summary>
public sealed class GroundworkActivityPublicationCommand(
    IPayloadSerializer payloadSerializer,
    IGroundworkRuntimeDocumentSerializer runtimeSerializer,
    IActivityDefinitionVersionPublicationStore publications,
    GroundworkActivityDependencyProjection dependencyProjection,
    GroundworkActivityManagementProjectionWriter managementProjectionWriter,
    PublishingGroundworkDocumentSerializer publishingSerializer,
    GroundworkLaneTargets laneTargets,
    GroundworkLaneStores laneStores)
    : ICommitActivityPublicationCommand<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt>
{
    private static readonly JsonSerializerOptions DesignJson = GroundworkActivitiesDesignJson.Options;

    /// <summary>
    /// This command reads and writes three lanes, so it cannot use one ambient store: on a split host the
    /// design documents live in a different database from the runtime ones. Every read is routed by the
    /// document kind it addresses. On a single-target host all three resolve to the same store, as before.
    /// </summary>
    private IDocumentStore DesignStore => laneStores.For<ActivitiesDesignGroundworkStorageManifestSource>();

    private IDocumentStore RuntimeStore => laneStores.For<RuntimeGroundworkStorageManifestSource>();

    private IDocumentStore PublishingStore => laneStores.For<PublishingGroundworkStorageManifestSource>();

    private IBoundedDocumentStore DesignBounded => laneStores.BoundedFor<ActivitiesDesignGroundworkStorageManifestSource>();

    private IBoundedDocumentStore RuntimeBounded => laneStores.BoundedFor<RuntimeGroundworkStorageManifestSource>();

    /// <summary>The lane store owning <paramref name="documentKind"/>.</summary>
    private IDocumentStore StoreForKind(string documentKind) =>
        RuntimeKinds.Contains(documentKind) ? RuntimeStore
        : PublishingKinds.Contains(documentKind) ? PublishingStore
        : DesignStore;

    public async Task<ActivityPublicationResult> ExecuteAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var colocated = ActivityPublicationLaneColocation.AreColocated(laneTargets);
        ValidateCommit(commit);

        // A split publication commits in phases, so it can be interrupted after the design commit — which is
        // the point the publication becomes done — but before the receipt lands. The preflight below is
        // create-only and would reject that retry as a duplicate, leaving the receipt unobtainable. Detect
        // the interrupted case first and finish it rather than redoing work that already committed.
        if (!colocated && await TryResumeAtReceiptAsync(commit, cancellationToken) is { } resumed)
            return resumed;

        var draftEnvelope = await RequiredAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            commit.Design.DraftId,
            cancellationToken);
        var draft = DeserializeDesign<ActivityDefinitionDraft>(draftEnvelope);
        var authoringEnvelope = await RequiredAuthoringByDefinitionAsync(commit.Design.DefinitionId, cancellationToken);
        var authoring = DeserializeDesign<ActivityDefinitionAuthoringState>(authoringEnvelope);
        var definitionEnvelope = await RequiredAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind,
            commit.Design.DefinitionId,
            cancellationToken);
        var definition = DeserializeDesign<ActivityDefinition>(definitionEnvelope);
        ValidateExpectedState(commit.Design, draft, authoring);

        await EnsureNewVersionAsync(commit.Design.CatalogVersion, cancellationToken);
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind, commit.Design.Publication.Id, cancellationToken);
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind, commit.Design.Layout.Id, cancellationToken);
        foreach (var edge in commit.Design.DirectDependencies)
            await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind, edge.Id, cancellationToken);
        var runtimeAlreadyCommitted = await RuntimePhaseAlreadyCommittedAsync(commit, colocated, cancellationToken);
        await EnsureAbsentAsync(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            GroundworkActivityPublicationReceiptStore.Id(
                commit.Receipt.TenantId,
                commit.Receipt.IdempotencyKey),
            cancellationToken);

        var requests = new List<SaveDocumentRequest>();
        var templateRequest = await CreateTemplateRequestAsync(commit.ExecutableTemplate, cancellationToken);
        if (templateRequest is not null)
            requests.Add(templateRequest);
        requests.Add(CreateRuntimeRequest(
            ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
            commit.SourceReference.SourceReferenceId,
            new SourceReferenceDocument(
                ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceCollection,
                commit.SourceReference.ArtifactId,
                commit.SourceReference),
            0));
        requests.Add(CreateRichDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionCollection,
            commit.Design.CatalogVersion,
            0));
        requests.Add(CreateReceiptRequest(commit.Receipt));
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationCollection,
            commit.Design.Publication,
            0));
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutCollection,
            commit.Design.Layout,
            0));
        requests.AddRange(commit.Design.DirectDependencies.Select(edge => CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeCollection,
            edge,
            0)));

        var establishesRecommendation = authoring.HeadVersionId is null && authoring.RecommendedVersionId is null;
        draft.Status = ActivityDefinitionDraftStatus.Published;
        draft.PublishedVersionId = commit.Design.Publication.DefinitionVersionId;
        draft.LastModifiedAt = commit.Design.Publication.PublishedAt;
        authoring.HeadVersionId = commit.Design.Publication.DefinitionVersionId;
        if (establishesRecommendation)
            authoring.RecommendedVersionId = commit.Design.Publication.DefinitionVersionId;
        authoring.LastModifiedAt = commit.Design.Publication.PublishedAt;
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftCollection,
            draft,
            draftEnvelope.Version));
        requests.Add(CreateDesignRequest(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateCollection,
            authoring,
            authoringEnvelope.Version));
        var projection = await PrepareProjectionAsync(commit.Design, cancellationToken);
        requests.Add(projection.Request);
        await using var managementProjection = await managementProjectionWriter.PrepareAsync(
            new(
                commit.Design.Publication.PublishedAt,
                [new(definition, authoring)],
                [draft],
                [commit.Design.Publication]),
            cancellationToken);

        try
        {
            if (colocated)
            {
                // One store backs all three lanes, so the whole publication is one transaction and the
                // guarantee is exactly what it has always been.
                await managementProjection.CommitAsync(
                    [
                        ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
                        ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
                        ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
                        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind,
                        PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind
                    ],
                    requests,
                    cancellationToken);
            }
            else
            {
                // The publication instant, not the wall clock: it makes the intent's recorded order a property
                // of the publication rather than of when the process happened to reach this line.
                await CommitAcrossTargetsAsync(
                    managementProjection,
                    requests,
                    commit.Design.Publication.PublishedAt,
                    runtimeAlreadyCommitted,
                    cancellationToken);
            }
        }
        catch (DocumentAtomicWriteException exception)
        {
            throw Conflict($"Atomic activity publication failed at document '{exception.Id}' with status '{exception.Status}'.", exception);
        }

        return new(
            commit.Design.DefinitionId,
            commit.Design.Publication.DefinitionVersionId,
            commit.Design.DraftId,
            commit.ExecutableTemplate.TemplateId,
            commit.SourceReference.SourceReferenceId,
            commit.Design.Publication.PublishedAt);
    }

    /// <summary>Whether this publication's runtime phase already committed; see <see cref="RuntimePublicationPhase"/>.</summary>
    private Task<bool> RuntimePhaseAlreadyCommittedAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
        bool colocated,
        CancellationToken cancellationToken) =>
        RuntimePublicationPhase.AlreadyCommittedAsync(
            RuntimeStore,
            runtimeSerializer,
            commit.SourceReference.SourceReferenceId,
            commit.SourceReference.ArtifactId,
            colocated,
            cancellationToken);

    /// <summary>Builds the create-only receipt write, shared by the ordinary path and the resume path.</summary>
    private SaveDocumentRequest CreateReceiptRequest(ActivityPublicationReceipt receipt)
    {
        var (schemaVersion, content) = publishingSerializer.Serialize(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            receipt);
        return new(
            PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
            GroundworkActivityPublicationReceiptStore.Id(receipt.TenantId, receipt.IdempotencyKey),
            schemaVersion,
            content,
            0);
    }

    private async Task<ActivityPublicationResult?> TryResumeAtReceiptAsync(
        ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit,
        CancellationToken cancellationToken)
    {
        var receiptId = GroundworkActivityPublicationReceiptStore.Id(
            commit.Receipt.TenantId,
            commit.Receipt.IdempotencyKey);
        var publishingStore = laneStores.For<PublishingGroundworkStorageManifestSource>();
        if (await publishingStore.LoadAsync(
                PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind,
                receiptId,
                cancellationToken) is not null)
        {
            // Fully committed already; the preflight reports the duplicate as it always has.
            return null;
        }

        var designStore = laneStores.For<ActivitiesDesignGroundworkStorageManifestSource>();
        var publicationEnvelope = await designStore.LoadAsync(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            commit.Design.Publication.Id,
            cancellationToken);
        if (publicationEnvelope is null)
            return null;

        var stored = DeserializeDesign<ActivityDefinitionVersionPublication>(publicationEnvelope);
        if (!StringComparer.Ordinal.Equals(stored.DefinitionVersionId, commit.Design.Publication.DefinitionVersionId))
            return null;

        await WritePublicationReceiptAsync([CreateReceiptRequest(commit.Receipt)], cancellationToken);
        return new(
            commit.Design.DefinitionId,
            commit.Design.Publication.DefinitionVersionId,
            commit.Design.DraftId,
            commit.ExecutableTemplate.TemplateId,
            commit.SourceReference.SourceReferenceId,
            stored.PublishedAt);
    }

    /// <summary>
    /// Commits a publication whose lanes are on different Groundwork targets, as an ordered sequence rather
    /// than one transaction (there is no cross-store transaction to have). The ordering is what makes every
    /// partial state inert:
    /// <list type="number">
    /// <item>
    /// Runtime first. The template is content-addressed and the source reference is create-only, so a repeat
    /// is a no-op and a template written without the rest is unreachable: nothing resolves it until the
    /// design commit lands. Unreachable is not collectable — the source reference committed alongside it is a
    /// live retention root — so a crash here strands both until a retry adopts them (ADR 0066).
    /// </item>
    /// <item>
    /// Design second, and this is the linearization point. The publication is done when this commits; the
    /// draft and authoring state move under their own optimistic versions, so a concurrent publication still
    /// loses here exactly as before.
    /// </item>
    /// <item>
    /// The publishing receipt last. It is an idempotency artifact derived from the caller's own commit, not
    /// a source of truth, so a crash before it leaves the publication done and the receipt recoverable: a
    /// retry with the same idempotency key resumes at this step rather than redoing the publication.
    /// </item>
    /// </list>
    /// <para>
    /// Step 2 also stages the receipt's post-commit intent, which is what stops step 3 depending on the caller
    /// coming back. Ordering makes each partial state inert; the intent is what makes the last one converge.
    /// It is written inside the design transaction so it is durable at exactly the instant the publication
    /// becomes done — a moment earlier and a rolled-back publication would leave a receipt obligation for a
    /// publication that never happened.
    /// </para>
    /// </summary>
    private async Task CommitAcrossTargetsAsync(
        GroundworkActivityManagementProjectionUpdate managementProjection,
        IReadOnlyList<SaveDocumentRequest> requests,
        DateTimeOffset recordedAt,
        bool runtimeAlreadyCommitted,
        CancellationToken cancellationToken)
    {
        var runtimeRequests = requests.Where(request => RuntimeKinds.Contains(request.DocumentKind)).ToArray();
        var receiptRequests = requests.Where(request => PublishingKinds.Contains(request.DocumentKind)).ToArray();
        var designRequests = requests
            .Where(request => !RuntimeKinds.Contains(request.DocumentKind)
                              && !PublishingKinds.Contains(request.DocumentKind))
            .ToArray();
        if (receiptRequests.Length > 0)
        {
            designRequests =
            [
                .. designRequests,
                GroundworkDesignPostCommitOutbox.CreateSaveRequest(
                    ActivityPublicationReceiptDelivery.CreateIntent(receiptRequests[0]),
                    recordedAt)
            ];
        }

        if (runtimeRequests.Length > 0 && !runtimeAlreadyCommitted)
        {
            await RuntimeStore.SaveAllAsync(
                DocumentCommitScope.Of(RuntimeKinds),
                runtimeRequests,
                cancellationToken);
        }

        await managementProjection.CommitAsync(
            [
                ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
                ActivitiesDesignStorageManifest.ActivityDependencyProjectionDocumentKind,
                GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentDocumentKind
            ],
            designRequests,
            cancellationToken);

        await WritePublicationReceiptAsync(receiptRequests, cancellationToken);
    }

    /// <summary>
    /// Writes the publishing receipt after the design commit, then retires the intent that guaranteed it. The
    /// intent is dropped only once the receipt is durable, so a crash anywhere in between leaves the
    /// obligation standing for <see cref="GroundworkDesignPostCommitRedrive"/> to finish.
    /// </summary>
    private async Task WritePublicationReceiptAsync(
        IReadOnlyList<SaveDocumentRequest> receiptRequests,
        CancellationToken cancellationToken)
    {
        if (receiptRequests.Count == 0)
            return;

        var receipt = receiptRequests[0];
        await ActivityPublicationReceiptDelivery.WriteAsync(
            laneStores.For<PublishingGroundworkStorageManifestSource>(),
            receipt,
            cancellationToken);
        await DesignPostCommitOutbox.DiscardAsync(
            ActivityPublicationReceiptDelivery.CreateIntent(receipt).IntentId,
            cancellationToken);
    }

    /// <summary>
    /// The design lane's post-commit outbox. Addressed by lane rather than injected so it is unmistakably the
    /// design target's: the intent has to be in the same transaction as the design commit to mean anything.
    /// </summary>
    private GroundworkDesignPostCommitOutbox DesignPostCommitOutbox => new(DesignStore, DesignBounded);

    private static readonly string[] RuntimeKinds =
    [
        ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
        ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind
    ];

    private static readonly string[] PublishingKinds =
    [
        PublishingGroundworkStorageManifest.ActivityPublicationReceiptDocumentKind
    ];

    private async Task<ActivityDependencyProjectionPreparedUpdate> PrepareProjectionAsync(
        ActivityPublicationDesignMutation mutation,
        CancellationToken cancellationToken)
    {
        var publication = mutation.Publication;
        var owner = new ActivityDefinitionReference(
            "ActivityVersion",
            publication.DefinitionId,
            publication.DefinitionVersionId,
            publication.Version,
            TemplateHash: publication.TemplateHash,
            TenantId: publication.TenantId,
            Lifecycle: publication.Lifecycle);
        var items = new List<ActivityDependencyItem>(mutation.DirectDependencies.Count);
        foreach (var edge in mutation.DirectDependencies.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            var target = await publications.FindAsync(edge.DependencyVersionId, cancellationToken)
                         ?? throw Conflict($"Dependency publication '{edge.DependencyVersionId}' was not found.");
            var dependency = new ActivityDefinitionReference(
                "ActivityVersion",
                target.DefinitionId,
                target.DefinitionVersionId,
                target.Version,
                TemplateHash: target.TemplateHash,
                TenantId: target.TenantId,
                Lifecycle: target.Lifecycle);
            items.Add(new(
                edge.Id,
                owner,
                dependency,
                new(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
                true,
                1,
                [owner, dependency]));
        }
        var sourceDraft = new ActivityDefinitionReference(
            "ActivityDraft",
            mutation.DefinitionId,
            DraftId: mutation.DraftId,
            Revision: mutation.ExpectedDraftRevision,
            TenantId: publication.TenantId);
        return await dependencyProjection.PrepareUpdateAsync(
            [new(sourceDraft, owner, [], items)],
            publication.PublishedAt,
            cancellationToken);
    }

    private async Task<SaveDocumentRequest?> CreateTemplateRequestAsync(
        ExecutableActivityTemplate template,
        CancellationToken cancellationToken)
    {
        var existing = await RuntimeStore.LoadAsync(ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind, template.TemplateId, cancellationToken);
        if (existing is not null)
        {
            EnsureSameTemplate(runtimeSerializer.Deserialize<TemplateDocument>(existing).Template, template);
            return null;
        }

        var sameHash = await RuntimeBounded.QueryAsync(
            new DocumentQuery(
                ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
                ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ElsaRuntimeStorageManifest.TemplateHashField,
                    template.TemplateHash))],
                take: 1),
            cancellationToken);
        if (sameHash.Documents.Count > 0)
            throw Conflict($"Template hash '{template.TemplateHash}' is already bound to another template identity.");

        return CreateRuntimeRequest(
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind,
            template.TemplateId,
            new TemplateDocument(ElsaRuntimeStorageManifest.ExecutableActivityTemplateCollection, template.TemplateHash, template),
            0);
    }

    private void EnsureSameTemplate(ExecutableActivityTemplate existing, ExecutableActivityTemplate candidate)
    {
        if (!StringComparer.Ordinal.Equals(existing.TemplateHash, candidate.TemplateHash))
            throw Conflict($"Template id '{candidate.TemplateId}' is already bound to a different hash.");
        var existingJson = JsonNode.Parse(runtimeSerializer.SerializeForComparison(existing)) as JsonObject;
        var candidateJson = JsonNode.Parse(runtimeSerializer.SerializeForComparison(candidate)) as JsonObject;
        existingJson?.Remove("createdAt");
        candidateJson?.Remove("createdAt");
        if (!JsonNode.DeepEquals(existingJson, candidateJson))
            throw Conflict($"Template id '{candidate.TemplateId}' and hash '{candidate.TemplateHash}' are already bound to different content.");
    }

    private async Task EnsureNewVersionAsync(ActivityDefinitionVersion candidate, CancellationToken cancellationToken)
    {
        await EnsureAbsentAsync(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind, candidate.Id, cancellationToken);
        // The version kind keeps no doc-id-sorted route after the by-collection removal; a zero-clause
        // traversal on the by-definition route (declared version order) visits every version, and this
        // duplicate check only needs completeness, not a specific visit order.
        var envelopes = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
            DesignBounded,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind,
            ActivitiesDesignStorageManifest.ListActivityDefinitionVersionsByDefinitionQuery,
            [],
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionOrder,
            cancellationToken);
        var richOptions = GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer);
        foreach (var envelope in envelopes)
        {
            var document = JsonSerializer.Deserialize<GroundworkDocument<ActivityDefinitionVersion>>(envelope.ContentJson, richOptions)
                           ?? throw Conflict($"Activity version document '{envelope.Id}' is unreadable.");
            if (StringComparer.Ordinal.Equals(document.Entity.DefinitionId, candidate.DefinitionId) &&
                StringComparer.Ordinal.Equals(document.Entity.SemVerSortKey, candidate.SemVerSortKey))
                throw Conflict($"Activity version '{candidate.Version}' already exists for definition '{candidate.DefinitionId}'.");
        }
    }

    private static void ValidateExpectedState(
        ActivityPublicationDesignMutation mutation,
        ActivityDefinitionDraft draft,
        ActivityDefinitionAuthoringState authoring)
    {
        if (draft.Revision != mutation.ExpectedDraftRevision)
            throw Conflict("The activity draft revision changed before publication committed.");
        if (draft.Status != ActivityDefinitionDraftStatus.Active)
            throw Conflict("The activity draft is no longer active.");
        if (!StringComparer.Ordinal.Equals(draft.DefinitionId, mutation.DefinitionId) ||
            !StringComparer.Ordinal.Equals(authoring.DefinitionId, mutation.DefinitionId))
            throw Conflict("The publication owner identities do not align.");
        if (!StringComparer.Ordinal.Equals(authoring.HeadVersionId, mutation.ExpectedDefinitionHeadVersionId))
            throw Conflict("The activity definition head changed before publication committed.");
        if (authoring.ContentAuthority.Kind != ActivityContentAuthorityKind.Design)
            throw Conflict("The activity definition is not Design-owned.");
    }

    private static void ValidateCommit(ActivityPublicationCommit<ExecutableActivityTemplate, WorkflowExecutableSourceReference, ActivityPublicationReceipt> commit)
    {
        var design = commit.Design;
        var publication = design.Publication;
        var template = commit.ExecutableTemplate;
        var source = commit.SourceReference;
        var receipt = commit.Receipt;
        var expectedRequestFingerprint = ActivityPublicationRequestFingerprint.Compute(
            receipt.DraftId,
            receipt.ExpectedDraftRevision,
            receipt.ExpectedDefinitionHeadVersionId,
            receipt.RequestedVersion,
            receipt.ReviewToken);
        if (!StringComparer.Ordinal.Equals(design.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(design.CatalogVersion.Id, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(design.Layout.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication definition/version identities do not align.", nameof(commit));
        if (receipt.Status != ActivityPublicationReceiptStatus.Applied ||
            receipt.Outcome is null ||
            string.IsNullOrWhiteSpace(receipt.IdempotencyKey) ||
            !StringComparer.Ordinal.Equals(receipt.RequestFingerprint, expectedRequestFingerprint) ||
            string.IsNullOrWhiteSpace(receipt.ReviewToken) ||
            !StringComparer.Ordinal.Equals(receipt.TenantId, commit.OperationTenantId) ||
            !StringComparer.Ordinal.Equals(receipt.DraftId, design.DraftId) ||
            receipt.ExpectedDraftRevision != design.ExpectedDraftRevision ||
            !StringComparer.Ordinal.Equals(receipt.ExpectedDefinitionHeadVersionId, design.ExpectedDefinitionHeadVersionId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DefinitionVersionId, publication.DefinitionVersionId) ||
            !StringComparer.Ordinal.Equals(receipt.RequestedVersion, publication.Version) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.Version, publication.Version) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.DraftId, design.DraftId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(receipt.Outcome.SourceReferenceId, source.SourceReferenceId) ||
            receipt.Outcome.PublishedAt != publication.PublishedAt ||
            receipt.UpdatedAt != publication.PublishedAt)
            throw new ArgumentException("Publication receipt does not match authoritative publication material.", nameof(commit));
        if (!StringComparer.Ordinal.Equals(publication.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(publication.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(source.ArtifactId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(source.SourceReferenceId, publication.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(source.DefinitionId, publication.DefinitionId) ||
            !StringComparer.Ordinal.Equals(source.DefinitionVersionId, publication.DefinitionVersionId))
            throw new ArgumentException("Publication, template, and Source Reference identities do not align.", nameof(commit));
        if (publication.DirectDependencyCount != design.DirectDependencies.Count ||
            template.DirectDependencies.Count != design.DirectDependencies.Count ||
            publication.ClosedTemplateCount != template.ClosedTemplates.Count ||
            publication.ResumeTargetCount != template.ResumeTargets.Count)
            throw new ArgumentException("Publication dependency summary does not match authoritative material.", nameof(commit));
        if (design.DirectDependencies.Select(x => x.OccurrenceId).Distinct(StringComparer.Ordinal).Count() != design.DirectDependencies.Count)
            throw new ArgumentException("Direct dependency occurrence ids must be unique.", nameof(commit));

        var declaredRequirements = publication.RuntimeRequirements
            .Select(x => (x.ConsumerKey, x.SchemaVersion))
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        var actualRequirements = template.RuntimeRequirements
            .Select(x => (x.ConsumerKey, x.SchemaVersion))
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        if (!declaredRequirements.SequenceEqual(actualRequirements))
            throw new ArgumentException("Publication Runtime requirements do not match the template.", nameof(commit));

        foreach (var edge in design.DirectDependencies)
        {
            var dependency = template.DirectDependencies.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.OccurrenceId, edge.OccurrenceId));
            if (dependency is null ||
                !StringComparer.Ordinal.Equals(dependency.DefinitionVersionId, edge.DependencyVersionId) ||
                !StringComparer.Ordinal.Equals(dependency.TemplateHash, edge.DependencyTemplateHash) ||
                !StringComparer.Ordinal.Equals(edge.OwnerVersionId, publication.DefinitionVersionId) ||
                !StringComparer.Ordinal.Equals(edge.OwnerTemplateHash, publication.TemplateHash))
                throw new ArgumentException("A dependency edge does not match the template.", nameof(commit));
        }

        if (!SemVer.TryParse(publication.Version, out var publicationVersion) ||
            !SemVer.TryParse(design.CatalogVersion.Version, out var catalogVersion) ||
            publicationVersion != catalogVersion ||
            !StringComparer.Ordinal.Equals(source.ArtifactVersion, publication.Version))
            throw new ArgumentException("Publication version labels do not align.", nameof(commit));
        if (!StringComparer.Ordinal.Equals(publication.TenantId, design.CatalogVersion.TenantId) ||
            !StringComparer.Ordinal.Equals(publication.TenantId, design.Layout.TenantId) ||
            design.DirectDependencies.Any(x => !StringComparer.Ordinal.Equals(x.TenantId, publication.TenantId)))
            throw new ArgumentException("Publication Design document tenants do not align.", nameof(commit));
    }

    private async Task<DocumentEnvelope> RequiredAsync(string kind, string id, CancellationToken cancellationToken) =>
        await StoreForKind(kind).LoadAsync(kind, id, cancellationToken) ?? throw Conflict($"Required document '{kind}/{id}' was not found.");

    private async Task<DocumentEnvelope> RequiredAuthoringByDefinitionAsync(string definitionId, CancellationToken cancellationToken)
    {
        var matches = await DesignBounded.QueryAsync(
            new DocumentQuery(
                ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
                "list-by-definition",
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(
                    ActivitiesDesignStorageManifest.DefinitionIdField,
                    definitionId))],
                ActivitiesDesignStorageManifest.ByDefinitionDocumentOrder,
                take: 2),
            cancellationToken);
        return matches.Documents.Count switch
        {
            1 => matches.Documents[0],
            0 => throw Conflict($"Authoring state for activity definition '{definitionId}' was not found."),
            _ => throw Conflict($"Multiple authoring states exist for activity definition '{definitionId}'.")
        };
    }

    private async Task EnsureAbsentAsync(string kind, string id, CancellationToken cancellationToken)
    {
        if (await StoreForKind(kind).LoadAsync(kind, id, cancellationToken) is not null)
            throw Conflict($"Document '{kind}/{id}' already exists.");
    }

    private static TEntity DeserializeDesign<TEntity>(DocumentEnvelope envelope)
        where TEntity : Entity =>
        JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, DesignJson)?.Entity
        ?? throw Conflict($"Document '{envelope.DocumentKind}/{envelope.Id}' is unreadable.");

    private static SaveDocumentRequest CreateDesignRequest<TEntity>(string kind, string collection, TEntity entity, long expectedVersion)
        where TEntity : Entity
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(kind, collection, ActivitiesDesignStorageManifest.SchemaVersion, entity, DesignJson);
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private SaveDocumentRequest CreateRichDesignRequest(string kind, string collection, ActivityDefinitionVersion entity, long expectedVersion)
    {
        var request = GroundworkDocumentWriter.ToSaveRequest(
            kind,
            collection,
            ActivitiesDesignStorageManifest.SchemaVersion,
            entity,
            GroundworkActivitiesDesignDocumentSerialization.Create(payloadSerializer));
        return new(request.DocumentKind, request.Id, request.SchemaVersion, request.ContentJson, expectedVersion);
    }

    private SaveDocumentRequest CreateRuntimeRequest<TDocument>(string kind, string id, TDocument document, long expectedVersion)
    {
        var serialized = runtimeSerializer.Serialize(kind, document);
        return new(kind, id, serialized.SchemaVersion, serialized.ContentJson, expectedVersion);
    }

    private static InvalidOperationException Conflict(string message, Exception? innerException = null) => new(message, innerException);

}
